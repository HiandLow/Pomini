using System;
using System.Linq;
using System.Text;

namespace PokemonHelper.Utils
{
    public static class HangulDistance
    {
        public static string KeepOnlyHangul(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if ((c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x3131 && c <= 0x318E))
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }

        public static int Compute(string a, string b)
        {
            if (a.Length != b.Length) return int.MaxValue;
            int distance = 0;
            for (int i = 0; i < a.Length; i++)
            {
                distance += ComputeJamoDistance(a[i], b[i]);
            }
            return distance;
        }

        public static int ComputeJamoDistance(char c1, char c2)
        {
            if (c1 == c2) return 0;

            bool isHangul1 = c1 >= 0xAC00 && c1 <= 0xD7A3;
            bool isHangul2 = c2 >= 0xAC00 && c2 <= 0xD7A3;

            if (isHangul1 && isHangul2)
            {
                int code1 = c1 - 0xAC00;
                int code2 = c2 - 0xAC00;

                int cho1 = code1 / (21 * 28);
                int jung1 = (code1 % (21 * 28)) / 28;
                int jong1 = code1 % 28;

                int cho2 = code2 / (21 * 28);
                int jung2 = (code2 % (21 * 28)) / 28;
                int jong2 = code2 % 28;

                int dist = 0;
                if (cho1 != cho2) dist++;
                if (jung1 != jung2) dist++;
                if (jong1 != jong2) dist++;
                return dist;
            }
            return 3;
        }

        public static int ComputeWithLengthTolerance(string s1, string s2, int maxLenDiff)
        {
            if (Math.Abs(s1.Length - s2.Length) > maxLenDiff)
            {
                return int.MaxValue;
            }

            int len1 = s1.Length;
            int len2 = s2.Length;
            int[,] d = new int[len1 + 1, len2 + 1];

            for (int i = 0; i <= len1; i++) d[i, 0] = i * 3;
            for (int j = 0; j <= len2; j++) d[0, j] = j * 3;

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    int cost = ComputeJamoDistance(s1[i - 1], s2[j - 1]);
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 3, d[i, j - 1] + 3),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[len1, len2];
        }
    }
}
