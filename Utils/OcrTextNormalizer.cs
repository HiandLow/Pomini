using System.Text;

namespace PokemonHelper.Utils
{
    public static class OcrTextNormalizer
    {
        public static string Normalize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }
            StringBuilder stringBuilder = new StringBuilder(raw.Length);
            foreach (char c in raw)
            {
                if (!char.IsWhiteSpace(c))
                {
                    stringBuilder.Append(c);
                }
            }
            return stringBuilder.ToString();
        }
    }
}
