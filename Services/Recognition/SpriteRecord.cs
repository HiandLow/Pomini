namespace PokemonHelper.Services.Recognition;

public sealed record SpriteRecord(int DexId, string FormKey, string NameKo, string DisplayName, string IconFile, string IconUrl, ulong DHashBits, string DHashHex);
