using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;




namespace PokemonHelper.Services.Recognition;

[SupportedOSPlatform("windows")]
public sealed class OpponentPartyRecognizer
{
	public sealed record SlotDiagnostic(int SlotNumber, long ElapsedMs, string LastAttempt);

	public sealed record Result(IReadOnlyList<OpponentPartySlot> Slots, bool Aborted = false, IReadOnlyList<SlotDiagnostic>? SlotDiagnostics = null);

	public const int SlotCount = 6;

	public const double HardFilterMinScore = 0.5;

	public const double LowConfidenceNccThreshold = 0.4;

	public const int MaxReidentifyTries = 3;

	public const double RelaxedTypeMinScore = 0.2;

	private readonly IScreenCapturer _capturer;

	private readonly NccMatcher _ncc;

	private readonly TypeIconMatcher _typeMatcher;

	private readonly ISpritesProvider _provider;

	private string? _captureDir;

	private string? _lowConfidenceDir;

	private DateTime? _passStartTime;

	private bool _passCleanupDone;

	public OpponentPartyRecognizer(IScreenCapturer capturer, NccMatcher ncc, TypeIconMatcher typeMatcher, ISpritesProvider provider)
	{
		_capturer = capturer;
		_ncc = ncc;
		_typeMatcher = typeMatcher;
		_provider = provider;
	}

	public Result Recognize(nint hwnd, IReadOnlyList<RectangleF> slots, IReadOnlyCollection<int> seasonDexIds, IReadOnlyList<RectangleF>? typeSlots = null, IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<PokemonType>>>? dexTypes = null, IReadOnlyDictionary<(int dexId, string formKey), IReadOnlyList<PokemonType>>? spriteTypes = null, Func<bool>? shouldContinue = null)
	{
		int num = Math.Min(slots.Count, 6);
		_passStartTime = DateTime.Now;
		_passCleanupDone = false;
		Dictionary<(int, string), string> nameByKey = _provider.AllSprites.Where((SpriteRecord s) => !string.IsNullOrEmpty(s.NameKo)).ToDictionary((SpriteRecord s) => (DexId: s.DexId, FormKey: s.FormKey), (SpriteRecord s) => s.NameKo);
		Dictionary<int, string> nameByDex = (from s in _provider.AllSprites
			where !string.IsNullOrEmpty(s.NameKo)
			group s by s.DexId).ToDictionary((IGrouping<int, SpriteRecord> g) => g.Key, (IGrouping<int, SpriteRecord> g) => g.First().NameKo);
		bool flag = typeSlots != null && typeSlots.Count > 0 && dexTypes != null && dexTypes.Count > 0 && _typeMatcher.CacheCount > 0;
		OpponentPartySlot[] array = new OpponentPartySlot[num];
		List<SlotDiagnostic> list = new List<SlotDiagnostic>(num);
		bool aborted = false;
		for (int num2 = 0; num2 < num; num2++)
		{
			if (shouldContinue != null && !shouldContinue())
			{
				aborted = true;
				break;
			}
			Stopwatch stopwatch = Stopwatch.StartNew();
			using Bitmap bitmap = _capturer.CaptureWindowRegion(hwnd, slots[num2]);
			TrySaveSlotCapture(bitmap, $"slot{num2 + 1}.png");
			RectangleF rectangleF = ((flag && num2 < typeSlots.Count) ? typeSlots[num2] : default(RectangleF));
			bool flag2 = flag && rectangleF.Width > 0f && rectangleF.Height > 0f;
			OpponentPartySlot opponentPartySlot = new OpponentPartySlot(null, "", "", double.MinValue, Array.Empty<TypeMatch>(), 0);
			List<string> list2 = new List<string>(3);
			for (int num3 = 1; num3 <= 3; num3++)
			{
				IReadOnlyList<TypeMatch> readOnlyList = Array.Empty<TypeMatch>();
				bool flag3 = false;
				if (num3 == 3)
				{
					flag3 = true;
				}
				else if (flag2)
				{
					using Bitmap bitmap2 = _capturer.CaptureWindowRegion(hwnd, rectangleF);
					if (num3 == 1)
					{
						TrySaveSlotCapture(bitmap2, $"slot{num2 + 1}_type.png");
					}
					double minScore = ((num3 == 1) ? 0.3 : 0.2);
					int topN = ((num3 == 1) ? 2 : 3);
					try
					{
						readOnlyList = _typeMatcher.TopN(bitmap2, topN, minScore);
					}
					catch
					{
						readOnlyList = Array.Empty<TypeMatch>();
					}
				}
				IReadOnlyCollection<int> readOnlyCollection = (flag3 ? seasonDexIds : BuildCandidateDexIds(seasonDexIds, readOnlyList, dexTypes));
				bool flag4 = !flag3 && spriteTypes != null && spriteTypes.Count > 0 && readOnlyList.Count > 0;
				int topN2 = ((!flag4) ? 1 : 5);
				IReadOnlyList<NccMatch> readOnlyList2;
				try
				{
					readOnlyList2 = _ncc.TopN(bitmap, topN2, readOnlyCollection, excludeMegaForms: true);
				}
				catch
				{
					readOnlyList2 = Array.Empty<NccMatch>();
				}
				bool flag5 = false;
				IReadOnlyList<NccMatch> readOnlyList4;
				if (flag4 && readOnlyList2.Count > 0)
				{
					IReadOnlyList<NccMatch> readOnlyList3 = FilterByExactSpriteTypeSet(readOnlyList2, readOnlyList, spriteTypes);
					if (readOnlyList3.Count > 0)
					{
						readOnlyList4 = readOnlyList3;
						flag5 = true;
					}
					else
					{
						readOnlyList4 = new NccMatch[1] { readOnlyList2[0] };
					}
				}
				else
				{
					readOnlyList4 = readOnlyList2;
				}
				OpponentPartySlot opponentPartySlot2;
				if (readOnlyList4.Count == 0)
				{
					opponentPartySlot2 = new OpponentPartySlot(null, "", "", 0.0, readOnlyList, num3);
				}
				else
				{
					NccMatch nccMatch = readOnlyList4[0];
					string displayName = ResolveName(nccMatch.Sprite.DexId, nccMatch.Sprite.FormKey, nccMatch.Sprite.DisplayName, nameByKey, nameByDex);
					opponentPartySlot2 = new OpponentPartySlot(nccMatch.Sprite.DexId, nccMatch.Sprite.FormKey, displayName, nccMatch.Score, readOnlyList, num3);
				}
				list2.Add(FormatAttempt(num3, opponentPartySlot2, flag3, readOnlyCollection.Count));
				if (flag5)
				{
					opponentPartySlot = opponentPartySlot2;
					break;
				}
				if (opponentPartySlot2.Score > opponentPartySlot.Score)
				{
					opponentPartySlot = opponentPartySlot2;
				}
				if (opponentPartySlot2.Score >= 0.4)
				{
					opponentPartySlot = opponentPartySlot2;
					break;
				}
				if (ShouldTrustFirstTryDespiteLowNcc(num3, flag3, readOnlyList, readOnlyCollection.Count, seasonDexIds.Count))
				{
					opponentPartySlot = opponentPartySlot2;
					break;
				}
			}
			array[num2] = opponentPartySlot;
			if (opponentPartySlot.Score < 0.4)
			{
				TrySaveLowConfidenceDiagnostic(hwnd, bitmap, num2, flag2 ? new RectangleF?(rectangleF) : ((RectangleF?)null), list2);
			}
			stopwatch.Stop();
			int slotNumber = num2 + 1;
			long elapsedMilliseconds = stopwatch.ElapsedMilliseconds;
			object lastAttempt;
			if (list2.Count <= 0)
			{
				lastAttempt = "";
			}
			else
			{
				lastAttempt = list2[list2.Count - 1];
			}
			list.Add(new SlotDiagnostic(slotNumber, elapsedMilliseconds, (string)lastAttempt));
		}
		for (int num4 = 0; num4 < num; num4++)
		{
			if ((object)array[num4] == null)
			{
				array[num4] = new OpponentPartySlot(null, "", "", 0.0, Array.Empty<TypeMatch>(), 0);
			}
		}
		return new Result(array, aborted, list);
	}

	internal static bool ShouldTrustFirstTryDespiteLowNcc(int tries, bool ignoreTypeFilter, IReadOnlyList<TypeMatch> detectedTypes, int candidatesCount, int seasonDexIdsCount)
	{
		if (tries != 1)
		{
			return false;
		}
		if (ignoreTypeFilter)
		{
			return false;
		}
		if (detectedTypes.Count == 0)
		{
			return false;
		}
		if (detectedTypes[0].Score < 0.5)
		{
			return false;
		}
		if (candidatesCount <= 0)
		{
			return false;
		}
		if (candidatesCount >= seasonDexIdsCount)
		{
			return false;
		}
		return true;
	}

	internal static string ResolveName(int dexId, string formKey, string fallbackDisplayName, IReadOnlyDictionary<(int, string), string> nameByKey, IReadOnlyDictionary<int, string> nameByDex)
	{
		if (!string.IsNullOrEmpty(fallbackDisplayName))
		{
			return fallbackDisplayName;
		}
		if (nameByKey.TryGetValue((dexId, formKey), out string value) && !string.IsNullOrEmpty(value))
		{
			return value;
		}
		if (nameByDex.TryGetValue(dexId, out string value2) && !string.IsNullOrEmpty(value2))
		{
			return value2;
		}
		return "";
	}

	private static string FormatAttempt(int tries, OpponentPartySlot slot, bool ignoreTypeFilter, int candidatePoolSize)
	{
		string value = ((slot.DetectedTypes.Count == 0) ? "[]" : ("[" + string.Join(",", slot.DetectedTypes.Select((TypeMatch t) => $"{t.Type}({t.Score:0.00})")) + "]"));
		string value2 = (ignoreTypeFilter ? $"pool=season({candidatePoolSize})" : $"pool=type-filtered({candidatePoolSize})");
		string value3 = ((!slot.DexId.HasValue) ? "?" : (string.IsNullOrEmpty(slot.DisplayName) ? $"#{slot.DexId}" : slot.DisplayName));
		return $"try{tries}: ncc={value3}({slot.Score:0.000}) types={value} {value2}";
	}

	internal static IReadOnlyCollection<int> BuildCandidateDexIds(IReadOnlyCollection<int> seasonDexIds, IReadOnlyList<TypeMatch> detectedTypes, IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<PokemonType>>>? dexTypes)
	{
		if (detectedTypes.Count <= 0 || !(detectedTypes[0].Score >= 0.5) || dexTypes == null || dexTypes.Count <= 0)
		{
			return seasonDexIds;
		}
		IReadOnlyCollection<int> readOnlyCollection = FilterByExactTypeSet(seasonDexIds, detectedTypes, dexTypes);
		if (readOnlyCollection.Count != 0)
		{
			return readOnlyCollection;
		}
		return seasonDexIds;
	}

	internal static IReadOnlyList<NccMatch> FilterByExactSpriteTypeSet(IReadOnlyList<NccMatch> rawTop, IReadOnlyList<TypeMatch> detectedTypes, IReadOnlyDictionary<(int dexId, string formKey), IReadOnlyList<PokemonType>> spriteTypes)
	{
		HashSet<PokemonType> hashSet = new HashSet<PokemonType>();
		foreach (TypeMatch detectedType in detectedTypes)
		{
			hashSet.Add(detectedType.Type);
		}
		List<NccMatch> list = new List<NccMatch>(rawTop.Count);
		foreach (NccMatch item in rawTop)
		{
			if (!spriteTypes.TryGetValue((item.Sprite.DexId, item.Sprite.FormKey), out IReadOnlyList<PokemonType> value))
			{
				list.Add(item);
			}
			else
			{
				if (value.Count != hashSet.Count)
				{
					continue;
				}
				bool flag = true;
				foreach (PokemonType item2 in value)
				{
					if (!hashSet.Contains(item2))
					{
						flag = false;
						break;
					}
				}
				if (flag)
				{
					list.Add(item);
				}
			}
		}
		return list;
	}

	internal static IReadOnlyCollection<int> FilterByExactTypeSet(IReadOnlyCollection<int> seasonDexIds, IReadOnlyList<TypeMatch> detectedTypes, IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<PokemonType>>> dexTypes)
	{
		HashSet<PokemonType> hashSet = new HashSet<PokemonType>();
		foreach (TypeMatch detectedType in detectedTypes)
		{
			hashSet.Add(detectedType.Type);
		}
		List<int> list = new List<int>(seasonDexIds.Count);
		foreach (int seasonDexId in seasonDexIds)
		{
			if (dexTypes.TryGetValue(seasonDexId, out IReadOnlyList<IReadOnlyList<PokemonType>> value) && FormSetMatches(value, hashSet))
			{
				list.Add(seasonDexId);
			}
		}
		return list;
	}

	private static bool FormSetMatches(IReadOnlyList<IReadOnlyList<PokemonType>> formsTypes, HashSet<PokemonType> detected)
	{
		foreach (IReadOnlyList<PokemonType> formsType in formsTypes)
		{
			if (formsType.Count != detected.Count)
			{
				continue;
			}
			bool flag = true;
			foreach (PokemonType item in formsType)
			{
				if (!detected.Contains(item))
				{
					flag = false;
					break;
				}
			}
			if (flag)
			{
				return true;
			}
		}
		return false;
	}

	internal static RectangleF[] SplitVertically(RectangleF total, int n)
	{
		RectangleF[] array = new RectangleF[n];
		float num = total.Height / (float)n;
		for (int i = 0; i < n; i++)
		{
			array[i] = new RectangleF(total.X, total.Y + num * (float)i, total.Width, num);
		}
		return array;
	}

	private void TrySaveSlotCapture(Bitmap bmp, string fileName)
	{
		try
		{
			string filename = Path.Combine(_captureDir ?? (_captureDir = EnsureCaptureDir()), fileName);
			bmp.Save(filename, ImageFormat.Png);
		}
		catch
		{
		}
	}

	private void TrySaveLowConfidenceDiagnostic(nint hwnd, Bitmap spriteBmp, int slotIndex, RectangleF? typeRect, IReadOnlyList<string> attemptLog)
	{
		try
		{
			string text = _lowConfidenceDir ?? (_lowConfidenceDir = EnsureLowConfidenceDir());
			if (!_passCleanupDone)
			{
				if (_passStartTime.HasValue)
				{
					CleanupOldLowConfidenceCaptures(text, _passStartTime.Value);
				}
				_passCleanupDone = true;
			}
			string value = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
			string text2 = $"{value}_slot{slotIndex + 1}";
			spriteBmp.Save(Path.Combine(text, text2 + "_sprite.png"), ImageFormat.Png);
			if (typeRect.HasValue)
			{
				try
				{
					using Bitmap bitmap = _capturer.CaptureWindowRegion(hwnd, typeRect.Value);
					bitmap.Save(Path.Combine(text, text2 + "_type.png"), ImageFormat.Png);
				}
				catch
				{
				}
			}
			File.WriteAllText(Path.Combine(text, text2 + "_meta.txt"), string.Join(Environment.NewLine, attemptLog));
		}
		catch
		{
		}
	}

	private static string EnsureCaptureDir()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCH", "opponent-party-captures");
		Directory.CreateDirectory(text);
		return text;
	}

	private static string EnsureLowConfidenceDir()
	{
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCH", "low-confidence-captures");
		Directory.CreateDirectory(text);
		return text;
	}

	internal static void CleanupOldLowConfidenceCaptures(string dir, DateTime passStart)
	{
		try
		{
			foreach (string item in Directory.EnumerateFiles(dir))
			{
				string fileName = Path.GetFileName(item);
				if (fileName.Length >= 19 && DateTime.TryParseExact(fileName.Substring(0, 19), "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var result) && result < passStart)
				{
					try
					{
						File.Delete(item);
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}
}
