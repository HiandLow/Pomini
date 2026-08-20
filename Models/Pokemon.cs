using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace PokemonHelper.Models
{
    public class MasterData
    {
        [JsonPropertyName("species")]
        public List<Pokemon> Species { get; set; } = new();
    }

    public class Pokemon
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("nameKo")]
        public string NameKo { get; set; } = "";

        [JsonPropertyName("nameEn")]
        public string NameEn { get; set; } = "";

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = new();

        [JsonPropertyName("baseStats")]
        public BaseStats BaseStats { get; set; } = new();
    }

    public class BaseStats
    {
        [JsonPropertyName("hp")]
        public int Hp { get; set; }
        [JsonPropertyName("atk")]
        public int Atk { get; set; }
        [JsonPropertyName("def")]
        public int Def { get; set; }
        [JsonPropertyName("spa")]
        public int Spa { get; set; }
        [JsonPropertyName("spd")]
        public int Spd { get; set; }
        [JsonPropertyName("spe")]
        public int Spe { get; set; }
    }
}
