using System.Collections.Generic;
using PokemonHelper.Services.Recognition;

namespace PokemonHelper.Models
{
    public class BattleLogEvent
    {
        public string EventType { get; set; } = ""; // "Switch", "RankChange", "WeatherChange", "Faint", "AbilityTrigger"
        public string Source { get; set; } = ""; // "My", "Opponent" or "Field"
        public string Name { get; set; } = ""; // Pokemon name involved, or ability name
        public string Description { get; set; } = ""; // Original log text or description
        
        // Specific payload based on EventType
        public object? Payload { get; set; }
        
        public int TargetIndex { get; set; } = -1;
    }

    public class RankChangePayload
    {
        public string Stat { get; set; } = ""; // "Atk", "Def", "Spa", "Spd", "Spe", "Accuracy", "Evasion"
        public int Stages { get; set; } // +1, -1, +2, etc.
    }
}
