using System.Drawing;

namespace PokemonHelper.Services.Recognition;

public interface IScreenCapturer
{
	Bitmap CaptureWindowRegion(nint hwnd, RectangleF normalized);

	Bitmap CaptureWindow(nint hwnd)
	{
		return CaptureWindowRegion(hwnd, new RectangleF(0f, 0f, 1f, 1f));
	}

	void SetMonitorTarget(WindowBounds? monitorBounds)
	{
	}
}
