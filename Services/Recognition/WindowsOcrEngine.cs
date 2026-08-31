using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace PokemonHelper.Services.Recognition
{
	public sealed class WindowsOcrEngine : IOcrEngine, IDisposable
	{
		private readonly OcrEngine _engine;

		public WindowsOcrEngine(string lang = "ko")
		{
			_engine = OcrEngine.TryCreateFromLanguage(new Language(lang)) ?? OcrEngine.TryCreateFromUserProfileLanguages() ?? throw new InvalidOperationException($"Windows OCR {lang} 모델을 찾지 못했습니다.");
		}

		public string Recognize(Bitmap bmp, OcrLayoutHint hint = OcrLayoutHint.Block)
		{
			using SoftwareBitmap bitmap = BitmapToSoftwareBitmap(bmp);
			string text = string.Concat(_engine.RecognizeAsync(bitmap).AsTask().GetAwaiter()
				.GetResult()
				.Lines.Select((OcrLine l) => l.Text));
			StringBuilder stringBuilder = new StringBuilder(text.Length);
			string text2 = text;
			foreach (char c in text2)
			{
				if (!char.IsWhiteSpace(c) && (IsHangul(c) || c <= '\u007f'))
				{
					stringBuilder.Append(c);
				}
			}
			return stringBuilder.ToString();
		}

		private static bool IsHangul(char c)
		{
			if ((c < '가' || c > '힣') && (c < 'ᄀ' || c > 'ᇿ'))
			{
				if (c >= '\u3130')
				{
					return c <= '\u318f';
				}
				return false;
			}
			return true;
		}

		private static SoftwareBitmap BitmapToSoftwareBitmap(Bitmap bmp)
		{
			using Bitmap bitmap = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.DrawImageUnscaled(bmp, 0, 0);
			}
			Rectangle rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
			BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
			try
			{
				int num = bitmapData.Stride * bitmapData.Height;
				byte[] array = new byte[num];
				Marshal.Copy(bitmapData.Scan0, array, 0, num);
				return SoftwareBitmap.CreateCopyFromBuffer(array.AsBuffer(), BitmapPixelFormat.Bgra8, bitmap.Width, bitmap.Height, BitmapAlphaMode.Premultiplied);
			}
			finally
			{
				bitmap.UnlockBits(bitmapData);
			}
		}

		public void Dispose()
		{
		}
	}
}
