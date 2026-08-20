using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace PokemonHelper.Services.Recognition;

public sealed class LogOcr
{
	public static readonly RectangleF DefaultLogRegion = new RectangleF(0.04f, 0.66f, 0.48f, 0.2f);

	private readonly IScreenCapturer _capturer;

	private readonly IOcrEngine _ocr;

	private static readonly char[] TimerSeparators = new char[2] { ':', '.' };

	private const int MegaAnchorJamoDistance = 2;

	public RectangleF LogRegion { get; set; }

	public bool UseThresholdPreprocess { get; set; } = true;

	public bool UseHsvPreprocess { get; set; } = true; // Use HSV by default based on the new implementation plan

	// 사용자 제공 RGB 기준에서 범위를 아주 살짝 더 널널하게 조정함 (명도 160 이상, 채도 90 이하)
	public OpenCvSharp.Scalar HsvLowerBound { get; set; } = new OpenCvSharp.Scalar(0, 0, 160); 

	public OpenCvSharp.Scalar HsvUpperBound { get; set; } = new OpenCvSharp.Scalar(180, 90, 255);

	public double ThresholdValue { get; set; } = 180.0;

	public LogOcr(IScreenCapturer capturer, IOcrEngine ocr, RectangleF? region = null)
	{
		_capturer = capturer;
		_ocr = ocr;
		LogRegion = region ?? DefaultLogRegion;
	}

	public string RecognizeLogRaw(nint hwnd)
	{
		using Bitmap bmp = _capturer.CaptureWindowRegion(hwnd, LogRegion);
		return RecognizeLogRaw(bmp);
	}

	public string RecognizeLogRaw(Bitmap bmp)
	{
		if (UseHsvPreprocess)
		{
			using (Bitmap bmp2 = ImagePreprocessor.BinarizeByHsv(bmp, HsvLowerBound, HsvUpperBound, 2))
			{

				return _ocr.Recognize(bmp2) ?? string.Empty;
			}
		}
		else if (UseThresholdPreprocess)
		{
			using (Bitmap bmp2 = ImagePreprocessor.BinarizeForLog(bmp, 2, ThresholdValue))
			{

				return _ocr.Recognize(bmp2) ?? string.Empty;
			}
		}
		return _ocr.Recognize(bmp) ?? string.Empty;
	}

	public Bitmap CaptureLogRegion(nint hwnd)
	{
		return _capturer.CaptureWindowRegion(hwnd, LogRegion);
	}

	public static ulong ComputeFingerprint(Bitmap bmp)
	{
		if (bmp.Width == 0 || bmp.Height == 0)
		{
			return 0uL;
		}
		Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
		BitmapData bitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			byte[] array = new byte[bitmapData.Stride * bmp.Height];
			Marshal.Copy(bitmapData.Scan0, array, 0, array.Length);
			ulong num = 14695981039346656037uL;
			for (int i = 0; i < bmp.Height; i++)
			{
				int num2 = i * bitmapData.Stride;
				ushort num3 = 0;
				ushort num4 = 0;
				for (int j = 0; j < bmp.Width; j++)
				{
					int num5 = num2 + j * 4;
					byte b = array[num5];
					byte b2 = array[num5 + 1];
					byte b3 = array[num5 + 2];
					if (b3 > 180 && b2 > 180 && b > 180)
					{
						num3++;
					}
					num4 += (ushort)(b3 / 16);
				}
				num = (num ^ num3) * 1099511628211L;
				num = (num ^ num4) * 1099511628211L;
			}
			return num;
		}
		finally
		{
			bmp.UnlockBits(bitmapData);
		}
	}

	public static bool IsTimerOnlyRaw(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return false;
		}
        
        bool allDigits = true;
        foreach (char c in raw)
        {
            if (!char.IsAsciiDigit(c))
            {
                allDigits = false;
                break;
            }
        }
        if (allDigits && raw.Length >= 3 && raw.Length <= 5)
        {
            return true;
        }

		int num = raw.IndexOfAny(TimerSeparators);
		if (num < 1 || num > 2 || raw.Length - num - 1 != 2)
		{
			return false;
		}
		for (int i = 0; i < raw.Length; i++)
		{
			if (i != num && !char.IsAsciiDigit(raw[i]))
			{
				return false;
			}
		}
		return true;
	}

	public static int CountBrightPixels(Bitmap bmp, int threshold = 180)
	{
		if (bmp.Width == 0 || bmp.Height == 0)
		{
			return 0;
		}
		Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
		BitmapData bitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			byte[] array = new byte[bitmapData.Stride * bmp.Height];
			Marshal.Copy(bitmapData.Scan0, array, 0, array.Length);
			int num = 0;
			for (int i = 0; i < bmp.Height; i++)
			{
				int num2 = i * bitmapData.Stride;
				for (int j = 0; j < bmp.Width; j++)
				{
					int num3 = num2 + j * 4;
					if (array[num3] > threshold && array[num3 + 1] > threshold && array[num3 + 2] > threshold)
					{
						num++;
					}
				}
			}
			return num;
		}
		finally
		{
			bmp.UnlockBits(bitmapData);
		}
	}

	public static LogEvent? Classify(string normalized)
	{
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return null;
		}
		if (RankUpParser.FuzzyIndexOf(normalized, "메가진화", 0, 2) < 0)
		{
			return null;
		}
		return new LogEvent(LogEventKind.MegaEvolution, "mega");
	}
}
