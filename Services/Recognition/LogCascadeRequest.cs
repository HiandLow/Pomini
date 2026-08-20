using System;
using System.Collections.Generic;
using System.Drawing;

namespace PokemonHelper.Services.Recognition;

public sealed class LogCascadeRequest : IDisposable
{
	public Bitmap Frame { get; }

	public int BestScore { get; }

	public IReadOnlyList<string> Vocab { get; }

	public string SourceRaw { get; }

	public LogCascadeRequest(Bitmap frame, int bestScore, IReadOnlyList<string> vocab, string sourceRaw = "")
	{
		Frame = frame;
		BestScore = bestScore;
		Vocab = vocab;
		SourceRaw = sourceRaw;
	}

	public void Dispose()
	{
		Frame.Dispose();
	}
}
