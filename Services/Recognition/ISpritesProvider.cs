using System.Collections.Generic;

namespace PokemonHelper.Services.Recognition;

public interface ISpritesProvider
{
	IReadOnlyList<SpriteRecord> AllSprites { get; }

	string? SpriteDirectory { get; }

	IEnumerable<SpriteRecord> FindByDex(int dexId);
}
