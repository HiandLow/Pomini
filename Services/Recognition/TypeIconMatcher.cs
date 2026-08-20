using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using OpenCvSharp;
using OpenCvSharp.Extensions;


namespace PokemonHelper.Services.Recognition;

[SupportedOSPlatform("windows")]
public sealed class TypeIconMatcher : IDisposable
{
	private sealed record RefData(Mat Gray, double HueAvg);

	private readonly record struct TypeCandidate(PokemonType Type, double Score, Rect Box);

	public const double DefaultMinScore = 0.3;

	private const double EdgeCropRatio = 0.88;

	private static readonly double[] ScaleSweep = new double[4] { 0.5, 0.7, 0.85, 1.0 };

	internal const double ColorHueDistanceThreshold = 40.0;

	internal const double ColorMinSaturation = 30.0;

	internal const double NormalShapeMinScore = 0.45;

	internal const double AchromaticMaxSaturation = 120.0;

	internal const double IconGlyphMinWhiteFraction = 0.06;

	private const int IconGlyphMinValue = 178;

	private const int IconGlyphMaxSaturation = 64;

	public static readonly IReadOnlyDictionary<PokemonType, string> FileKeys = new Dictionary<PokemonType, string>
	{
		[PokemonType.Normal] = "normal",
		[PokemonType.Fire] = "fire",
		[PokemonType.Water] = "water",
		[PokemonType.Electric] = "electric",
		[PokemonType.Grass] = "grass",
		[PokemonType.Ice] = "ice",
		[PokemonType.Fighting] = "fighting",
		[PokemonType.Poison] = "poison",
		[PokemonType.Ground] = "ground",
		[PokemonType.Flying] = "flying",
		[PokemonType.Psychic] = "psychic",
		[PokemonType.Bug] = "bug",
		[PokemonType.Rock] = "rock",
		[PokemonType.Ghost] = "ghost",
		[PokemonType.Dragon] = "dragon",
		[PokemonType.Dark] = "dark",
		[PokemonType.Steel] = "steel",
		[PokemonType.Fairy] = "fairy"
	};

	private readonly string? _overrideDir;

	private readonly object _loadLock = new object();

	private Dictionary<PokemonType, RefData>? _cache;

	private bool _disposed;

	internal const double NmsOverlapThreshold = 0.4;

	public int CacheCount
	{
		get
		{
			EnsureLoaded();
			return _cache?.Count ?? 0;
		}
	}

	public TypeIconMatcher(string? overrideDir = null)
	{
		_overrideDir = overrideDir;
	}

	public IReadOnlyList<TypeMatch> TopN(Bitmap captured, int topN = 2, double minScore = 0.3)
	{
		if (topN <= 0 || captured.Width <= 0 || captured.Height <= 0)
		{
			return Array.Empty<TypeMatch>();
		}
		EnsureLoaded();
		if (_cache == null || _cache.Count == 0)
		{
			return Array.Empty<TypeMatch>();
		}
		using Mat mat = captured.ToMat();
		using Mat capBgr = ((mat.Channels() == 4) ? mat.CvtColor(ColorConversionCodes.BGRA2BGR) : mat.Clone());
		using Mat mat2 = ToGray(mat);
		int rows = mat2.Rows;
		int cols = mat2.Cols;
		if (rows < 8 || cols < 8)
		{
			return Array.Empty<TypeMatch>();
		}
		List<TypeCandidate> list = new List<TypeCandidate>(2);
		foreach (Rect item in ComputeIconCells(cols, rows))
		{
			if (!(CellWhiteGlyphFraction(capBgr, item) < 0.06))
			{
				TypeCandidate? typeCandidate = ClassifyCell(capBgr, mat2, item, minScore);
				if (typeCandidate.HasValue)
				{
					list.Add(typeCandidate.Value);
				}
			}
		}
		list.Sort((TypeCandidate a, TypeCandidate b) => b.Score.CompareTo(a.Score));
		List<TypeMatch> list2 = new List<TypeMatch>(Math.Min(topN, list.Count));
		HashSet<PokemonType> hashSet = new HashSet<PokemonType>();
		foreach (TypeCandidate item2 in list)
		{
			if (list2.Count >= topN)
			{
				break;
			}
			if (hashSet.Add(item2.Type))
			{
				list2.Add(new TypeMatch(item2.Type, item2.Score));
			}
		}
		return list2;
	}

	internal static IReadOnlyList<Rect> ComputeIconCells(int capW, int capH)
	{
		List<Rect> list = new List<Rect>(2);
		int num = Math.Max(0, capW - capH);
		list.Add(new Rect(num, 0, Math.Min(capH, capW - num), capH));
		if (capW >= capH * 3 / 2)
		{
			list.Add(new Rect(0, 0, Math.Min(capH, capW), capH));
		}
		return list;
	}

	private static double CellWhiteGlyphFraction(Mat capBgr, Rect cell)
	{
		int num = Math.Max(0, Math.Min(cell.X, capBgr.Cols - 1));
		int num2 = Math.Max(0, Math.Min(cell.Y, capBgr.Rows - 1));
		int num3 = Math.Max(1, Math.Min(cell.Width, capBgr.Cols - num));
		int num4 = Math.Max(1, Math.Min(cell.Height, capBgr.Rows - num2));
		using Mat mat = new Mat(capBgr, new Rect(num, num2, num3, num4));
		using Mat mat2 = new Mat();
		Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGR2HSV);
		Mat.Indexer<Vec3b> genericIndexer = mat2.GetGenericIndexer<Vec3b>();
		int num5 = 0;
		for (int i = 0; i < num4; i++)
		{
			for (int j = 0; j < num3; j++)
			{
				Vec3b vec3b = genericIndexer[i, j];
				if (vec3b.Item2 > 178 && vec3b.Item1 < 64)
				{
					num5++;
				}
			}
		}
		int num6 = num3 * num4;
		return (num6 == 0) ? 0.0 : ((double)num5 / (double)num6);
	}

	private TypeCandidate? ClassifyCell(Mat capBgr, Mat capGray, Rect cell, double minScore)
	{
		using Mat mat = new Mat(capGray, cell).Clone();
		using Mat bgr = new Mat(capBgr, cell).Clone();
		int rows = mat.Rows;
		int cols = mat.Cols;
		if (rows < 8 || cols < 8)
		{
			return null;
		}
		TypeCandidate? result = null;
		foreach (KeyValuePair<PokemonType, RefData> item in _cache)
		{
			item.Deconstruct(out var key, out var value);
			PokemonType pokemonType = key;
			RefData refData = value;
			double num = double.MinValue;
			Rect rect = default(Rect);
			double[] scaleSweep = ScaleSweep;
			foreach (double num2 in scaleSweep)
			{
				int num3 = Math.Max(8, (int)((double)rows * num2));
				if (num3 > rows)
				{
					continue;
				}
				int num4 = Math.Max(8, (int)((double)refData.Gray.Cols / (double)refData.Gray.Rows * (double)num3));
				if (num4 > cols)
				{
					continue;
				}
				using Mat mat2 = new Mat();
				Cv2.Resize(refData.Gray, mat2, new OpenCvSharp.Size(num4, num3), 0.0, 0.0, InterpolationFlags.Area);
				using Mat mat3 = new Mat();
				Cv2.MatchTemplate(mat, mat2, mat3, TemplateMatchModes.CCoeffNormed);
				Cv2.MinMaxLoc(mat3, out var _, out var maxVal, out var _, out var maxLoc);
				if (maxVal > num)
				{
					num = maxVal;
					rect = new Rect(maxLoc.X, maxLoc.Y, num4, num3);
				}
			}
			if (num <= double.MinValue || double.IsNaN(num))
			{
				continue;
			}
			if (refData.HueAvg >= 0.0)
			{
				double num5 = ExtractCaptureHue(bgr, rect);
				if (num5 >= 0.0 && HueDistance(refData.HueAvg, num5) > 40.0)
				{
					continue;
				}
			}
			else if (ExtractCaptureSaturation(bgr, rect) > 120.0)
			{
				continue;
			}
			double num6 = ((pokemonType == PokemonType.Normal) ? Math.Max(minScore, 0.45) : minScore);
			if (!(num < num6) && (!result.HasValue || num > result.Value.Score))
			{
				result = new TypeCandidate(pokemonType, num, rect);
			}
		}
		return result;
	}

	internal (int PerTypeCount, IReadOnlyList<(PokemonType Type, double Score, Rect Box, bool Picked, string Reason)> Steps) DiagnoseTopNFull(Bitmap captured, int topN, double minScore)
	{
		List<(PokemonType, double, Rect, bool, string)> list = new List<(PokemonType, double, Rect, bool, string)>();
		EnsureLoaded();
		if (_cache == null || _cache.Count == 0)
		{
			return (PerTypeCount: 0, Steps: list);
		}
		using Mat mat = captured.ToMat();
		using Mat bgr = ((mat.Channels() == 4) ? mat.CvtColor(ColorConversionCodes.BGRA2BGR) : mat.Clone());
		using Mat mat2 = ToGray(mat);
		int rows = mat2.Rows;
		int cols = mat2.Cols;
		if (rows < 8 || cols < 8)
		{
			return (PerTypeCount: 0, Steps: list);
		}
		List<TypeCandidate> list2 = new List<TypeCandidate>(_cache.Count);
		foreach (KeyValuePair<PokemonType, RefData> item2 in _cache)
		{
			item2.Deconstruct(out var key, out var value);
			PokemonType type = key;
			RefData refData = value;
			double num = double.MinValue;
			Rect rect = default(Rect);
			double[] scaleSweep = ScaleSweep;
			foreach (double num2 in scaleSweep)
			{
				int num3 = Math.Max(8, (int)((double)rows * num2));
				if (num3 > rows)
				{
					continue;
				}
				int num4 = Math.Max(8, (int)((double)refData.Gray.Cols / (double)refData.Gray.Rows * (double)num3));
				if (num4 > cols)
				{
					continue;
				}
				using Mat mat3 = new Mat();
				Cv2.Resize(refData.Gray, mat3, new OpenCvSharp.Size(num4, num3), 0.0, 0.0, InterpolationFlags.Area);
				using Mat mat4 = new Mat();
				Cv2.MatchTemplate(mat2, mat3, mat4, TemplateMatchModes.CCoeffNormed);
				Cv2.MinMaxLoc(mat4, out var _, out var maxVal, out var _, out var maxLoc);
				if (maxVal > num)
				{
					num = maxVal;
					rect = new Rect(maxLoc.X, maxLoc.Y, num4, num3);
				}
			}
			if (num <= double.MinValue || double.IsNaN(num))
			{
				continue;
			}
			if (refData.HueAvg >= 0.0)
			{
				double num5 = ExtractCaptureHue(bgr, rect);
				if (num5 >= 0.0 && HueDistance(refData.HueAvg, num5) > 40.0)
				{
					continue;
				}
			}
			else if (ExtractCaptureSaturation(bgr, rect) > 120.0)
			{
				continue;
			}
			list2.Add(new TypeCandidate(type, num, rect));
		}
		int count = list2.Count;
		list2.Sort((TypeCandidate a, TypeCandidate b) => b.Score.CompareTo(a.Score));
		List<TypeCandidate> list3 = new List<TypeCandidate>(topN);
		foreach (TypeCandidate item3 in list2)
		{
			string item;
			if (list3.Count >= topN)
			{
				item = "topN reached";
				list.Add((item3.Type, item3.Score, item3.Box, false, item));
				continue;
			}
			double num6 = ((item3.Type == PokemonType.Normal) ? Math.Max(minScore, 0.45) : minScore);
			if (item3.Score < num6)
			{
				item = $"score<itemMin({num6:F2})";
				list.Add((item3.Type, item3.Score, item3.Box, false, item));
				continue;
			}
			bool flag = false;
			PokemonType value2 = PokemonType.Normal;
			foreach (TypeCandidate item4 in list3)
			{
				if (IsSameIcon(item3.Box, item4.Box))
				{
					flag = true;
					value2 = item4.Type;
					break;
				}
			}
			if (!flag)
			{
				list3.Add(item3);
				item = "PICK";
			}
			else
			{
				item = $"overlap with {value2}";
			}
			list.Add((item3.Type, item3.Score, item3.Box, !flag, item));
		}
		return (PerTypeCount: count, Steps: list);
	}

	internal IReadOnlyList<(PokemonType Type, double Score, Rect Box, double RefHue, double CapHue, bool Passed)> DiagnoseTopN(Bitmap captured)
	{
		List<(PokemonType, double, Rect, double, double, bool)> list = new List<(PokemonType, double, Rect, double, double, bool)>();
		EnsureLoaded();
		if (_cache == null || _cache.Count == 0)
		{
			return list;
		}
		using Mat mat = captured.ToMat();
		using Mat bgr = ((mat.Channels() == 4) ? mat.CvtColor(ColorConversionCodes.BGRA2BGR) : mat.Clone());
		using Mat mat2 = ToGray(mat);
		int rows = mat2.Rows;
		int cols = mat2.Cols;
		foreach (KeyValuePair<PokemonType, RefData> item3 in _cache)
		{
			item3.Deconstruct(out var key, out var value);
			PokemonType item = key;
			RefData refData = value;
			double num = double.MinValue;
			Rect rect = default(Rect);
			double[] scaleSweep = ScaleSweep;
			foreach (double num2 in scaleSweep)
			{
				int num3 = Math.Max(8, (int)((double)rows * num2));
				if (num3 > rows)
				{
					continue;
				}
				int num4 = Math.Max(8, (int)((double)refData.Gray.Cols / (double)refData.Gray.Rows * (double)num3));
				if (num4 > cols)
				{
					continue;
				}
				using Mat mat3 = new Mat();
				Cv2.Resize(refData.Gray, mat3, new OpenCvSharp.Size(num4, num3), 0.0, 0.0, InterpolationFlags.Area);
				using Mat mat4 = new Mat();
				Cv2.MatchTemplate(mat2, mat3, mat4, TemplateMatchModes.CCoeffNormed);
				Cv2.MinMaxLoc(mat4, out var _, out var maxVal, out var _, out var maxLoc);
				if (maxVal > num)
				{
					num = maxVal;
					rect = new Rect(maxLoc.X, maxLoc.Y, num4, num3);
				}
			}
			double num5 = -1.0;
			bool item2;
			if (num <= double.MinValue || double.IsNaN(num))
			{
				item2 = false;
			}
			else if (refData.HueAvg < 0.0)
			{
				item2 = ExtractCaptureSaturation(bgr, rect) <= 120.0;
			}
			else
			{
				num5 = ExtractCaptureHue(bgr, rect);
				item2 = num5 < 0.0 || HueDistance(refData.HueAvg, num5) <= 40.0;
			}
			list.Add((item, num, rect, refData.HueAvg, num5, item2));
		}
		return list;
	}

	internal static bool IsSameIcon(Rect a, Rect b)
	{
		int num = Math.Max(a.X, b.X);
		int num2 = Math.Max(a.Y, b.Y);
		int num3 = Math.Min(a.X + a.Width, b.X + b.Width);
		int num4 = Math.Min(a.Y + a.Height, b.Y + b.Height);
		if (num3 <= num || num4 <= num2)
		{
			return false;
		}
		double num5 = (double)(num3 - num) * (double)(num4 - num2);
		double val = (double)a.Width * (double)a.Height;
		double val2 = (double)b.Width * (double)b.Height;
		double num6 = Math.Min(val, val2);
		if (num6 > 0.0)
		{
			return num5 / num6 > 0.4;
		}
		return false;
	}

	public TypeMatch? Match(Bitmap captured, double minScore = 0.3)
	{
		IReadOnlyList<TypeMatch> readOnlyList = TopN(captured, 1, minScore);
		if (readOnlyList.Count != 0)
		{
			return readOnlyList[0];
		}
		return null;
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
			Dictionary<PokemonType, RefData> dictionary = new Dictionary<PokemonType, RefData>();
			foreach (KeyValuePair<PokemonType, string> fileKey in FileKeys)
			{
				fileKey.Deconstruct(out var key, out var value);
				PokemonType key2 = key;
				string key3 = value;
				string text = ResolvePath(key3);
				if (text == null || !File.Exists(text))
				{
					continue;
				}
				try
				{
					using Bitmap src = new Bitmap(text);
					using Mat mat = src.ToMat();
					using Mat src2 = ToGray(mat);
					using Mat mat2 = CenterCrop(src2, 0.88);
					double hueAvg = ExtractRefHue(mat);
					dictionary[key2] = new RefData(mat2.Clone(), hueAvg);
				}
				catch
				{
				}
			}
			_cache = dictionary;
		}
	}

	private static double ExtractRefHue(Mat bgraOrBgr)
	{
		Mat mat = null;
		bool flag = false;
		bool flag2 = false;
		Mat mat2;
		if (bgraOrBgr.Channels() == 4)
		{
			mat2 = new Mat();
			Cv2.CvtColor(bgraOrBgr, mat2, ColorConversionCodes.BGRA2BGR);
			flag = true;
			Mat[] array = Cv2.Split(bgraOrBgr);
			mat = array[3];
			array[0].Dispose();
			array[1].Dispose();
			array[2].Dispose();
			flag2 = true;
		}
		else
		{
			if (bgraOrBgr.Channels() != 3)
			{
				return -1.0;
			}
			mat2 = bgraOrBgr;
		}
		try
		{
			int num = Math.Max(1, (int)((double)mat2.Cols * 0.88));
			int num2 = Math.Max(1, (int)((double)mat2.Rows * 0.88));
			int x = (mat2.Cols - num) / 2;
			int y = (mat2.Rows - num2) / 2;
			Rect roi = new Rect(x, y, num, num2);
			using Mat mat3 = new Mat(mat2, roi);
			using Mat mat4 = new Mat();
			Cv2.CvtColor(mat3, mat4, ColorConversionCodes.BGR2HSV);
			Mat mat5 = ((mat == null) ? null : new Mat(mat, roi));
			try
			{
				return ComputeMeanHueFromHsv(mat4, mat5);
			}
			finally
			{
				mat5?.Dispose();
			}
		}
		finally
		{
			if (flag)
			{
				mat2.Dispose();
			}
			if (flag2)
			{
				mat?.Dispose();
			}
		}
	}

	private static double ComputeMeanHueFromHsv(Mat hsv, Mat? alphaMask)
	{
		if (!hsv.IsContinuous() || (alphaMask != null && !alphaMask.IsContinuous())) return -1.0;

		int total = hsv.Rows * hsv.Cols;
		byte[] hsvData = new byte[total * 3]; // Vec3b has 3 bytes
		byte[]? maskData = alphaMask != null ? new byte[total] : null;

		System.Runtime.InteropServices.Marshal.Copy(hsv.Data, hsvData, 0, total * 3);
		if (alphaMask != null)
		{
			System.Runtime.InteropServices.Marshal.Copy(alphaMask.Data, maskData!, 0, total);
		}

		double num = 0.0;
		double num2 = 0.0;
		int num3 = 0;
		
		for (int i = 0; i < total; i++)
		{
			if (maskData == null || maskData[i] >= 128)
			{
				byte h = hsvData[i * 3 + 0];
				byte s = hsvData[i * 3 + 1];
				byte v = hsvData[i * 3 + 2];
				
				if (!(s < 30))
				{
					double num4 = (double)h * 2.0 * Math.PI / 180.0;
					num += Math.Sin(num4);
					num2 += Math.Cos(num4);
					num3++;
				}
			}
		}

		if (num3 == 0)
		{
			return -1.0;
		}
		double num5 = Math.Atan2(num, num2);
		if (num5 < 0.0)
		{
			num5 += Math.PI * 2.0;
		}
		return num5 * 180.0 / Math.PI / 2.0;
	}

	private static double ExtractCaptureHue(Mat bgr, Rect region)
	{
		int num = Math.Max(0, Math.Min(region.X, bgr.Cols - 1));
		int num2 = Math.Max(0, Math.Min(region.Y, bgr.Rows - 1));
		int width = Math.Max(1, Math.Min(region.Width, bgr.Cols - num));
		int height = Math.Max(1, Math.Min(region.Height, bgr.Rows - num2));
		using Mat mat = new Mat(bgr, new Rect(num, num2, width, height));
		using Mat mat2 = new Mat();
		Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGR2HSV);
		return ComputeMeanHueFromHsv(mat2, null);
	}

	private static double ExtractCaptureSaturation(Mat bgr, Rect region)
	{
		int num = Math.Max(0, Math.Min(region.X, bgr.Cols - 1));
		int num2 = Math.Max(0, Math.Min(region.Y, bgr.Rows - 1));
		int width = Math.Max(1, Math.Min(region.Width, bgr.Cols - num));
		int height = Math.Max(1, Math.Min(region.Height, bgr.Rows - num2));
		using Mat mat = new Mat(bgr, new Rect(num, num2, width, height));
		using Mat mat2 = new Mat();
		Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGR2HSV);
		return Cv2.Mean(mat2).Val1;
	}

	internal static double HueDistance(double h1, double h2)
	{
		if (h1 < 0.0 || h2 < 0.0)
		{
			return double.NaN;
		}
		double num = Math.Abs(h1 - h2);
		return Math.Min(num, 360.0 - num);
	}

	private static Mat ToGray(Mat src)
	{
		return src.Channels() switch
		{
			1 => src.Clone(), 
			4 => src.CvtColor(ColorConversionCodes.BGRA2GRAY), 
			_ => src.CvtColor(ColorConversionCodes.BGR2GRAY), 
		};
	}

	private static Mat CenterCrop(Mat src, double ratio)
	{
		int num = Math.Max(8, (int)((double)src.Cols * ratio));
		int num2 = Math.Max(8, (int)((double)src.Rows * ratio));
		int x = (src.Cols - num) / 2;
		int y = (src.Rows - num2) / 2;
		return new Mat(src, new Rect(x, y, num, num2));
	}

	private string? ResolvePath(string key)
	{
		if (_overrideDir != null)
		{
			return Path.Combine(_overrideDir, key + ".png");
		}
		string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PCH", "type-icons", key + ".png");
		if (File.Exists(text))
		{
			return text;
		}
		return Path.Combine(AppContext.BaseDirectory, "data", "type-icons", key + ".png");
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
		foreach (RefData value in _cache.Values)
		{
			value.Gray.Dispose();
		}
		_cache.Clear();
	}
}
