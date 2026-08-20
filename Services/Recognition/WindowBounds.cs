namespace PokemonHelper.Services.Recognition;

public readonly record struct WindowBounds(int Left, int Top, int Width, int Height)
{
	public int Right => Left + Width;

	public int Bottom => Top + Height;
}
