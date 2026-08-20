using System;
using System.Collections.Generic;
using System.Linq;
using PokemonHelper.Models;
using PokemonHelper.Utils;

namespace PokemonHelper.Services
{
    public static class MatchupResolver
    {
        public static Pokemon? FindBestMatch(string raw, IReadOnlyList<Pokemon> candidates, int maxJamoDistance = 3, int maxLenDiff = 1)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            if (candidates.Count == 0) return null;

            string text = HangulDistance.KeepOnlyHangul(raw);
            if (text.Length == 0 || text.Length == 1) return null;

            int count = candidates.Count;
            string[] array = new string[count];
            string?[] array2 = new string?[count];
            for (int i = 0; i < count; i++)
            {
                array[i] = HangulDistance.KeepOnlyHangul(candidates[i].NameKo);
                string? text2 = FormBaseName(candidates[i].NameKo);
                array2[i] = (text2 != null && text2.Length >= 2) ? text2 : null;
            }

            bool[] array3 = new bool[count];
            for (int j = 0; j < count; j++)
            {
                if (array2[j] == null) continue;
                for (int k = 0; k < count; k++)
                {
                    if (k != j && (array2[j] == array2[k] || array2[j] == array[k]))
                    {
                        array3[j] = true;
                        break;
                    }
                }
            }

            for (int l = 0; l < count; l++)
            {
                if (array3[l]) array2[l] = null;
            }

            // Exact
            for (int m = 0; m < count; m++)
            {
                if (candidates[m].NameKo == text || array[m] == text || array2[m] == text)
                {
                    return candidates[m];
                }
            }

            // Contains
            Pokemon? species = null;
            int num = 0;
            for (int n = 0; n < count; n++)
            {
                string? text3 = null;
                if (array[n].Length > 0 && text.Contains(array[n], StringComparison.Ordinal))
                    text3 = array[n];
                else
                {
                    string? text4 = array2[n];
                    if (text4 != null && text.Contains(text4, StringComparison.Ordinal))
                        text3 = text4;
                }

                if (text3 != null && text3.Length > num)
                {
                    species = candidates[n];
                    num = text3.Length;
                }
            }
            if (species != null) return species;

            // Prefix
            Pokemon? result = null;
            int num2 = 0;
            for (int num3 = 0; num3 < count; num3++)
            {
                if (array[num3].Length <= text.Length || !array[num3].StartsWith(text, StringComparison.Ordinal))
                {
                    string? text5 = array2[num3];
                    if (text5 == null || text5.Length <= text.Length || !text5.StartsWith(text, StringComparison.Ordinal))
                        continue;
                }
                result = candidates[num3];
                if (++num2 >= 2) break;
            }
            if (num2 == 1) return result;

            // Suffix
            Pokemon? result2 = null;
            int num4 = 0;
            for (int num5 = 0; num5 < count; num5++)
            {
                if (array[num5].Length <= text.Length || !array[num5].EndsWith(text, StringComparison.Ordinal))
                {
                    string? text6 = array2[num5];
                    if (text6 == null || text6.Length <= text.Length || !text6.EndsWith(text, StringComparison.Ordinal))
                        continue;
                }
                result2 = candidates[num5];
                if (++num4 >= 2) break;
            }
            if (num4 == 1) return result2;

            // Fuzzy
            Pokemon? species2 = null;
            int num6 = int.MaxValue;
            int num7 = EffectiveJamoBudget(text.Length, maxJamoDistance);
            for (int num8 = 0; num8 < count; num8++)
            {
                int num9 = int.MaxValue;
                if (array[num8].Length == text.Length)
                    num9 = HangulDistance.Compute(text, array[num8]);

                string? text7 = array2[num8];
                if (text7 != null && text7.Length == text.Length)
                    num9 = Math.Min(num9, HangulDistance.Compute(text, text7));

                if (num9 <= num7 && num9 < num6)
                {
                    species2 = candidates[num8];
                    num6 = num9;
                }
            }
            if (species2 != null) return species2;

            // Len-tol
            for (int num10 = 0; num10 < count; num10++)
            {
                if (array[num10].Length > 0 && array[num10].Length != text.Length)
                {
                    int num11 = HangulDistance.ComputeWithLengthTolerance(text, array[num10], maxLenDiff);
                    if (num11 <= num7 && num11 < num6)
                    {
                        species2 = candidates[num10];
                        num6 = num11;
                    }
                }

                string? text8 = array2[num10];
                if (text8 != null && text8.Length != text.Length)
                {
                    int num12 = HangulDistance.ComputeWithLengthTolerance(text, text8, maxLenDiff);
                    if (num12 <= num7 && num12 < num6)
                    {
                        species2 = candidates[num10];
                        num6 = num12;
                    }
                }
            }

            return species2;
        }

        private static string? FormBaseName(string? nameKo)
        {
            if (string.IsNullOrEmpty(nameKo)) return null;
            int num = nameKo.IndexOf('(');
            if (num <= 0) return null;
            return nameKo.Substring(0, num);
        }

        private static int EffectiveJamoBudget(int keyLength, int maxJamoDistance)
        {
            if (keyLength > 2)
            {
                if (keyLength == 3) return Math.Min(maxJamoDistance, 2);
                return maxJamoDistance;
            }
            return Math.Min(maxJamoDistance, 1);
        }
    }
}
