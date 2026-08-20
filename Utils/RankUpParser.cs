using System;
using System.Collections.Generic;
using System.Linq;

namespace PokemonHelper.Utils
{
    /// <summary>
    /// 원본 PCH.Recognition.RankUpParser 그대로 이식
    /// </summary>
    public static class RankUpParser
    {
        public sealed record MultiStatMove(string AnchorKo, IReadOnlyList<StatRank> Stats, int Delta);

        private static readonly (string Keyword, StatRank Stat)[] StatKeywords = new (string, StatRank)[7]
        {
            ("특수공격", StatRank.Spa),
            ("특공",   StatRank.Spa),
            ("특수방어", StatRank.Spd),
            ("특방",   StatRank.Spd),
            ("스피드",  StatRank.Spe),
            ("공격",   StatRank.Atk),
            ("방어",   StatRank.Def)
        };

        internal const int MaxVerbJamoDistance = 3;
        internal const int MaxStatJamoDistance = 1;
        internal const int MaxAdverbJamoDistance = 1;
        public const int BellyDrumDelta = 12;

        public static readonly StatRank[] NoRetreatStats = { StatRank.Atk, StatRank.Def, StatRank.Spa, StatRank.Spd, StatRank.Spe };

        public static readonly MultiStatMove[] MultiStatMoves = new MultiStatMove[3]
        {
            new("배수의진", NoRetreatStats, 1),
            new("원시의힘", NoRetreatStats, 1),
            new("은빛바람", NoRetreatStats, 1)
        };

        public static IReadOnlyList<RankChange> Parse(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return Array.Empty<RankChange>();
            var list = new List<RankChange>();

            int num = FuzzyIndexOf(normalized, "풀파워로만들었다", 0, 3);
            if (num > 0)
            {
                int num2 = FuzzyIndexOf(normalized, "체력을깎아서", 0, 2);
                if (num2 < 0 || num2 > num) num2 = num;
                string text = normalized.Substring(0, num2);
                if (text.EndsWith("는", StringComparison.Ordinal) || text.EndsWith("은", StringComparison.Ordinal))
                    text = text.Substring(0, text.Length - 1);
                list.Add(new RankChange(text, StatRank.Atk, 12));
            }

            int num3 = 0;
            while (num3 < normalized.Length)
            {
                int num4 = FuzzyIndexOf(normalized, "올라갔다", num3, 3);
                int num5 = FuzzyIndexOf(normalized, "떨어졌다", num3, 3);
                if (num4 < 0 && num5 < 0) break;

                int num6, num7;
                if (num4 < 0) { num6 = num5; num7 = -1; }
                else if (num5 < 0) { num6 = num4; num7 = 1; }
                else if (num4 < num5) { num6 = num4; num7 = 1; }
                else { num6 = num5; num7 = -1; }

                int num8 = 1;
                int num9 = Math.Max(0, num6 - 8);
                string haystack = normalized.Substring(num9, num6 - num9);
                if (FuzzyIndexOf(haystack, "매우크게", 0, 1) >= 0) num8 = 3;
                else if (FuzzyIndexOf(haystack, "크게", 0, 1) >= 0) num8 = 2;

                int num10 = Math.Max(num3, num6 - 15);
                string text3 = normalized.Substring(num10, num6 - num10);
                bool[] array = new bool[text3.Length];
                var hashSet = new HashSet<StatRank>();
                var list2 = new List<(StatRank, int, int)>();

                foreach (var (item, item2) in StatKeywords)
                {
                    int num11 = 0;
                    while (num11 < text3.Length)
                    {
                        int num12 = FuzzyIndexOf(text3, item, num11, 1);
                        if (num12 < 0) break;
                        bool flag = false;
                        for (int j = num12; j < num12 + item.Length; j++)
                            if (array[j]) { flag = true; break; }
                        if (!flag && hashSet.Add(item2))
                        {
                            for (int k = num12; k < num12 + item.Length; k++) array[k] = true;
                            list2.Add((item2, num10 + num12, item.Length));
                            break;
                        }
                        num11 = num12 + 1;
                    }
                }

                if (list2.Count == 0) { num3 = num6 + 4; continue; }
                list2.Sort((a, b) => a.Item2.CompareTo(b.Item2));

                if (list2.Count >= 2)
                {
                    bool flag2 = false;
                    for (int n = 0; n < list2.Count - 1; n++)
                    {
                        int end = list2[n].Item2 + list2[n].Item3;
                        int next = list2[n + 1].Item2;
                        if (next > end)
                        {
                            string between = normalized.Substring(end, next - end);
                            if (between.Contains("와", StringComparison.Ordinal) ||
                                between.Contains("과", StringComparison.Ordinal) ||
                                between.Contains("및", StringComparison.Ordinal))
                            { flag2 = true; break; }
                        }
                    }
                    if (!flag2)
                    {
                        var best = list2.OrderByDescending(c => c.Item3).ThenBy(c => c.Item2).First();
                        list2.Clear(); list2.Add(best);
                    }
                }

                int item5 = list2[0].Item2;
                string subjectRaw = "";
                int num15 = normalized.LastIndexOf("의", item5, item5 - num3, StringComparison.Ordinal);
                if (num15 >= num3) subjectRaw = normalized.Substring(num3, num15 - num3);
                else if (item5 > num3) subjectRaw = normalized.Substring(num3, item5 - num3);

                int delta = num7 * num8;
                foreach (var (stat, _, _) in list2)
                    list.Add(new RankChange(subjectRaw, stat, delta));
                num3 = num6 + 4;
            }
            return list;
        }

        public static (string Subject, MultiStatMove Move)? TryParseMultiStatMove(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return null;
            foreach (var m in MultiStatMoves)
            {
                int num = FuzzyIndexOf(normalized, m.AnchorKo, 0, 1);
                if (num > 0)
                {
                    string text = normalized.Substring(0, num);
                    if (text.EndsWith("의", StringComparison.Ordinal) ||
                        text.EndsWith("는", StringComparison.Ordinal) ||
                        text.EndsWith("은", StringComparison.Ordinal))
                        text = text.Substring(0, text.Length - 1);
                    if (text.Length != 0) return (text, m);
                }
            }
            return null;
        }

        public static IReadOnlyList<StatRank> CollectStatKeywords(string normalized)
        {
            if (string.IsNullOrWhiteSpace(normalized)) return Array.Empty<StatRank>();
            bool[] array = new bool[normalized.Length];
            var hashSet = new HashSet<StatRank>();
            var list = new List<(StatRank, int)>();
            foreach (var (item, item2) in StatKeywords)
            {
                int num = 0;
                while (num < normalized.Length)
                {
                    int num2 = FuzzyIndexOf(normalized, item, num, 1);
                    if (num2 < 0) break;
                    bool flag = false;
                    for (int j = num2; j < num2 + item.Length && j < array.Length; j++)
                        if (array[j]) { flag = true; break; }
                    if (!flag && hashSet.Add(item2))
                    {
                        for (int k = num2; k < num2 + item.Length && k < array.Length; k++) array[k] = true;
                        list.Add((item2, num2));
                        break;
                    }
                    num = num2 + 1;
                }
            }
            list.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            return list.Select(f => f.Item1).ToList();
        }

        public static bool HasRankUpVerb(string normalized)
            => !string.IsNullOrWhiteSpace(normalized) && FuzzyIndexOf(normalized, "올라갔다", 0, 3) >= 0;

        public static bool HasMoveFailed(string normalized)
            => !string.IsNullOrWhiteSpace(normalized) &&
               (FuzzyIndexOf(normalized, "실패했다", 0, 1) >= 0 || FuzzyIndexOf(normalized, "통하지않았다", 0, 2) >= 0);

        public static int FuzzyIndexOf(string haystack, string needle, int startIndex, int maxJamoDistance)
        {
            if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return -1;
            if (startIndex >= haystack.Length) return -1;
            if (needle.Length > haystack.Length - startIndex) return -1;

            int num = haystack.IndexOf(needle, startIndex, StringComparison.Ordinal);
            if (num >= 0) return num;
            if (maxJamoDistance <= 0) return -1;

            for (int i = startIndex; i <= haystack.Length - needle.Length; i++)
            {
                int dist = 0;
                for (int j = 0; j < needle.Length; j++)
                {
                    dist += SyllableJamoDistance(haystack[i + j], needle[j]);
                    if (dist > maxJamoDistance) break;
                }
                if (dist <= maxJamoDistance) return i;
            }
            return -1;
        }

        internal static int SyllableJamoDistance(char a, char b)
        {
            if (a == b) return 0;
            bool aHangul = a >= '가' && a <= '힣';
            bool bHangul = b >= '가' && b <= '힣';
            if (aHangul && bHangul)
            {
                int na = a - 44032, nb = b - 44032;
                int dist = 0;
                if (na / 588 != nb / 588) dist++;
                if (na / 28 % 21 != nb / 28 % 21) dist++;
                if (na % 28 != nb % 28) dist++;
                return dist;
            }
            return 3;
        }
    }
}
