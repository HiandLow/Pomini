namespace PokemonHelper.Utils
{
    // 원본 PCH.Domain.Battle 의 Weather enum
    public enum Weather { None, Sun, Rain, Sand, Snow }

    // 원본 PCH.Domain.Battle 의 Terrain enum
    public enum Terrain { None, Electric, Grassy, Misty, Psychic }

    // 원본 PCH.Recognition 의 StatRank enum
    public enum StatRank { Atk, Def, Spa, Spd, Spe }

    // 원본 PCH.Recognition 의 ScreenKind enum
    public enum ScreenKind { Reflect, LightScreen, AuroraVeil }

    // 원본 PCH.Recognition 의 ItemEffectKind enum
    public enum ItemEffectKind { WhiteHerb, LumBerry, RawstBerry, CheriBerry }

    // --- 이벤트 레코드들 ---
    public sealed record WeatherChange(Weather Weather, bool IsSet);
    public sealed record TerrainChange(Terrain Terrain, bool IsSet);
    public sealed record ScreenChange(ScreenKind Kind, bool IsSet, bool IsMySide);
    public sealed record TailwindChange(bool IsSet, bool? IsMySide);
    public sealed record RankChange(string SubjectRaw, StatRank Stat, int Delta);
    public sealed record BurnInflicted(string SubjectRaw);
    public sealed record ParalysisInflicted(string SubjectRaw);
    public sealed record RestUsed(string SubjectRaw);
    public sealed record AllySwitchChange(bool IsMySide);
    public sealed record ItemEffect(string SubjectRaw, ItemEffectKind Kind);
}
