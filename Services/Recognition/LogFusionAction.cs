using System.Drawing;

namespace PokemonHelper.Services.Recognition;

public sealed record LogFusionAction(Bitmap Image, bool IsFused, LogFusionActionKind Kind = LogFusionActionKind.Passthrough, int GroupId = 0, int FrameCount = 0);
