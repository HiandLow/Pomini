using System.Collections.Generic;

namespace PokemonHelper.Services.Recognition;

public sealed record OpponentPartySlot(int? DexId, string FormKey, string DisplayName, double Score, IReadOnlyList<TypeMatch> DetectedTypes, int Tries);
