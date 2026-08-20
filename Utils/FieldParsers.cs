namespace PokemonHelper.Utils
{
    /// <summary>원본 WeatherParser 그대로 이식</summary>
    public static class WeatherParser
    {
        private static readonly (Weather Weather, bool IsSet, string[] Anchors)[] Patterns =
        {
            (Weather.Sun,  true,  new[] { "햇살", "강해졌다" }),
            (Weather.Sun,  false, new[] { "햇살", "원래대로" }),
            (Weather.Rain, true,  new[] { "비가", "내리기시작" }),
            (Weather.Rain, false, new[] { "비가", "그쳤다" }),
            (Weather.Sand, true,  new[] { "모래바람", "불기시작" }),
            (Weather.Sand, false, new[] { "모래바람", "가라앉" }),
            (Weather.Snow, true,  new[] { "눈이", "내리기시작" }),
            (Weather.Snow, false, new[] { "눈이", "그쳤다" })
        };

        public static WeatherChange? Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            foreach (var (weather, isSet, anchors) in Patterns)
            {
                bool ok = true;
                foreach (string text in anchors)
                {
                    int maxDist = text.Length <= 2 ? 1 : 2;
                    if (RankUpParser.FuzzyIndexOf(normalized, text, 0, maxDist) < 0) { ok = false; break; }
                }
                if (ok) return new WeatherChange(weather, isSet);
            }
            return null;
        }
    }

    /// <summary>원본 TerrainParser 그대로 이식</summary>
    public static class TerrainParser
    {
        private static readonly (Terrain Terrain, string[] Anchors)[] SetPatterns =
        {
            (Terrain.Electric, new[] { "전기", "흐르기시작" }),
            (Terrain.Grassy,   new[] { "풀이", "무성해" }),
            (Terrain.Misty,    new[] { "안개", "자욱해" }),
            (Terrain.Psychic,  new[] { "이상한느낌" })
        };

        public static TerrainChange? Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            if (RankUpParser.FuzzyIndexOf(normalized, "발밑", 0, 1) < 0) return null;
            if (RankUpParser.FuzzyIndexOf(normalized, "사라졌", 0, 1) >= 0)
                return new TerrainChange(Terrain.None, false);
            foreach (var (terrain, anchors) in SetPatterns)
            {
                bool ok = true;
                foreach (string text in anchors)
                {
                    int maxDist = text.Length <= 2 ? 1 : 2;
                    if (RankUpParser.FuzzyIndexOf(normalized, text, 0, maxDist) < 0) { ok = false; break; }
                }
                if (ok) return new TerrainChange(terrain, true);
            }
            return null;
        }
    }

    /// <summary>원본 ScreenParser 그대로 이식</summary>
    public static class ScreenParser
    {
        public static ScreenChange? Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            ScreenKind? screenKind =
                Has(normalized, "오로라베일") ? ScreenKind.AuroraVeil :
                Has(normalized, "리플렉터")  ? ScreenKind.Reflect :
                Has(normalized, "빛의장막")  ? ScreenKind.LightScreen :
                (ScreenKind?)null;
            if (!screenKind.HasValue) return null;
            bool isSet   = Has(normalized, "강해졌다");
            bool isUnset = Has(normalized, "없어졌다");
            if (isSet == isUnset) return null;
            bool? side = SideOf(normalized);
            if (!side.HasValue) return null;
            return new ScreenChange(screenKind.Value, isSet, side.Value);
        }

        internal static bool Has(string normalized, string anchor)
            => RankUpParser.FuzzyIndexOf(normalized, anchor, 0, anchor.Length <= 2 ? 1 : 2) >= 0;

        internal static bool? SideOf(string normalized)
        {
            if (Has(normalized, "상대")) return false;
            if (Has(normalized, "우리")) return true;
            return null;
        }
    }

    /// <summary>원본 TailwindParser 그대로 이식</summary>
    public static class TailwindParser
    {
        public static TailwindChange? Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            if (!ScreenParser.Has(normalized, "순풍")) return null;
            bool isSet   = ScreenParser.Has(normalized, "불기시작");
            bool isUnset = ScreenParser.Has(normalized, "멈췄다");
            if (isSet == isUnset) return null;
            bool? isMySide = ScreenParser.SideOf(normalized);
            if (!isMySide.HasValue && isSet) return null;
            return new TailwindChange(isSet, isMySide);
        }
    }
}
