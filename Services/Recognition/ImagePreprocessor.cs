using System;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace PokemonHelper.Services.Recognition;

public static class ImagePreprocessor
{
	public static Bitmap BinarizeForLog(Bitmap src, int scale = 2, double threshold = 180.0)
	{
		if (src.Width <= 0 || src.Height <= 0)
		{
			return new Bitmap(src);
		}
		using Mat mat = src.ToMat();
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
		Cv2.Resize(mat2, mat3, new OpenCvSharp.Size(Math.Max(1, src.Width * scale), Math.Max(1, src.Height * scale)), 0.0, 0.0, InterpolationFlags.Cubic);
		using Mat mat4 = new Mat();
		Cv2.Threshold(mat3, mat4, threshold, 255.0, ThresholdTypes.Binary);
		using Mat mat5 = new Mat();
		Cv2.CvtColor(mat4, mat5, ColorConversionCodes.GRAY2BGR);
		return mat5.ToBitmap();
	}

	public static Bitmap BinarizeByHsv(Bitmap src, Scalar lowerBound, Scalar upperBound, int scale = 2)
	{
		if (src.Width <= 0 || src.Height <= 0)
		{
			return new Bitmap(src);
		}
		using Mat mat = src.ToMat();
		using Mat mat2 = new Mat();
		if (mat.Channels() == 4)
		{
			Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGRA2BGR);
		}
		else
		{
			mat.CopyTo(mat2);
		}

		using Mat hsv = new Mat();
		Cv2.CvtColor(mat2, hsv, ColorConversionCodes.BGR2HSV);

		using Mat mask = new Mat();
		Cv2.InRange(hsv, lowerBound, upperBound, mask);

		using Mat resizedMask = new Mat();
		Cv2.Resize(mask, resizedMask, new OpenCvSharp.Size(Math.Max(1, src.Width * scale), Math.Max(1, src.Height * scale)), 0.0, 0.0, InterpolationFlags.Cubic);

		using Mat result = new Mat();
		Cv2.CvtColor(resizedMask, result, ColorConversionCodes.GRAY2BGR);
		return result.ToBitmap();
	}

	public static Bitmap UpscaleAndEnhance(Bitmap src, int scale = 3)
	{
		if (src.Width <= 0 || src.Height <= 0)
		{
			return new Bitmap(src);
		}
		using Mat mat = src.ToMat();
		using Mat mat2 = new Mat();
		if (mat.Channels() == 4)
		{
			Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGRA2BGR);
		}
		else
		{
			mat.CopyTo(mat2);
		}
		using Mat mat3 = new Mat();
		Cv2.CvtColor(mat2, mat3, ColorConversionCodes.BGR2HSV);
		Mat[] array = Cv2.Split(mat3);
		using Mat mat4 = array[2];
		array[0].Dispose();
		array[1].Dispose();
		using Mat mat5 = new Mat();
		Cv2.Resize(mat4, mat5, new OpenCvSharp.Size(src.Width * scale, src.Height * scale), 0.0, 0.0, InterpolationFlags.Cubic);
		using CLAHE cLAHE = Cv2.CreateCLAHE(2.0, new OpenCvSharp.Size(8, 8));
		using Mat mat6 = new Mat();
		cLAHE.Apply(mat5, mat6);
		using Mat mat7 = new Mat();
		Cv2.CvtColor(mat6, mat7, ColorConversionCodes.GRAY2BGR);
		return mat7.ToBitmap();
	}
}
