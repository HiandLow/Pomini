using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace PokemonHelper.Services.Recognition;

public sealed class ActiveBattleDetector : IDisposable
{
	public sealed record PixelStats(double DarkRatio, double BrightRatio, double AvgBrightness, int TotalPixels, double MatchScore = 0.0);

	public static readonly RectangleF DefaultRegion = new RectangleF(0.01f, 0.01f, 0.07f, 0.08f);

	public const int DarkThreshold = 180;

	public const int BrightThreshold = 540;

	public const double DarkRatioMin = 0.2;

	public const double BrightRatioMin = 0.02;

	private readonly IScreenCapturer _capturer;

	private Mat? _templateMat;

	public RectangleF Region { get; set; }

	public double MatchThreshold { get; set; } = 0.6;

	public bool HasTemplate => _templateMat != null;

	public ActiveBattleDetector(IScreenCapturer capturer, RectangleF? region = null)
	{
		_capturer = capturer;
		Region = region ?? DefaultRegion;
	}

	public void SetTemplate(Bitmap? template)
	{
		_templateMat?.Dispose();
		_templateMat = template?.ToMat();
	}

	public (bool Active, PixelStats Stats) Analyze(nint hwnd)
	{
		using Bitmap bitmap = _capturer.CaptureWindowRegion(hwnd, Region);
		PixelStats pixelStats = ComputePixelStats(bitmap);
		if (_templateMat != null)
		{
			double num = ComputeMatchScore(bitmap, _templateMat);
			pixelStats = pixelStats with
			{
				MatchScore = num
			};
			return (Active: num >= MatchThreshold, Stats: pixelStats);
		}
		return (Active: IsActiveFromStats(pixelStats), Stats: pixelStats);
	}

	public static double ComputeMatchScore(Bitmap captured, Mat template)
	{
		if (captured.Width <= 0 || captured.Height <= 0)
		{
			return 0.0;
		}
		using Mat mat = captured.ToMat();
		if (mat.Cols < template.Cols || mat.Rows < template.Rows)
		{
			return 0.0;
		}
		using Mat mat2 = new Mat();
		Cv2.MatchTemplate(mat, template, mat2, TemplateMatchModes.CCoeffNormed);
		Cv2.MinMaxLoc((InputArray)mat2, out double _, out double maxVal);
		return double.IsNaN(maxVal) ? 0.0 : maxVal;
	}

	public void Dispose()
	{
		_templateMat?.Dispose();
	}

	public static bool IsActiveFromStats(PixelStats stats)
	{
		if (stats.DarkRatio >= 0.2)
		{
			return stats.BrightRatio >= 0.02;
		}
		return false;
	}

	public static bool IsActiveFromPixels(Bitmap bmp)
	{
		PixelStats pixelStats = ComputePixelStats(bmp);
		if (pixelStats.DarkRatio >= 0.2)
		{
			return pixelStats.BrightRatio >= 0.02;
		}
		return false;
	}

	public static PixelStats ComputePixelStats(Bitmap bmp)
	{
		if (bmp.Width == 0 || bmp.Height == 0)
		{
			return new PixelStats(0.0, 0.0, 0.0, 0);
		}
		Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
		BitmapData bitmapData = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			byte[] array = new byte[bitmapData.Stride * bmp.Height];
			Marshal.Copy(bitmapData.Scan0, array, 0, array.Length);
			int num = 0;
			int num2 = 0;
			long num3 = 0L;
			int num4 = bmp.Width * bmp.Height;
			for (int i = 0; i < bmp.Height; i++)
			{
				int num5 = i * bitmapData.Stride;
				for (int j = 0; j < bmp.Width; j++)
				{
					int num6 = num5 + j * 4;
					int num7 = array[num6];
					int num8 = array[num6 + 1];
					int num9 = array[num6 + 2] + num8 + num7;
					num3 += num9;
					if (num9 <= 180)
					{
						num++;
					}
					if (num9 >= 540)
					{
						num2++;
					}
				}
			}
			return new PixelStats((double)num / (double)num4, (double)num2 / (double)num4, (double)num3 / (double)(num4 * 3), num4);
		}
		finally
		{
			bmp.UnlockBits(bitmapData);
		}
	}
}
