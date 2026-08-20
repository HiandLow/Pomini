using System;
using System.Drawing;

namespace PokemonHelper.Services.Recognition;

public interface IOcrEngine : IDisposable
{
	string Recognize(Bitmap bmp, OcrLayoutHint hint = OcrLayoutHint.Block);
}
