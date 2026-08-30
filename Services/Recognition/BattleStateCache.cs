using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PokemonHelper.Models;

namespace PokemonHelper.Services.Recognition
{
    public static class BattleStateCache
    {
        public static List<string> MyPartyNames { get; private set; } = new();
        public static Dictionary<string, List<string>> MyPokemonMoves { get; private set; } = new();

        public static List<string> OpponentPartyNames { get; private set; } = new();
        public static Dictionary<string, List<string>> OpponentPredictedMoves { get; private set; } = new();
        public static Dictionary<string, List<string>> OpponentTop10Moves { get; private set; } = new();

        public static HashSet<string> AllMovesCache { get; private set; } = new();
        
        public static bool IsInitialized { get; private set; } = false;

        public static void Initialize(object? opponentPartyData)
        {
            try
            {
                LoadAllMoves();
                LoadMyParty();
                LoadOpponentParty(opponentPartyData);
                
                IsInitialized = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BattleStateCache] Error during initialization: {ex.Message}");
            }
        }

        private static void LoadAllMoves()
        {
            AllMovesCache.Clear();
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "master.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("moves", out var movesArray))
                {
                    foreach (var move in movesArray.EnumerateArray())
                    {
                        if (move.TryGetProperty("nameKo", out var nameProp))
                        {
                            string name = nameProp.GetString() ?? "";
                            if (!string.IsNullOrEmpty(name))
                                AllMovesCache.Add(name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BattleStateCache] Failed to load AllMoves: {ex.Message}");
            }
        }

        private static void LoadMyParty()
        {
            MyPartyNames.Clear();
            MyPokemonMoves.Clear();

            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCH", "users", "local", "parties.json");
            if (!File.Exists(path)) return;

            try
            {
                var json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var firstParty = doc.RootElement[0];
                    if (firstParty.TryGetProperty("Members", out var members) && members.ValueKind == JsonValueKind.Array)
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

                            if (!string.IsNullOrEmpty(name))
                            {
                                MyPartyNames.Add(name);

                                var movesList = new List<string>();
                                if (member.TryGetProperty("MovesKo", out var movesArray) && movesArray.ValueKind == JsonValueKind.Array)
                                {
                                    foreach (var mv in movesArray.EnumerateArray())
                                    {
                                        var m = mv.GetString();
                                        if (!string.IsNullOrEmpty(m)) movesList.Add(m);
                                    }
                                }
                                MyPokemonMoves[name] = movesList;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BattleStateCache] Failed to load MyParty: {ex.Message}");
            }
        }

        private static void LoadOpponentParty(object? opponentPartyData)
        {
            OpponentPartyNames.Clear();
            OpponentPredictedMoves.Clear();
            OpponentTop10Moves.Clear();

            if (opponentPartyData is not List<Dictionary<string, object>> partyData) return;

            // Load Usage Stats from move-usage.json (same source as frontend /api/usage)
            var usagePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PCH", "data-cache", "move-usage.json");
            
            // Build dexId -> moves lookup table
            var usageByDexId = new Dictionary<int, (List<string> top4, List<string> top10)>();
            
            if (File.Exists(usagePath))
            {
                try
                {
                    var json = File.ReadAllText(usagePath);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("pokemon", out var pokemonMap))
                    {
                        foreach (var entry in pokemonMap.EnumerateObject())
                        {
                            var pData = entry.Value;
                            if (!pData.TryGetProperty("dexId", out var dexIdProp)) continue;
                            int dexId = dexIdProp.GetInt32();
                            if (dexId <= 0) continue;

                            if (!pData.TryGetProperty("moves", out var movesArr)) continue;

                            var top4 = new List<string>();
                            var top10 = new List<string>();
                            int count = 0;
                            foreach (var m in movesArr.EnumerateArray())
                            {
                                if (count >= 10) break;
                                if (m.TryGetProperty("ko", out var koProp))
                                {
                                    var mName = koProp.GetString();
                                    if (!string.IsNullOrEmpty(mName))
                                    {
                                        top10.Add(mName);
                                        if (count < 4) top4.Add(mName);
                                        count++;
                                    }
                                }
                            }
                            usageByDexId[dexId] = (top4, top10);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BattleStateCache] Failed to load move-usage.json: {ex.Message}");
                }
            }

            try 
            {
                foreach (var dict in partyData)
                {
                    string name = "";
                    int dexId = 0;

                    if (dict.TryGetValue("name", out var val) && val is string s) name = s;
                    else if (dict.TryGetValue("NameKo", out var val2) && val2 is string s2) name = s2;

                    if (dict.TryGetValue("dexId", out var dVal) && dVal is int d) dexId = d;
                    else if (dict.TryGetValue("SpeciesId", out var sVal) && sVal is int sid) dexId = sid;
                    else if (dict.TryGetValue("dexId", out var dVal2) && dVal2 is JsonElement je && je.ValueKind == JsonValueKind.Number) dexId = je.GetInt32();
                    else if (dict.TryGetValue("SpeciesId", out var sVal2) && sVal2 is JsonElement je2 && je2.ValueKind == JsonValueKind.Number) dexId = je2.GetInt32();

                    if (string.IsNullOrEmpty(name) && dexId > 0 && ScreenCaptureService.PokemonNames.TryGetValue(dexId, out var resolvedName))
                    {
                        name = resolvedName;
                    }

                    if (!string.IsNullOrEmpty(name))
                    {
                        OpponentPartyNames.Add(name);

                        if (dexId > 0 && usageByDexId.TryGetValue(dexId, out var moves))
                        {
                            OpponentPredictedMoves[name] = moves.top4;
                            OpponentTop10Moves[name] = moves.top10;
                        }
                        else
                        {
                            OpponentPredictedMoves[name] = new List<string>();
                            OpponentTop10Moves[name] = new List<string>();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[BattleStateCache] Failed to process opponent party: {ex.Message}");
            }
        }
    }
}
