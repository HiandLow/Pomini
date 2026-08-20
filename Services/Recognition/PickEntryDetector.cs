using System;
using System.Drawing;

namespace PokemonHelper.Services.Recognition;

public sealed class PickEntryDetector
{
	public static readonly RectangleF DefaultRegion = new RectangleF(0.38f, 0.03f, 0.3f, 0.05f);

	public static readonly RectangleF DefaultStatsRegion = new RectangleF(0.8f, 0.9f, 0.17f, 0.05f);

	private readonly IScreenCapturer _capturer;

	private readonly IOcrEngine _ocr;

	private readonly IOcrEngine _statsOcr;

	public RectangleF Region { get; set; }

	public RectangleF StatsRegion { get; set; }

	public double ThresholdValue { get; set; } = 180.0;

	public PickEntryDetector(IScreenCapturer capturer, IOcrEngine ocr, IOcrEngine? statsOcr = null, RectangleF? region = null, RectangleF? statsRegion = null)
	{
		_capturer = capturer;
		_ocr = ocr;
		_statsOcr = statsOcr ?? ocr;
		Region = region ?? DefaultRegion;
		StatsRegion = statsRegion ?? DefaultStatsRegion;
	}

	public bool IsPickEntryShown(nint hwnd)
	{
		return Analyze(hwnd).Shown;
	}

	public (bool Shown, string Raw) Analyze(nint hwnd)
	{
		string text = OcrRegion(hwnd, Region);
		string text2 = OcrStatsRegion(hwnd, StatsRegion);
		bool item = IsShown(Normalize(text), Normalize(text2));
		string item2 = $"랭크/싱글:'{text}' 능력치:'{text2}'";
		return (Shown: item, Raw: item2);
	}

	private string OcrRegion(nint hwnd, RectangleF region)
	{
		using Bitmap src = _capturer.CaptureWindowRegion(hwnd, region);
		using Bitmap bmp = ImagePreprocessor.BinarizeForLog(src, 2, ThresholdValue);

		return _ocr.Recognize(bmp, OcrLayoutHint.SingleLine) ?? string.Empty;
	}

	private string OcrStatsRegion(nint hwnd, RectangleF region)
	{
		using Bitmap bmp = _capturer.CaptureWindowRegion(hwnd, region);

		return _statsOcr.Recognize(bmp, OcrLayoutHint.SingleLine) ?? string.Empty;
	}

	internal static string Normalize(string raw)
	{
		return raw.Replace(" ", "").Replace("\t", "").Replace("\n", "")
			.Replace("\r", "");
	}

	internal static bool IsShown(string rankNormalized, string statsNormalized)
	{
		bool num = MatchesKeyword(rankNormalized, "랭크배틀") || MatchesKeyword(rankNormalized, "싱글배틀") || MatchesKeyword(rankNormalized, "프라이빗배틀") || MatchesKeyword(rankNormalized, "캐주얼배틀");
		bool flag = MatchesKeyword(statsNormalized, "능력치표시");
		return num && flag;
	}

	internal static bool MatchesKeyword(string normalized, string keyword)
	{
		if (string.IsNullOrEmpty(keyword) || keyword.Length < 2)
		{
			return normalized.Contains(keyword);
		}
		if (normalized.Contains(keyword))
		{
			return true;
		}
		if (keyword.Length < 4)
		{
			return false;
		}
		for (int i = 0; i < keyword.Length; i++)
		{
			string value = keyword.Remove(i, 1);
			if (normalized.Contains(value, StringComparison.Ordinal))
			{
				return true;
			}
		}
		return false;
	}
}
