using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using OpenCvSharp;
using OpenCvSharp.Extensions;


namespace PokemonHelper.Services.Recognition;

[SupportedOSPlatform("windows")]
public sealed class NccMatcher : IDisposable
{
	internal sealed record SpriteEntry(Mat Gray, Mat Mask, Mat GrayTight, Mat MaskTight, int MaskPixelCount, int MaskTightPixelCount, float[] ColorHist, Mat GrayStat, Mat MaskStat, int MaskStatPixelCount, float[] ColorHistStat) : IDisposable
	{
		public void Dispose()
		{
			Gray.Dispose();
			Mask.Dispose();
			if (GrayTight != Gray)
			{
				GrayTight.Dispose();
			}
			if (MaskTight != Mask)
			{
				MaskTight.Dispose();
			}
			if (GrayStat != Gray)
			{
				GrayStat.Dispose();
			}
			if (MaskStat != Mask)
			{
				MaskStat.Dispose();
			}
		}
	}

	internal sealed record CapturePair(Mat Gray, Mat Mask, int MaskPixelCount, float[] ColorHist) : IDisposable
	{
		public void Dispose()
		{
			Gray.Dispose();
			Mask.Dispose();
		}
	}

	public const int ResizeSize = 64;

	public const double CropRatio = 0.55;

	public const double DefaultMinScore = 0.1;

	private const int MinMaskPixels = 64;

	private const double TightCropBboxRatio = 0.85;

	private const int BgHueLow1 = 0;

	private const int BgHueHigh1 = 10;

	private const int BgHueLow2 = 155;

	private const int BgHueHigh2 = 180;

	private const int BgSatMin = 120;

	private const int BgValMin = 30;

	private static readonly double[] CaptureCropSweep = new double[4] { 0.4, 0.55, 0.7, 0.85 };

	private const double SizeMatchBonusWeight = 0.05;

	private const double StatTightSigmaK = 1.5;

	private static readonly int[] TemplateMatchScales = new int[7] { 48, 64, 80, 96, 112, 128, 144 };

	private const double ColorHistogramBonusWeight = 0.25;

	private const int ColorHistHueBins = 12;

	private const int ColorHistSatBins = 4;

	private const int ColorHistMinSat = 20;

	private readonly ISpritesProvider _provider;

	private readonly object _loadLock = new object();

	private Dictionary<(int dexId, string formKey), SpriteEntry>? _cache;

	private bool _disposed;

	public IReadOnlyList<SpriteRecord> AllSprites => _provider.AllSprites;

	public int CacheCount
	{
		get
		{
			EnsureLoaded();
			return _cache?.Count ?? 0;
		}
	}

	public NccMatcher(ISpritesProvider provider)
	{
		_provider = provider;
	}

	public NccMatch? Match(Bitmap captured, double minScore = 0.1, IReadOnlyCollection<int>? candidateDexIds = null, bool excludeMegaForms = false)
	{
		if (captured.Width <= 0 || captured.Height <= 0)
		{
			return null;
		}
		EnsureLoaded();
		if (_cache == null || _cache.Count == 0)
		{
			return null;
		}
		CapturePair[] array = NormalizeCaptureSweep(captured);
		CapturePair capturePair = NormalizeCaptureStatTight(captured);
		Mat mat = ConvertToRawGray(captured);
		try
		{
			IEnumerable<SpriteRecord> enumerable;
			if (candidateDexIds == null || candidateDexIds.Count <= 0)
			{
				IEnumerable<SpriteRecord> allSprites = _provider.AllSprites;
				enumerable = allSprites;
			}
			else
			{
				enumerable = candidateDexIds.SelectMany((int d) => _provider.FindByDex(d));
			}
			SpriteRecord spriteRecord = null;
			double num = double.MinValue;
			foreach (SpriteRecord item in enumerable)
			{
				if ((!excludeMegaForms || !IsMegaForm(item.FormKey)) && _cache.TryGetValue((item.DexId, item.FormKey), out SpriteEntry value))
				{
					double num2 = ScoreCaptureVsEntry(array, capturePair, mat, value);
					if (num2 > num)
					{
						num = num2;
						spriteRecord = item;
					}
				}
			}
			if ((object)spriteRecord == null || num < minScore)
			{
				return null;
			}
			return new NccMatch(spriteRecord, num);
		}
		finally
		{
			CapturePair[] array2 = array;
			for (int num3 = 0; num3 < array2.Length; num3++)
			{
				array2[num3].Dispose();
			}
			capturePair.Dispose();
			mat.Dispose();
		}
	}

	public (double defaultScore, double tightScore, double statScore, double templateScore, double maxScore) ScoreBreakdown(Bitmap captured, int dexId, string formKey)
	{
		if (captured.Width <= 0 || captured.Height <= 0)
		{
			return (defaultScore: 0.0, tightScore: 0.0, statScore: 0.0, templateScore: 0.0, maxScore: 0.0);
		}
		EnsureLoaded();
		if (_cache == null || !_cache.TryGetValue((dexId, formKey), out SpriteEntry value))
		{
			return (defaultScore: 0.0, tightScore: 0.0, statScore: 0.0, templateScore: 0.0, maxScore: 0.0);
		}
		CapturePair[] array = NormalizeCaptureSweep(captured);
		CapturePair capturePair = NormalizeCaptureStatTight(captured);
		Mat mat = ConvertToRawGray(captured);
		try
		{
			double num = double.MinValue;
			double num2 = double.MinValue;
			bool flag = value.GrayTight == value.Gray;
			bool flag2 = value.GrayStat == value.Gray;
			CapturePair[] array2 = array;
			foreach (CapturePair capturePair2 in array2)
			{
				double num3 = MaskedCcoeffNormed(capturePair2.Gray, value.Gray, value.Mask, capturePair2.Mask);
				double num4 = ComputeSizeMatch(capturePair2.MaskPixelCount, value.MaskPixelCount);
				double num5 = CompareColorHistograms(capturePair2.ColorHist, value.ColorHist);
				double num6 = num3 + num4 * 0.05 + num5 * 0.25;
				if (num6 > num)
				{
					num = num6;
				}
				if (!flag)
				{
					double num7 = MaskedCcoeffNormed(capturePair2.Gray, value.GrayTight, value.MaskTight, capturePair2.Mask);
					double num8 = ComputeSizeMatch(capturePair2.MaskPixelCount, value.MaskTightPixelCount);
					double num9 = num7 + num8 * 0.05 + num5 * 0.25;
					if (num9 > num2)
					{
						num2 = num9;
					}
				}
			}
			double num10 = double.MinValue;
			if (!flag2)
			{
				double num11 = MaskedCcoeffNormed(capturePair.Gray, value.GrayStat, value.MaskStat, capturePair.Mask);
				double num12 = ComputeSizeMatch(capturePair.MaskPixelCount, value.MaskStatPixelCount);
				double num13 = CompareColorHistograms(capturePair.ColorHist, value.ColorHistStat);
				num10 = num11 + num12 * 0.05 + num13 * 0.25;
			}
			double num14 = MultiScaleMatchTemplate(mat, value.Gray, value.Mask);
			double num15 = ComputeSizeMatch(array[1].MaskPixelCount, value.MaskPixelCount);
			double num16 = CompareColorHistograms(array[1].ColorHist, value.ColorHist);
			double num17 = num14 + num15 * 0.05 + num16 * 0.25;
			double item = Math.Max(Math.Max(Math.Max(num, num2), num10), num17);
			return (defaultScore: num, tightScore: num2, statScore: num10, templateScore: num17, maxScore: item);
		}
		finally
		{
			CapturePair[] array2 = array;
			for (int i = 0; i < array2.Length; i++)
			{
				array2[i].Dispose();
			}
			capturePair.Dispose();
			mat.Dispose();
		}
	}

	public IReadOnlyList<NccMatch> TopN(Bitmap captured, int topN = 5, IReadOnlyCollection<int>? candidateDexIds = null, bool excludeMegaForms = false)
	{
		if (topN <= 0 || captured.Width <= 0 || captured.Height <= 0)
		{
			return Array.Empty<NccMatch>();
		}
		EnsureLoaded();
		if (_cache == null || _cache.Count == 0)
		{
			return Array.Empty<NccMatch>();
		}
		CapturePair[] array = NormalizeCaptureSweep(captured);
		CapturePair capturePair = NormalizeCaptureStatTight(captured);
		Mat mat = ConvertToRawGray(captured);
		try
		{
			IEnumerable<SpriteRecord> enumerable;
			if (candidateDexIds == null || candidateDexIds.Count <= 0)
			{
				IEnumerable<SpriteRecord> allSprites = _provider.AllSprites;
				enumerable = allSprites;
			}
			else
			{
				enumerable = candidateDexIds.SelectMany((int d) => _provider.FindByDex(d));
			}
			List<NccMatch> list = new List<NccMatch>();
			foreach (SpriteRecord item in enumerable)
			{
				if ((!excludeMegaForms || !IsMegaForm(item.FormKey)) && _cache.TryGetValue((item.DexId, item.FormKey), out SpriteEntry value))
				{
					double score = ScoreCaptureVsEntry(array, capturePair, mat, value);
					list.Add(new NccMatch(item, score));
				}
			}
			return list.OrderByDescending((NccMatch m) => m.Score).Take(topN).ToList();
		}
		finally
		{
			CapturePair[] array2 = array;
			for (int num = 0; num < array2.Length; num++)
			{
				array2[num].Dispose();
			}
			capturePair.Dispose();
			mat.Dispose();
		}
	}

	internal static bool IsMegaForm(string formKey)
	{
		if (!string.IsNullOrEmpty(formKey))
		{
			return formKey.StartsWith("mega", StringComparison.Ordinal);
		}
		return false;
	}

	private static CapturePair[] NormalizeCaptureSweep(Bitmap bmp)
	{
		CapturePair[] array = new CapturePair[CaptureCropSweep.Length];
		for (int i = 0; i < CaptureCropSweep.Length; i++)
		{
			array[i] = NormalizeCaptureToGrayWithMask(bmp, CaptureCropSweep[i]);
		}
		return array;
	}

	private static double ScoreCaptureVsEntry(CapturePair[] caps, CapturePair capStat, Mat capRawGray, SpriteEntry entry)
	{
		double num = double.MinValue;
		bool flag = entry.GrayTight == entry.Gray;
		bool flag2 = entry.GrayStat == entry.Gray;
		foreach (CapturePair capturePair in caps)
		{
			double num2 = MaskedCcoeffNormed(capturePair.Gray, entry.Gray, entry.Mask, capturePair.Mask);
			double num3 = ComputeSizeMatch(capturePair.MaskPixelCount, entry.MaskPixelCount);
			double num4 = CompareColorHistograms(capturePair.ColorHist, entry.ColorHist);
			double num5 = num2 + num3 * 0.05 + num4 * 0.25;
			if (num5 > num)
			{
				num = num5;
			}
			if (!flag)
			{
				double num6 = MaskedCcoeffNormed(capturePair.Gray, entry.GrayTight, entry.MaskTight, capturePair.Mask);
				double num7 = ComputeSizeMatch(capturePair.MaskPixelCount, entry.MaskTightPixelCount);
				double num8 = num6 + num7 * 0.05 + num4 * 0.25;
				if (num8 > num)
				{
					num = num8;
				}
			}
		}
		if (!flag2)
		{
			double num9 = MaskedCcoeffNormed(capStat.Gray, entry.GrayStat, entry.MaskStat, capStat.Mask);
			double num10 = ComputeSizeMatch(capStat.MaskPixelCount, entry.MaskStatPixelCount);
			double num11 = CompareColorHistograms(capStat.ColorHist, entry.ColorHistStat);
			double num12 = num9 + num10 * 0.05 + num11 * 0.25;
			if (num12 > num)
			{
				num = num12;
			}
		}
		double num13 = MultiScaleMatchTemplate(capRawGray, entry.Gray, entry.Mask);
		double num14 = ComputeSizeMatch(caps[1].MaskPixelCount, entry.MaskPixelCount);
		double num15 = CompareColorHistograms(caps[1].ColorHist, entry.ColorHist);
		double num16 = num13 + num14 * 0.05 + num15 * 0.25;
		if (num16 > num)
		{
			num = num16;
		}
		return num;
	}

	internal static float[] ComputeColorHistogram(Mat bgr, Mat mask)
	{
		float[] array = new float[48];
		if (bgr.Empty()) return array;
        
		using Mat mat = new Mat();
		Cv2.CvtColor(bgr, mat, ColorConversionCodes.BGR2HSV);
        
        if (!mat.IsContinuous() || !mask.IsContinuous()) return array;

		int total = mat.Rows * mat.Cols;
		byte[] hsvData = new byte[total * 3];
		byte[] maskData = new byte[total];

		System.Runtime.InteropServices.Marshal.Copy(mat.Data, hsvData, 0, total * 3);
		System.Runtime.InteropServices.Marshal.Copy(mask.Data, maskData, 0, total);

		long num = 0L;
		
		for (int i = 0; i < total; i++)
		{
			if (maskData[i] != 0)
			{
				byte h = hsvData[i * 3 + 0];
				byte s = hsvData[i * 3 + 1];
				// byte v = hsvData[i * 3 + 2];
                
				if (s >= 20)
				{
					int num2 = Math.Min(11, h * 12 / 180);
					int num3 = Math.Min(3, s * 4 / 256);
					array[num2 * 4 + num3] += 1f;
					num++;
				}
			}
		}
		
		if (num > 0)
		{
			float num4 = 1f / (float)num;
			for (int k = 0; k < array.Length; k++)
			{
				array[k] *= num4;
			}
		}
		return array;
	}

	internal static double CompareColorHistograms(float[] a, float[] b)
	{
		if (a.Length != b.Length || a.Length == 0)
		{
			return 0.0;
		}
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		for (int i = 0; i < a.Length; i++)
		{
			num += (double)Math.Min(a[i], b[i]);
			num2 += (double)a[i];
			num3 += (double)b[i];
		}
		if (num2 < 1E-06 || num3 < 1E-06)
		{
			return 0.0;
		}
		return num;
	}

	internal static double ComputeSizeMatch(int capArea, int refArea)
	{
		int num = Math.Max(capArea, refArea);
		if (num == 0)
		{
			return 0.0;
		}
		int num2 = Math.Abs(capArea - refArea);
		return 1.0 - (double)num2 / (double)num;
	}

	internal static int CountMaskPixels(Mat mask)
	{
		if (mask.Empty())
		{
			return 0;
		}
		return Cv2.CountNonZero(mask);
	}

	internal static double MultiScaleMatchTemplate(Mat capRawGray, Mat refGray, Mat refMask)
	{
		if (capRawGray.Empty() || refGray.Empty())
		{
			return 0.0;
		}
		double num = double.MinValue;
		int cols = capRawGray.Cols;
		int rows = capRawGray.Rows;
		int cols2 = refGray.Cols;
		int[] templateMatchScales = TemplateMatchScales;
		foreach (int num2 in templateMatchScales)
		{
			if (num2 > cols || num2 > rows || num2 < 8)
			{
				continue;
			}
			Mat mat = null;
			Mat mat2 = null;
			try
			{
				double minVal;
				OpenCvSharp.Point maxLoc;
				OpenCvSharp.Point minLoc;
				if (num2 == cols2)
				{
					using (Mat mat3 = new Mat())
					{
						Cv2.MatchTemplate(capRawGray, refGray, mat3, TemplateMatchModes.CCoeffNormed, refMask);
						Cv2.MinMaxLoc(mat3, out minVal, out var maxVal, out maxLoc, out minLoc);
						if (!double.IsInfinity(maxVal) && !double.IsNaN(maxVal) && maxVal > num)
						{
							num = maxVal;
						}
					}
					continue;
				}
				mat = new Mat();
				mat2 = new Mat();
				Cv2.Resize(refGray, mat, new OpenCvSharp.Size(num2, num2), 0.0, 0.0, InterpolationFlags.Area);
				Cv2.Resize(refMask, mat2, new OpenCvSharp.Size(num2, num2), 0.0, 0.0, InterpolationFlags.Area);
				using Mat mat4 = new Mat();
				Cv2.MatchTemplate(capRawGray, mat, mat4, TemplateMatchModes.CCoeffNormed, mat2);
				Cv2.MinMaxLoc(mat4, out minVal, out var maxVal2, out minLoc, out maxLoc);
				if (!double.IsInfinity(maxVal2) && !double.IsNaN(maxVal2) && maxVal2 > num)
				{
					num = maxVal2;
				}
			}
			catch
			{
			}
			finally
			{
				mat?.Dispose();
				mat2?.Dispose();
			}
		}
		if (num != double.MinValue)
		{
			return num;
		}
		return 0.0;
	}

	internal static Mat ConvertToRawGray(Bitmap bmp)
	{
		using Mat mat = bmp.ToMat();
		return mat.Channels() switch
		{
			1 => mat.Clone(), 
			4 => mat.CvtColor(ColorConversionCodes.BGRA2GRAY), 
			_ => mat.CvtColor(ColorConversionCodes.BGR2GRAY), 
		};
	}

	internal static Rect? ComputeStatTightRect(Mat mask)
	{
		if (mask.Empty() || !mask.IsContinuous())
		{
			return null;
		}
		
		int total = mask.Rows * mask.Cols;
		byte[] maskData = new byte[total];
		System.Runtime.InteropServices.Marshal.Copy(mask.Data, maskData, 0, total);

		int rows = mask.Rows;
		int cols = mask.Cols;
		long num = 0L;
		long num2 = 0L;
		long num3 = 0L;
		
		for (int i = 0; i < rows; i++)
		{
			int rowOffset = i * cols;
			for (int j = 0; j < cols; j++)
			{
				if (maskData[rowOffset + j] != 0)
				{
					num += j;
					num2 += i;
					num3++;
				}
			}
		}
		
		if (num3 < 10)
		{
			return null;
		}
		int num4 = (int)(num / num3);
		int num5 = (int)(num2 / num3);
		long num6 = 0L;
		long num7 = 0L;
		for (int k = 0; k < rows; k++)
		{
			int rowOffset = k * cols;
			for (int l = 0; l < cols; l++)
			{
				if (maskData[rowOffset + l] != 0)
				{
					long num8 = l - num4;
					long num9 = k - num5;
					num6 += num8 * num8;
					num7 += num9 * num9;
				}
			}
		}
		int num12 = (int)Math.Round((double)num4);
		int num13 = (int)Math.Round((double)num5);
		double val = Math.Sqrt(num6 / (double)num3);
		double val2 = Math.Sqrt(num7 / (double)num3);
		int num10 = Math.Max(8, (int)Math.Round(Math.Max(val, val2) * 1.5));
		int num11 = Math.Min(Math.Min(cols, rows), num10 * 2);
		if (num11 < 8)
		{
			return null;
		}
		int num14 = Math.Max(0, num12 - num11 / 2);
		int num15 = Math.Max(0, num13 - num11 / 2);
		if (num14 + num11 > cols)
		{
			num14 = cols - num11;
		}
		if (num15 + num11 > rows)
		{
			num15 = rows - num11;
		}
		if (num14 < 0 || num15 < 0)
		{
			return null;
		}
		return new Rect(num14, num15, num11, num11);
	}

	private void EnsureLoaded()
	{
		if (_cache != null)
		{
			return;
		}
		lock (_loadLock)
		{
			if (_cache != null)
			{
				return;
			}
			string spriteDirectory = _provider.SpriteDirectory;
			Dictionary<(int, string), SpriteEntry> dictionary = new Dictionary<(int, string), SpriteEntry>(_provider.AllSprites.Count);
			if (spriteDirectory == null)
			{
				_cache = dictionary;
				return;
			}
			foreach (SpriteRecord allSprite in _provider.AllSprites)
			{
				if (string.IsNullOrEmpty(allSprite.IconFile))
				{
					continue;
				}
				string text = Path.Combine(spriteDirectory, allSprite.IconFile);
				if (!File.Exists(text))
				{
					continue;
				}
				try
				{
					using Bitmap bmp = new Bitmap(text);
					SpriteEntry value = NormalizeSpriteToEntry(bmp);
					dictionary[(allSprite.DexId, allSprite.FormKey)] = value;
				}
				catch
				{
				}
			}
			_cache = dictionary;
		}
	}

	internal static Mat NormalizeCaptureToGray(Bitmap bmp)
	{
		using Mat mat = bmp.ToMat();
		using Mat src = mat.Channels() switch
		{
			1 => mat.CvtColor(ColorConversionCodes.GRAY2BGR), 
			4 => mat.CvtColor(ColorConversionCodes.BGRA2BGR), 
			_ => mat.Clone(), 
		};
		using Mat mat2 = CenterCrop(src, 0.55);
		using Mat mat3 = new Mat();
		Cv2.Resize(mat2, mat3, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Mat mat4 = new Mat();
		Cv2.CvtColor(mat3, mat4, ColorConversionCodes.BGR2GRAY);
		return mat4;
	}

	internal static CapturePair NormalizeCaptureToGrayWithMask(Bitmap bmp, double cropRatio = 0.55)
	{
		using Mat mat = bmp.ToMat();
		using Mat src = mat.Channels() switch
		{
			1 => mat.CvtColor(ColorConversionCodes.GRAY2BGR), 
			4 => mat.CvtColor(ColorConversionCodes.BGRA2BGR), 
			_ => mat.Clone(), 
		};
		using Mat mat2 = CenterCrop(src, cropRatio);
		using Mat mat3 = new Mat();
		Cv2.Resize(mat2, mat3, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Mat mat4 = new Mat();
		Cv2.CvtColor(mat3, mat4, ColorConversionCodes.BGR2GRAY);
		using Mat mat5 = new Mat();
		Cv2.CvtColor(mat3, mat5, ColorConversionCodes.BGR2HSV);
		using Mat mat6 = new Mat();
		Cv2.InRange(mat5, new Scalar(0.0, 120.0, 30.0), new Scalar(10.0, 255.0, 255.0), mat6);
		using Mat mat7 = new Mat();
		Cv2.InRange(mat5, new Scalar(155.0, 120.0, 30.0), new Scalar(180.0, 255.0, 255.0), mat7);
		using Mat mat8 = new Mat();
		Cv2.BitwiseOr(mat6, mat7, mat8);
		Mat mat9 = new Mat();
		Cv2.BitwiseNot(mat8, mat9);
		int maskPixelCount = CountMaskPixels(mat9);
		float[] colorHist = ComputeColorHistogram(mat3, mat9);
		return new CapturePair(mat4, mat9, maskPixelCount, colorHist);
	}

	internal static CapturePair NormalizeCaptureStatTight(Bitmap bmp)
	{
		using Mat mat = bmp.ToMat();
		using Mat src = mat.Channels() switch
		{
			1 => mat.CvtColor(ColorConversionCodes.GRAY2BGR), 
			4 => mat.CvtColor(ColorConversionCodes.BGRA2BGR), 
			_ => mat.Clone(), 
		};
		using Mat mat2 = CenterCrop(src, 0.55);
		using Mat mat3 = new Mat();
		Cv2.CvtColor(mat2, mat3, ColorConversionCodes.BGR2HSV);
		using Mat mat4 = new Mat();
		Cv2.InRange(mat3, new Scalar(0.0, 120.0, 30.0), new Scalar(10.0, 255.0, 255.0), mat4);
		using Mat mat5 = new Mat();
		Cv2.InRange(mat3, new Scalar(155.0, 120.0, 30.0), new Scalar(180.0, 255.0, 255.0), mat5);
		using Mat mat6 = new Mat();
		Cv2.BitwiseOr(mat4, mat5, mat6);
		using Mat mat7 = new Mat();
		Cv2.BitwiseNot(mat6, mat7);
		Rect roi = ComputeStatTightRect(mat7) ?? new Rect(0, 0, mat7.Cols, mat7.Rows);
		using Mat mat8 = new Mat(mat2, roi);
		using Mat mat9 = new Mat(mat7, roi);
		using Mat mat10 = new Mat();
		using Mat mat11 = new Mat();
		Cv2.Resize(mat8, mat10, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Cv2.Resize(mat9, mat11, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Mat mat12 = new Mat();
		Cv2.CvtColor(mat10, mat12, ColorConversionCodes.BGR2GRAY);
		Mat mask = mat11.Clone();
		int maskPixelCount = CountMaskPixels(mask);
		float[] colorHist = ComputeColorHistogram(mat10, mask);
		return new CapturePair(mat12, mask, maskPixelCount, colorHist);
	}

	internal static SpriteEntry NormalizeSpriteToEntry(Bitmap bmp)
	{
		using Mat mat = bmp.ToMat();
		Mat mat2;
		Mat mat4;
		if (mat.Channels() == 4)
		{
			Mat[] array = Cv2.Split(mat);
			try
			{
				mat2 = array[3].Clone();
				using Mat mat3 = new Mat();
				Cv2.Merge(new Mat[3]
				{
					array[0],
					array[1],
					array[2]
				}, mat3);
				mat4 = mat3.Clone();
			}
			finally
			{
				Mat[] array2 = array;
				for (int i = 0; i < array2.Length; i++)
				{
					array2[i].Dispose();
				}
			}
		}
		else
		{
			Mat mat5 = ((mat.Channels() != 1) ? mat.Clone() : mat.CvtColor(ColorConversionCodes.GRAY2BGR));
			mat4 = mat5;
			mat2 = new Mat(mat.Size(), MatType.CV_8UC1, new Scalar(255.0));
		}
		try
		{
			using Mat mat6 = CenterCrop(mat4, 0.55);
			using Mat mat7 = CenterCrop(mat2, 0.55);
			using Mat mat8 = new Mat();
			using Mat mat9 = new Mat();
			Cv2.Resize(mat6, mat8, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
			Cv2.Resize(mat7, mat9, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
			Mat mat10 = new Mat();
			Cv2.CvtColor(mat8, mat10, ColorConversionCodes.BGR2GRAY);
			Mat mat11 = mat9.Clone();
			(Mat gray, Mat mask) tuple = ComputeTightEntry(mat4, mat2, mat10, mat11);
			Mat item = tuple.gray;
			Mat item2 = tuple.mask;
			int num = CountMaskPixels(mat11);
			int maskTightPixelCount = ((item2 == mat11) ? num : CountMaskPixels(item2));
			float[] array3 = ComputeColorHistogram(mat8, mat11);
			var (grayStat, maskStat, maskStatPixelCount, colorHistStat) = ComputeStatTightSpriteEntry(mat4, mat2, mat10, mat11, num, array3);
			return new SpriteEntry(mat10, mat11, item, item2, num, maskTightPixelCount, array3, grayStat, maskStat, maskStatPixelCount, colorHistStat);
		}
		finally
		{
			mat4.Dispose();
			mat2.Dispose();
		}
	}

	private static (Mat gray, Mat mask) ComputeTightEntry(Mat bgrFull, Mat alphaFull, Mat defaultGray, Mat defaultMask)
	{
		using Mat mat = new Mat();
		Cv2.Threshold(alphaFull, mat, 0.0, 255.0, ThresholdTypes.Binary);
		Rect rect = Cv2.BoundingRect(mat);
		if (rect.Width <= 0 || rect.Height <= 0)
		{
			return (gray: defaultGray, mask: defaultMask);
		}
		double num = (double)rect.Width * (double)rect.Height;
		double num2 = (double)alphaFull.Cols * (double)alphaFull.Rows;
		if (num / num2 >= 0.7224999999999999)
		{
			return (gray: defaultGray, mask: defaultMask);
		}
		int num3 = Math.Max(rect.Width, rect.Height);
		int num4 = rect.X + rect.Width / 2;
		int num5 = rect.Y + rect.Height / 2;
		int num6 = Math.Max(0, num4 - num3 / 2);
		int num7 = Math.Max(0, num5 - num3 / 2);
		if (num6 + num3 > alphaFull.Cols)
		{
			num6 = alphaFull.Cols - num3;
		}
		if (num7 + num3 > alphaFull.Rows)
		{
			num7 = alphaFull.Rows - num3;
		}
		if (num6 < 0 || num7 < 0 || num3 <= 0)
		{
			return (gray: defaultGray, mask: defaultMask);
		}
		Rect roi = new Rect(num6, num7, num3, num3);
		using Mat mat2 = new Mat(bgrFull, roi);
		using Mat mat3 = new Mat(alphaFull, roi);
		using Mat mat4 = new Mat();
		using Mat mat5 = new Mat();
		Cv2.Resize(mat2, mat4, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Cv2.Resize(mat3, mat5, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Mat mat6 = new Mat();
		Cv2.CvtColor(mat4, mat6, ColorConversionCodes.BGR2GRAY);
		return (gray: mat6, mask: mat5.Clone());
	}

	private static (Mat gray, Mat mask, int count, float[] hist) ComputeStatTightSpriteEntry(Mat bgrFull, Mat alphaFull, Mat defaultGray, Mat defaultMask, int defaultMaskCount, float[] defaultColorHist)
	{
		using Mat mat = new Mat();
		Cv2.Threshold(alphaFull, mat, 0.0, 255.0, ThresholdTypes.Binary);
		Rect rect = Cv2.BoundingRect(mat);
		if (rect.Width <= 0 || rect.Height <= 0)
		{
			return (gray: defaultGray, mask: defaultMask, count: defaultMaskCount, hist: defaultColorHist);
		}
		double num = (double)rect.Width * (double)rect.Height;
		double num2 = (double)alphaFull.Cols * (double)alphaFull.Rows;
		if (num / num2 < 0.7224999999999999)
		{
			return (gray: defaultGray, mask: defaultMask, count: defaultMaskCount, hist: defaultColorHist);
		}
		Rect? rect2 = ComputeStatTightRect(mat);
		if (!rect2.HasValue)
		{
			return (gray: defaultGray, mask: defaultMask, count: defaultMaskCount, hist: defaultColorHist);
		}
		using Mat mat2 = new Mat(bgrFull, rect2.Value);
		using Mat mat3 = new Mat(alphaFull, rect2.Value);
		using Mat mat4 = new Mat();
		using Mat mat5 = new Mat();
		Cv2.Resize(mat2, mat4, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Cv2.Resize(mat3, mat5, new OpenCvSharp.Size(64, 64), 0.0, 0.0, InterpolationFlags.Area);
		Mat mat6 = new Mat();
		Cv2.CvtColor(mat4, mat6, ColorConversionCodes.BGR2GRAY);
		Mat mat7 = mat5.Clone();
		int item = CountMaskPixels(mat7);
		float[] item2 = ComputeColorHistogram(mat4, mat7);
		return (gray: mat6, mask: mat7, count: item, hist: item2);
	}

	private static Mat CenterCrop(Mat src, double ratio)
	{
		int num = Math.Min(src.Cols, src.Rows);
		int num2 = Math.Max(8, (int)((double)num * ratio));
		int x = (src.Cols - num2) / 2;
		int y = (src.Rows - num2) / 2;
		return new Mat(src, new Rect(x, y, num2, num2));
	}

	internal static double MaskedCcoeffNormed(Mat a, Mat b, Mat mask, Mat? captureMask = null)
	{
		if (a.Rows != b.Rows || a.Cols != b.Cols) return 0.0;
		if (a.Rows != mask.Rows || a.Cols != mask.Cols) return 0.0;
		if (captureMask != null && (captureMask.Rows != a.Rows || captureMask.Cols != a.Cols)) return 0.0;
        
        if (!a.IsContinuous() || !b.IsContinuous() || !mask.IsContinuous() || (captureMask != null && !captureMask.IsContinuous()))
            return 0.0; // Assume continuous for simplicity in this optimization. ImRead/ToMat are continuous.

		int total = a.Rows * a.Cols;
		byte[] aData = new byte[total];
		byte[] bData = new byte[total];
		byte[] maskData = new byte[total];
		byte[]? capMaskData = captureMask != null ? new byte[total] : null;

		System.Runtime.InteropServices.Marshal.Copy(a.Data, aData, 0, total);
		System.Runtime.InteropServices.Marshal.Copy(b.Data, bData, 0, total);
		System.Runtime.InteropServices.Marshal.Copy(mask.Data, maskData, 0, total);
		if (captureMask != null)
		{
			System.Runtime.InteropServices.Marshal.Copy(captureMask.Data, capMaskData!, 0, total);
		}

		long num = 0L;
		long num2 = 0L;
		long num3 = 0L;
		
		for (int i = 0; i < total; i++)
		{
			if (maskData[i] != 0 && (capMaskData == null || capMaskData[i] != 0))
			{
				num2 += aData[i];
				num3 += bData[i];
				num++;
			}
		}

		if (num < 64)
		{
			return 0.0;
		}

		double num4 = (double)num2 / (double)num;
		double num5 = (double)num3 / (double)num;
		double num6 = 0.0;
		double num7 = 0.0;
		double num8 = 0.0;
		
		for (int i = 0; i < total; i++)
		{
			if (maskData[i] != 0 && (capMaskData == null || capMaskData[i] != 0))
			{
				double num9 = (double)aData[i] - num4;
				double num10 = (double)bData[i] - num5;
				num6 += num9 * num10;
				num7 += num9 * num9;
				num8 += num10 * num10;
			}
		}

		double num11 = Math.Sqrt(num7 * num8);
		if (!(num11 < 1E-09))
		{
			return num6 / num11;
		}
		return 0.0;
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;
		if (_cache == null)
		{
			return;
		}
		foreach (SpriteEntry value in _cache.Values)
		{
			value.Dispose();
		}
		_cache.Clear();
	}
}
