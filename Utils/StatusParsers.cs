using System;
using System.Collections.Generic;

namespace PokemonHelper.Utils
{
    /// <summary>원본 BurnParser 그대로 이식</summary>
    public static class BurnParser
    {
        private const string Keyword = "화상을입었다";

        public static IReadOnlyList<BurnInflicted> Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return Array.Empty<BurnInflicted>();
            var list = new List<BurnInflicted>();
            int num = 0;
            while (num < normalized.Length)
            {
                int idx = RankUpParser.FuzzyIndexOf(normalized, Keyword, num, 2);
                if (idx < 0) break;
                string text = normalized.Substring(num, idx - num)
                    .TrimStart('!', '.', ',', ' ');
                if (text.EndsWith("에게", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 2);
                else if (text.EndsWith("는", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 1);
                list.Add(new BurnInflicted(text));
                num = idx + Keyword.Length;
            }
            return list;
        }
    }

    /// <summary>원본 ParalysisParser 그대로 이식</summary>
    public static class ParalysisParser
    {
        private const string Anchor = "마비되어";
        private const string Tail   = "어려워";

        public static IReadOnlyList<ParalysisInflicted> Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return Array.Empty<ParalysisInflicted>();
            if (RankUpParser.FuzzyIndexOf(normalized, Tail, 0, 1) < 0) return Array.Empty<ParalysisInflicted>();
            var list = new List<ParalysisInflicted>();
            int num = 0;
            while (num < normalized.Length)
            {
                int idx = RankUpParser.FuzzyIndexOf(normalized, Anchor, num, 2);
                if (idx < 0 || RankUpParser.FuzzyIndexOf(normalized, Tail, idx, 1) < 0) break;
                string text = normalized.Substring(num, idx - num)
                    .TrimStart('!', '.', ',', ' ');
                if (text.EndsWith("에게", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 2);
                else if (text.Length > 0 && "은는이가을를".Contains(text[text.Length - 1]))
                    text = text.Substring(0, text.Length - 1);
                list.Add(new ParalysisInflicted(text));
                num = idx + Anchor.Length;
            }
            return list;
        }
    }

    /// <summary>원본 RestParser 그대로 이식</summary>
    public static class RestParser
    {
        private const string Anchor = "잠이들어건강해졌다";

        public static IReadOnlyList<RestUsed> Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return Array.Empty<RestUsed>();
            var list = new List<RestUsed>();
            int num = 0;
            while (num < normalized.Length)
            {
                int idx = RankUpParser.FuzzyIndexOf(normalized, Anchor, num, 2);
                if (idx < 0) break;
                string text = normalized.Substring(num, idx - num)
                    .TrimStart('!', '.', ',', ' ');
                if (text.EndsWith("은", StringComparison.Ordinal) || text.EndsWith("는", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 1);
                list.Add(new RestUsed(text));
                int end = idx + Anchor.Length;
                int bang = normalized.IndexOf('!', end);
                num = bang >= 0 ? bang + 1 : end;
            }
            return list;
        }
    }

    /// <summary>원본 AllySwitchParser 그대로 이식</summary>
    public static class AllySwitchParser
    {
        private const string Anchor = "자리를바꿨다";

        public static AllySwitchChange? Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            if (RankUpParser.FuzzyIndexOf(normalized, Anchor, 0, 2) < 0) return null;
            return new AllySwitchChange(RankUpParser.FuzzyIndexOf(normalized, "상대", 0, 1) != 0);
        }
    }

    /// <summary>원본 ItemEffectParser 그대로 이식</summary>
    public static class ItemEffectParser
    {
        private static readonly (string Anchor, string VerbStem, ItemEffectKind Kind)[] Anchors =
        {
            ("하양허브", "되돌렸", ItemEffectKind.WhiteHerb),
            ("리샘열매", "나았",   ItemEffectKind.LumBerry),
            ("복분열매", "나았",   ItemEffectKind.RawstBerry),
            ("버치열매", "나았",   ItemEffectKind.CheriBerry)
        };

        private const int VerbSearchSpan = 20;

        public static IReadOnlyList<ItemEffect> Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return Array.Empty<ItemEffect>();
            var list = new List<ItemEffect>();
            int num = 0;
            while (num < normalized.Length)
            {
                int bestIdx = -1, bestLen = 0;
                string verbStem = "";
                ItemEffectKind kind = ItemEffectKind.WhiteHerb;

                foreach (var (anchor, verb, k) in Anchors)
                {
                    int idx = RankUpParser.FuzzyIndexOf(normalized, anchor, num, 2);
                    if (idx >= 0 && (bestIdx < 0 || idx < bestIdx))
                    { bestIdx = idx; bestLen = anchor.Length; verbStem = verb; kind = k; }
                }
                if (bestIdx < 0) break;

                int afterAnchor = bestIdx + bestLen;
                int limit = Math.Min(normalized.Length, afterAnchor + VerbSearchSpan);
                int verbIdx = RankUpParser.FuzzyIndexOf(normalized, verbStem, afterAnchor, 2);
                if (verbIdx < 0 || verbIdx >= limit) { num = afterAnchor; continue; }

                string subject = normalized.Substring(num, bestIdx - num)
                    .TrimStart('!', '.', ',', ' ');
                if (subject.EndsWith("은", StringComparison.Ordinal) || subject.EndsWith("는", StringComparison.Ordinal))
                    subject = subject.Substring(0, subject.Length - 1);
                list.Add(new ItemEffect(subject, kind));

                int end = verbIdx + verbStem.Length;
                int bang = normalized.IndexOf('!', end);
                num = bang >= 0 ? bang + 1 : end;
            }
            return list;
        }
    }
}
