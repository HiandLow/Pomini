using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using PokemonHelper.Models;
using PokemonHelper.Services.Recognition;

namespace PokemonHelper.Services.Recognition
{
    public class BattleLogAnalyzer
    {
        private string? _pendingMyMegaForm = null;
        private string? _pendingOpponentMegaForm = null;

        public void Reset()
        {
            _pendingMyMegaForm = null;
            _pendingOpponentMegaForm = null;
        }

        public BattleLogEvent? Analyze(string logText)
        {
            if (string.IsNullOrWhiteSpace(logText))
                return null;

            // 0. 메가링/나이트 반응 감지
            if (logText.Contains("나이트") && (logText.Contains("반응했다") || logText.Contains("모두링") || logText.Contains("메가링")))
            {
                string source = DetermineSource(logText, "나이트");
                string form = "Normal";
                if (logText.Contains("나이트X")) form = "X";
                else if (logText.Contains("나이트Y")) form = "Y";
                
                if (source == "My") _pendingMyMegaForm = form;
                else _pendingOpponentMegaForm = form;
                
                return null; // 아직 메가진화 이벤트는 발생시키지 않음
            }

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
                    Description = logText,
                    TargetIndex = MatchSwitchIndex(logText, "My")
                };
            }
            if (logText.Contains("내보냈다"))
            {
                return new BattleLogEvent
                {
                    EventType = "Switch",
                    Source = "Opponent",
                    Description = logText,
                    TargetIndex = MatchSwitchIndex(logText, "Opponent")
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

            // 5. 메가진화 감지 (OCR 오타 보정을 위해 FuzzyIndexOf 사용)
            if (RankUpParser.FuzzyIndexOf(logText, "메가진화", 0, 2) >= 0)
            {
                string source = DetermineSource(logText, "메가진화");
                string form = "Normal";
                
                if (logText.Contains("메가리자몽X") || logText.Contains("뮤츠X")) form = "X";
                else if (logText.Contains("메가리자몽Y") || logText.Contains("뮤츠Y")) form = "Y";
                else
                {
                    if (source == "My" && _pendingMyMegaForm != null)
                    {
                        form = _pendingMyMegaForm;
                        _pendingMyMegaForm = null;
                    }
                    else if (source == "Opponent" && _pendingOpponentMegaForm != null)
                    {
                        form = _pendingOpponentMegaForm;
                        _pendingOpponentMegaForm = null;
                    }
                }

                return new BattleLogEvent
                {
                    EventType = "MegaEvolution",
                    Source = source,
                    Description = logText,
                    Payload = form
                };
            }

            return null;
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
            string source = desc.StartsWith("상대") ? "Opponent" : "My";
            return new BattleLogEvent
            {
                EventType = "StatusChange",
                Source = source,
                Description = desc,
                Payload = status
            };
        }

        private int MatchSwitchIndex(string description, string source)
        {
            var names = source == "My" ? GetMyPartyNames() : GetOpponentPartyNames();
            if (names == null || names.Count == 0) return -1;

            int bestMatchCount = 1; // 최소 2글자 이상 일치
            int targetIndex = -1;

            for (int i = 0; i < names.Count; i++)
            {
                string n = names[i];
                if (string.IsNullOrWhiteSpace(n)) continue;

                int matchCount = 0;
                foreach (char c in n)
                {
                    if (description.Contains(c)) matchCount++;
                }
                if (description.Contains(n)) matchCount += 10; // 완전 일치 가산점

                if (matchCount > bestMatchCount)
                {
                    bestMatchCount = matchCount;
                    targetIndex = i;
                }
            }

            return targetIndex;
        }

        private List<string> GetMyPartyNames()
        {
            var names = new List<string>();
            try
            {
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCH", "users", "local", "parties.json");
                if (System.IO.File.Exists(path))
                {
                    var json = System.IO.File.ReadAllText(path);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                    {
                        var firstParty = doc.RootElement[0];
                        if (firstParty.TryGetProperty("Members", out var members) && members.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var member in members.EnumerateArray())
                            {
                                string name = "";
                                if (member.TryGetProperty("NameKo", out var nameProp)) name = nameProp.GetString() ?? "";
                                if (string.IsNullOrEmpty(name))
                                {
                                    int dexId = 0;
                                    if (member.TryGetProperty("SpeciesId", out var speciesIdProp)) dexId = speciesIdProp.GetInt32();
                                    else if (member.TryGetProperty("dexId", out var dexIdProp)) dexId = dexIdProp.GetInt32();
                                    
                                    if (dexId > 0 && ScreenCaptureService.PokemonNames.TryGetValue(dexId, out var resolvedName))
                                    {
                                        name = resolvedName;
                                    }
                                }
                                names.Add(name);
                            }
                        }
                    }
                }
            }
            catch { }
            return names;
        }

        private List<string> GetOpponentPartyNames()
        {
            var names = new List<string>();
            try
            {
                if (ScreenCaptureService.LastPartyData is List<Dictionary<string, object>> partyData)
                {
                    foreach (var dict in partyData)
                    {
                        string name = "";
                        if (dict.TryGetValue("name", out var val) && val is string s) name = s;
                        else if (dict.TryGetValue("NameKo", out var val2) && val2 is string s2) name = s2;
                        
                        if (string.IsNullOrEmpty(name))
                        {
                            int dexId = 0;
                            if (dict.TryGetValue("dexId", out var dVal) && dVal is int d) dexId = d;
                            else if (dict.TryGetValue("SpeciesId", out var sVal) && sVal is int sid) dexId = sid;
                            else if (dict.TryGetValue("dexId", out var dVal2) && dVal2 is JsonElement je && je.ValueKind == JsonValueKind.Number) dexId = je.GetInt32();
                            else if (dict.TryGetValue("SpeciesId", out var sVal2) && sVal2 is JsonElement je2 && je2.ValueKind == JsonValueKind.Number) dexId = je2.GetInt32();
                            
                            if (dexId > 0 && ScreenCaptureService.PokemonNames.TryGetValue(dexId, out var resolvedName))
                            {
                                name = resolvedName;
                            }
                        }
                        names.Add(name);
                    }
                }
            }
            catch { }
            return names;
        }
    }
}
