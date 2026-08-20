using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace PokemonHelper.Services
{
	public static class OcrCache
	{
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
	}
}
