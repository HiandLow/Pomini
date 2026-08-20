using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace PokemonHelper.Services.Recognition;

public sealed class LogFusionVoter : IDisposable
{
	private int _groupId = -1;

	private int _bestScore;

	private int _bestJunk;

	private int _bestHangul;

	private bool _bestVocabHit;

	private string _bestRaw = "";

	private Bitmap? _bestColorFrame;

	private LogCascadeRequest? _pendingCascade;

	internal const int RecentDuplicateMaxEdits = 2;

	private static readonly string[] SpeciesAnchors = new string[20]
	{
		"쓰러졌다", "내보냈다", "곁으로돌아간다", "돌아와", "끌려나왔다", "화상을입었다", "화상을입혔다", "마비되어", "하양허브", "리샘열매",
		"복분열매", "버치열매", "메가진화", "변신했다", "변신하고", "능력변화를복사했다", "풀파워로만들었다", "타입이됐", "배턴터치", "일루전"
	};

	public LogFrameFusion Fusion { get; } = new LogFrameFusion
	{
		VotingMode = true
	};

	public Func<IReadOnlyList<string>>? VocabProvider { get; set; }

	public int CleanMinHangul { get; set; } = 4;

	public bool CascadeEnabled { get; set; }

	private bool BestDamaged
	{
		get
		{
			if (_bestJunk <= 0)
			{
				return _bestHangul < CleanMinHangul;
			}
			return true;
		}
	}

	private bool BestClean
	{
		get
		{
			// [중요 수정] 
			// 텍스트가 한 글자씩 타이핑되는 중에, 우연히 길이 4자 이상 + 포켓몬 이름 일치 조건이 만족되면 
			// 뒤의 글자(예: 배턴터치)가 타이핑되기도 전에 OCR을 조기 종료해버리는 치명적 버그가 있었습니다.
			// 이를 방지하기 위해 조기 종료 조건(BestClean)을 무효화합니다.
			return false;
		}
	}

	public IReadOnlyList<string> Advance(Bitmap? frame, bool changed, long nowMs, Func<Bitmap, string> ocr)
	{
		List<string> list = new List<string>(2);
		IReadOnlyList<string> readOnlyList = null;
		foreach (LogFusionAction item in Fusion.Advance(frame, changed, nowMs))
		{
			using Bitmap bitmap = item.Image;
			switch (item.Kind)
			{
			case LogFusionActionKind.Candidate:
			{
				if (item.GroupId != _groupId)
				{
					_groupId = item.GroupId;
					_bestScore = int.MinValue;
					_bestJunk = int.MaxValue;
					_bestHangul = 0;
					_bestRaw = "";
					DropBestColorFrame();
				}
				else if (BestClean)
				{
					break;
				}
				string text2 = ocr(bitmap);
				if (readOnlyList == null)
				{
					readOnlyList = SafeVocab();
				}
				int num2 = ScoreRaw(text2, readOnlyList);
				if (num2 >= _bestScore)
				{
					_bestScore = num2;
					CountChars(text2, out _bestHangul, out _bestJunk);
					_bestVocabHit = HasVocabHit(text2, readOnlyList);
					_bestRaw = text2;
					list.Add(text2);
					if (CascadeEnabled)
					{
						_bestColorFrame?.Dispose();
						_bestColorFrame = new Bitmap(bitmap);
					}
				}
				break;
			}
			case LogFusionActionKind.CompositeOnClose:
			{
				bool flag = item.GroupId == _groupId;
				if (flag && BestClean)
				{
					DropBestColorFrame();
					break;
				}
				if (item.FrameCount != 1)
				{
					string text = ocr(bitmap);
					if (readOnlyList == null)
					{
						readOnlyList = SafeVocab();
					}
					int num = ScoreRaw(text, readOnlyList);
					if (num > (flag ? _bestScore : int.MinValue))
					{
						list.Add(text);
						if (flag)
						{
							_bestScore = num;
							CountChars(text, out _bestHangul, out _bestJunk);
							_bestVocabHit = HasVocabHit(text, readOnlyList);
							_bestRaw = text;
						}
					}
				}
				bool flag2 = IsSpeciesAnchorMiss(_bestRaw, _bestVocabHit);
				if (flag && CascadeEnabled && _bestColorFrame != null && ((BestDamaged && _bestHangul >= CleanMinHangul && !_bestVocabHit) || flag2))
				{
					if (readOnlyList == null)
					{
						readOnlyList = SafeVocab();
					}
					if (readOnlyList != null && readOnlyList.Count > 0 && _pendingCascade == null)
					{
						try
						{
							double threshold = ((flag2 && !BestDamaged) ? (Fusion.BinarizeThreshold * 0.5) : Fusion.BinarizeThreshold);
							Bitmap bitmap2 = CropToTextBbox(_bestColorFrame, threshold);
							if (bitmap2 != null)
							{
								_pendingCascade = new LogCascadeRequest(bitmap2, _bestScore, readOnlyList.ToArray(), _bestRaw);
							}
						}
						catch
						{
						}
					}
				}
				DropBestColorFrame();
				break;
			}
			default:
				list.Add(ocr(bitmap));
				break;
			}
		}
		return list;
	}

	public void Reset()
	{
		Fusion.Reset();
		_groupId = -1;
		_bestScore = int.MinValue;
		_bestJunk = int.MaxValue;
		_bestHangul = 0;
		_bestVocabHit = false;
		_bestRaw = "";
		DropBestColorFrame();
		_pendingCascade?.Dispose();
		_pendingCascade = null;
	}

	public void Dispose()
	{
		Reset();
	}

	public LogCascadeRequest? TakePendingCascade()
	{
		LogCascadeRequest? pendingCascade = _pendingCascade;
		_pendingCascade = null;
		return pendingCascade;
	}

	public static bool ShouldAdoptSecondOpinion(string second, int bestScore, IReadOnlyList<string>? vocab)
	{
		if (second.Length > 0 && HasVocabHit(second, vocab))
		{
			return ScoreRaw(second, vocab) > bestScore;
		}
		return false;
	}



	private static string KeepHangul(string s)
	{
		StringBuilder stringBuilder = new StringBuilder(s.Length);
		foreach (char c in s)
		{
			if (c >= '가' && c <= '힣')
			{
				stringBuilder.Append(c);
			}
		}
		return stringBuilder.ToString();
	}

	private static int EditDistance(string a, string b, int max)
	{
		int length = a.Length;
		int length2 = b.Length;
		if (Math.Abs(length - length2) > max)
		{
			return max + 1;
		}
		int[] array = new int[length2 + 1];
		int[] array2 = new int[length2 + 1];
		for (int i = 0; i <= length2; i++)
		{
			array[i] = i;
		}
		for (int j = 1; j <= length; j++)
		{
			array2[0] = j;
			int num = array2[0];
			for (int k = 1; k <= length2; k++)
			{
				int num2 = ((a[j - 1] != b[k - 1]) ? 1 : 0);
				array2[k] = Math.Min(Math.Min(array2[k - 1] + 1, array[k] + 1), array[k - 1] + num2);
				if (array2[k] < num)
				{
					num = array2[k];
				}
			}
			if (num > max)
			{
				return max + 1;
			}
			int[] array3 = array2;
			array2 = array;
			array = array3;
		}
		return array[length2];
	}

	internal static bool IsSpeciesAnchorMiss(string raw, bool vocabHit)
	{
		if (vocabHit || raw.Length == 0)
		{
			return false;
		}
		if (TrimNeutralEnd(raw) == "가랏")
		{
			return true;
		}
		string[] speciesAnchors = SpeciesAnchors;
		foreach (string value in speciesAnchors)
		{
			if (raw.Contains(value, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	private static string TrimNeutralEnd(string raw)
	{
		int num;
		for (num = raw.Length; num > 0; num--)
		{
			char c = raw[num - 1];
			bool flag = char.IsWhiteSpace(c);
			if (!flag)
			{
				bool flag2;
				switch (c)
				{
				case '!':
				case ',':
				case '.':
				case ':':
				case '?':
				case '…':
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (!flag)
			{
				break;
			}
		}
		return raw.Substring(0, num);
	}

	private void DropBestColorFrame()
	{
		_bestColorFrame?.Dispose();
		_bestColorFrame = null;
	}

	internal static Bitmap? CropToTextBbox(Bitmap color, double threshold, int pad = 8)
	{
		using Mat mat = color.ToMat();
		using Mat mat2 = new Mat();
		if (mat.Channels() == 4)
		{
			Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGRA2GRAY);
		}
		else if (mat.Channels() == 3)
		{
			Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGR2GRAY);
		}
		else
		{
			mat.CopyTo(mat2);
		}
		using Mat mat3 = new Mat();
		Cv2.Threshold(mat2, mat3, threshold, 255.0, ThresholdTypes.Binary);
		if (Cv2.CountNonZero(mat3) == 0)
		{
			return null;
		}
		using Mat mat4 = new Mat();
		Cv2.FindNonZero(mat3, mat4);
		Rect rect = Cv2.BoundingRect(mat4);
		int num = Math.Max(0, rect.X - pad);
		int num2 = Math.Max(0, rect.Y - pad);
		int num3 = Math.Min(color.Width, rect.X + rect.Width + pad);
		int num4 = Math.Min(color.Height, rect.Y + rect.Height + pad);
		if (num3 - num <= 0 || num4 - num2 <= 0)
		{
			return null;
		}
		return color.Clone(new Rectangle(num, num2, num3 - num, num4 - num2), color.PixelFormat);
	}

	private static bool HasVocabHit(string raw, IReadOnlyList<string>? vocab)
	{
		if (vocab == null || vocab.Count == 0)
		{
			return true;
		}
		foreach (string item in vocab)
		{
			if (item.Length >= 3 && raw.Contains(item, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}

	public static int ScoreRaw(string raw, IReadOnlyList<string>? vocab = null)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return 0;
		}
		CountChars(raw, out var hangul, out var junk);
		// 긴 문장(타이핑이 많이 진행된 문장)이 짧은 사전 단어 매칭을 이길 수 있도록 한글 가중치를 2에서 3으로 상향합니다.
		int num = hangul * 3 - junk * 3;
		if (vocab != null)
		{
			foreach (string item in vocab)
			{
				if (item.Length >= 3 && raw.Contains(item, StringComparison.Ordinal))
				{
					num += item.Length * 3;
				}
			}
		}
		return num;
	}

	private static void CountChars(string raw, out int hangul, out int junk)
	{
		hangul = 0;
		junk = 0;
		foreach (char c in raw)
		{
			if (c >= '가' && c <= '힣')
			{
				hangul++;
				continue;
			}
			bool flag = char.IsWhiteSpace(c);
			if (!flag)
			{
				bool flag2;
				switch (c)
				{
				case '!':
				case ',':
				case '.':
				case ':':
				case '?':
				case '…':
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (!flag)
			{
				junk++;
			}
		}
	}

	private IReadOnlyList<string>? SafeVocab()
	{
		try
		{
			return VocabProvider?.Invoke();
		}
		catch
		{
			return null;
		}
	}
}
