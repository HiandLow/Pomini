namespace PokemonHelper.Services.Recognition;

public sealed record LogEvent(LogEventKind Kind, string? MegaForm = null);
