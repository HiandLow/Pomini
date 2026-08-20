using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PokemonHelper.Models
{
    public class UsageDataRoot
    {
        [JsonPropertyName("formats")]
        public FormatsData Formats { get; set; } = new();
    }

    public class FormatsData
    {
        [JsonPropertyName("single")]
        public List<UsagePokemon> Single { get; set; } = new();
    }

    public class UsagePokemon
    {
        [JsonPropertyName("rank")]
        public int Rank { get; set; }

        [JsonPropertyName("nameKo")]
        public string NameKo { get; set; } = "";

        [JsonPropertyName("moves")]
        public List<UsageMove> Moves { get; set; } = new();
    }

    public class UsageMove
    {
        [JsonPropertyName("ko")]
        public string NameKo { get; set; } = "";

        [JsonPropertyName("pct")]
        public double Percentage { get; set; }
    }
}
