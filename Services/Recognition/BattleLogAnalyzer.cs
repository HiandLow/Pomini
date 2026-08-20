using System;
using System.Collections.Generic;
using System.Linq;
using PokemonHelper.Models;
using PokemonHelper.Services.Recognition;

namespace PokemonHelper.Services.Recognition
{
    public class BattleLogAnalyzer
    {
        public BattleLogEvent? Analyze(string logText)
        {
            if (string.IsNullOrWhiteSpace(logText))
                return null;

            // 1. 랭크업 감지
            var rankChanges = RankUpParser.Parse(logText);
            if (rankChanges.Count > 0)
            {
                var change = rankChanges.First();
                string source = DetermineSource(logText, change.SubjectRaw);
                return new BattleLogEvent
                {
                    EventType = "RankChange",
                    Source = source,
                    Name = source,
                    Description = logText,
                    Payload = new RankChangePayload
                    {
                        Stat = change.Stat.ToString(),
                        Stages = change.Delta
                    }
                };
            }

            // 2. 교체 감지 (단순 키워드 기반)
            if (logText.Contains("가방에서")) return null; // 아이템 사용 방지
            
            if (logText.Contains("배턴터치"))
            {
                return new BattleLogEvent
                {
                    EventType = "BatonPass",
                    Source = DetermineSource(logText, "배턴터치"),
                    Description = logText
                };
            }
            
            if (logText.Contains("가랏") || logText.Contains("가릿") || logText.Contains("가라") || logText.Contains("부탁해") || logText.Contains("가자"))
            {
                return new BattleLogEvent
                {
                    EventType = "Switch",
                    Source = "My",
                    Description = logText
                };
            }
            if (logText.Contains("내보냈다"))
            {
                return new BattleLogEvent
                {
                    EventType = "Switch",
                    Source = "Opponent",
                    Description = logText
                };
            }

            // 3. 상태이상 감지
            if (logText.Contains("마비했다") || logText.Contains("마비 상태가 되었다"))
                return CreateStatus("PRZ", logText);
            if (logText.Contains("화상을 입었다") || logText.Contains("화상 상태가 되었다"))
                return CreateStatus("BRN", logText);
            if (logText.Contains("독을 먹었다") || logText.Contains("맹독을 먹었다") || logText.Contains("독 상태가 되었다"))
                return CreateStatus(logText.Contains("맹독") ? "TOX" : "PSN", logText);
            if (logText.Contains("잠들었다") || logText.Contains("잠 상태가 되었다"))
                return CreateStatus("SLP", logText);
            if (logText.Contains("얼어붙었다") || logText.Contains("얼음 상태가 되었다"))
                return CreateStatus("FRZ", logText);

            // 4. 날씨 감지 (단순 키워드)
            if (logText.Contains("비가 내리기 시작했다"))
                return CreateWeather("Rain", logText);
            if (logText.Contains("햇살이 강해졌다"))
                return CreateWeather("Sun", logText);
            if (logText.Contains("모래바람이 불기 시작했다"))
                return CreateWeather("Sand", logText);
            if (logText.Contains("싸라기눈이 내리기 시작했다"))
                return CreateWeather("Snow", logText);

            return new BattleLogEvent
            {
                EventType = "Log",
                Description = logText
            };
        }

        private string DetermineSource(string fullText, string subjectRaw)
        {
            if (fullText.StartsWith("상대") || fullText.StartsWith("상대의")) return "Opponent";
            return "My";
        }

        private BattleLogEvent CreateWeather(string weather, string desc)
        {
            return new BattleLogEvent
            {
                EventType = "WeatherChange",
                Source = "Field",
                Description = desc,
                Payload = weather
            };
        }

        private BattleLogEvent CreateStatus(string status, string desc)
        {
            // 로그 앞부분으로 내/상대 구분
            string source = desc.StartsWith("상대") ? "Opponent" : "My";
            return new BattleLogEvent
            {
                EventType = "StatusChange",
                Source = source,
                Description = desc,
                Payload = status
            };
        }
    }
}
