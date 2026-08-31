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
        private string? _activeMyPokemon = null;
        private string? _activeOpponentPokemon = null;

        public void Reset()
        {
            _pendingMyMegaForm = null;
            _pendingOpponentMegaForm = null;
            _activeMyPokemon = null;
            _activeOpponentPokemon = null;
        }

        public BattleLogEvent? Analyze(string logText)
        {
            if (string.IsNullOrWhiteSpace(logText))
                return null;
                
            logText = logText.Replace(" ", "");

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
            

            
            if (logText.Contains("가랏") || logText.Contains("가릿") || logText.Contains("가라") || logText.Contains("부탁해") || logText.Contains("가자"))
            {
                int idx = MatchSwitchIndex(logText, "My");
                if (idx >= 0 && idx < BattleStateCache.MyPartyNames.Count)
                    _activeMyPokemon = BattleStateCache.MyPartyNames[idx];
                return new BattleLogEvent
                {
                    EventType = "Switch",
                    Source = "My",
                    Name = _activeMyPokemon ?? "",
                    Description = logText,
                    TargetIndex = idx
                };
            }
            if (logText.Contains("내보냈다"))
            {
                int idx = MatchSwitchIndex(logText, "Opponent");
                if (idx >= 0 && idx < BattleStateCache.OpponentPartyNames.Count)
                    _activeOpponentPokemon = BattleStateCache.OpponentPartyNames[idx];
                return new BattleLogEvent
                {
                    EventType = "Switch",
                    Source = "Opponent",
                    Name = _activeOpponentPokemon ?? "",
                    Description = logText,
                    TargetIndex = idx
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

            if (logText.Contains("효과가굉장했다") || logText.Contains("효과가별로인듯하다") || logText.Contains("효과가없는것같다") || logText.Contains("효과가경장했다") || logText.Contains("효과가핑장했다") || logText.Contains("효과가링장했다") || logText.Contains("효과가징장했다"))
            {
                return new BattleLogEvent { EventType = "Effectiveness", Source = DetermineSource(logText, ""), Description = logText };
            }

            if (logText.Contains("급소에맞았다"))
            {
                return new BattleLogEvent { EventType = "CriticalHit", Source = DetermineSource(logText, ""), Description = logText };
            }

            if (logText.Contains("빗나갔다") || logText.Contains("피했다"))
            {
                return new BattleLogEvent { EventType = "Miss", Source = DetermineSource(logText, ""), Description = logText };
            }
            if (logText.Contains("실패하고말았다") || logText.Contains("잘통하지않았다"))
            {
                return new BattleLogEvent { EventType = "Fail", Source = DetermineSource(logText, ""), Description = logText };
            }

            if (logText.Contains("쓰러졌다"))
            {
                return new BattleLogEvent { EventType = "Faint", Source = DetermineSource(logText, ""), Description = logText };
            }

            
            bool hasExclamation = logText.EndsWith("!") || logText.EndsWith("1") || logText.EndsWith("l") || logText.EndsWith("I") || logText.EndsWith("|") || logText.EndsWith("i");
            if (hasExclamation && !logText.Contains("효과가") && !logText.Contains("올라갔다") && !logText.Contains("떨어졌다") && !logText.Contains("내보냈다") && !logText.Contains("들어갔다") && !logText.Contains("넣어버렸다") && !logText.Contains("메가진화") && !logText.Contains("급소에") && !logText.Contains("쓰러졌다") && !logText.Contains("모습이"))
            {
                string source = DetermineSource(logText, "");
                string? detectedMove = null;
                
                // 이름 접두사에 의한 오인식 방지 (예: 상대대도각참의아이언헤드 -> 대도각참 매칭 방지)
                string searchTarget = logText;
                if (source == "My" && !string.IsNullOrEmpty(_activeMyPokemon))
                {
                    searchTarget = searchTarget.Replace($"{_activeMyPokemon}의", "");
                }
                else if (source == "Opponent" && !string.IsNullOrEmpty(_activeOpponentPokemon))
                {
                    searchTarget = searchTarget.Replace($"상대{_activeOpponentPokemon}의", "")
                                               .Replace($"{_activeOpponentPokemon}의", "");
                }

                if (source == "My")
                {
                    // 현재 필드 포켓몬의 기술 4개에서만 검색
                    if (_activeMyPokemon != null &&
                        BattleStateCache.MyPokemonMoves.TryGetValue(_activeMyPokemon, out var myMoves))
                    {
                        foreach (var m in myMoves)
                        {
                            if (RankUpParser.FuzzyIndexOf(searchTarget, m, 0, 1) >= 0)
                            {
                                detectedMove = m;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    // 상대 포켓몬: 현재 필드 포켓몬 기준 cascading 검색
                    if (_activeOpponentPokemon != null)
                    {
                        // 1차: 확정/예측 슬롯 (Top 4)
                        if (detectedMove == null && BattleStateCache.OpponentPredictedMoves.TryGetValue(_activeOpponentPokemon, out var top4))
                        {
                            foreach (var m in top4)
                                if (RankUpParser.FuzzyIndexOf(searchTarget, m, 0, 1) >= 0) { detectedMove = m; break; }
                        }
                        // 2차: Top 10
                        if (detectedMove == null && BattleStateCache.OpponentTop10Moves.TryGetValue(_activeOpponentPokemon, out var top10))
                        {
                            foreach (var m in top10)
                                if (RankUpParser.FuzzyIndexOf(searchTarget, m, 0, 1) >= 0) { detectedMove = m; break; }
                        }
                    }
                    // 3차: 전체 기술 풀 (포켓몬 특정 불가 or 위에서 못 찾은 경우)
                    if (detectedMove == null)
                    {
                        foreach (var m in BattleStateCache.AllMovesCache)
                        {
                            if (m.Length >= 2 && searchTarget.Contains(m))
                            {
                                detectedMove = m;
                                break;
                            }
                        }
                    }
                }

                if (detectedMove != null)
                {
                    return new BattleLogEvent
                    {
                        EventType = "MoveUse",
                        Source = source,
                        Name = source == "My" ? (_activeMyPokemon ?? "") : (_activeOpponentPokemon ?? ""),
                        Description = logText,
                        Payload = detectedMove
                    };
                }
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
            var names = source == "My" ? BattleStateCache.MyPartyNames : BattleStateCache.OpponentPartyNames;
            if (names == null || names.Count == 0) return -1;

            int bestMatchCount = -1;
            int bestLength = -1;
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
                if (description.Contains(n)) matchCount += 100; // 완전 일치 시 압도적 가산점 부여

                if (matchCount > bestMatchCount || (matchCount == bestMatchCount && n.Length > bestLength))
                {
                    bestMatchCount = matchCount;
                    bestLength = n.Length;
                    targetIndex = i;
                }
            }

            if (bestMatchCount < 2) return -1; // 최소 2글자 이상 매칭되어야 인정 (혹은 Contains 보너스)

            return targetIndex;
        }

        
    }
}
