using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokemonHelper.Services.Recognition;

public sealed class JsonSpritesProvider : ISpritesProvider
{
	private sealed record Snapshot(IReadOnlyList<SpriteRecord> All, IReadOnlyDictionary<int, IReadOnlyList<SpriteRecord>> ByDex, string? SpriteDir)
	{
		public static readonly Snapshot Empty = new Snapshot(Array.Empty<SpriteRecord>(), new Dictionary<int, IReadOnlyList<SpriteRecord>>(), null);
	}

	private sealed class SpritesFile
	{
		[JsonPropertyName("sprites")]
		public List<SpriteDto>? Sprites { get; set; }
	}

	private sealed class SpriteDto
	{
		public int DexId { get; set; }

		public string? FormKey { get; set; }

		public string? NameKo { get; set; }

		public string? DisplayName { get; set; }

		public string? IconFile { get; set; }

		public string? IconUrl { get; set; }

		public string? DHash { get; set; }
	}

	private readonly Lazy<Snapshot> _snapshot;

	private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		PropertyNameCaseInsensitive = true
	};

	public IReadOnlyList<SpriteRecord> AllSprites => _snapshot.Value.All;

	public string? SpriteDirectory => _snapshot.Value.SpriteDir;

	public JsonSpritesProvider(string? overridePath = null)
	{
		_snapshot = new Lazy<Snapshot>(() => LoadSnapshot(overridePath));
	}

	public IEnumerable<SpriteRecord> FindByDex(int dexId)
	{
		if (!_snapshot.Value.ByDex.TryGetValue(dexId, out IReadOnlyList<SpriteRecord> value))
		{
			return Array.Empty<SpriteRecord>();
		}
		return value;
	}

	private static Snapshot LoadSnapshot(string? overridePath)
	{
		string text = overridePath ?? ResolveDefaultPath();
		if (text == null || !File.Exists(text))
		{
			return Snapshot.Empty;
		}
		try
		{
			SpritesFile spritesFile = JsonSerializer.Deserialize<SpritesFile>(File.ReadAllText(text), JsonOpts);
			if (spritesFile?.Sprites == null)
			{
				return Snapshot.Empty;
			}
			List<SpriteRecord> list = new List<SpriteRecord>(spritesFile.Sprites.Count);
			foreach (SpriteDto sprite in spritesFile.Sprites)
			{
				if (!string.IsNullOrWhiteSpace(sprite.DHash) && ulong.TryParse(sprite.DHash, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var result))
				{
					list.Add(new SpriteRecord(sprite.DexId, sprite.FormKey ?? "default", sprite.NameKo ?? string.Empty, sprite.DisplayName ?? sprite.NameKo ?? string.Empty, sprite.IconFile ?? string.Empty, sprite.IconUrl ?? string.Empty, result, sprite.DHash));
				}
			}
			Dictionary<int, IReadOnlyList<SpriteRecord>> byDex = (from s in list
				group s by s.DexId).ToDictionary((Func<IGrouping<int, SpriteRecord>, int>)((IGrouping<int, SpriteRecord> g) => g.Key), (Func<IGrouping<int, SpriteRecord>, IReadOnlyList<SpriteRecord>>)((IGrouping<int, SpriteRecord> g) => g.ToList()));
			string directoryName = Path.GetDirectoryName(text);
			string text2 = ((directoryName == null) ? null : Path.Combine(directoryName, "sprites"));
			if (text2 != null && !Directory.Exists(text2))
			{
				text2 = null;
			}
			return new Snapshot(list, byDex, text2);
		}
		catch
		{
			return Snapshot.Empty;
		}
	}

	private static string? ResolveDefaultPath()
	{
		string baseDirectory = AppContext.BaseDirectory;
		string text = Path.Combine(baseDirectory, "sprites.json");
		if (File.Exists(text))
		{
			return text;
		}
		DirectoryInfo directoryInfo = new DirectoryInfo(baseDirectory);
		int num = 0;
		while (num < 8 && directoryInfo != null)
		{
			InlineArray5<string> buffer = default(InlineArray5<string>);
			buffer[0] = directoryInfo.FullName;
			buffer[1] = "tools";
			buffer[2] = "PCH.DataBuilder";
			buffer[3] = "output";
			buffer[4] = "sprites.json";
			string text2 = Path.Combine(buffer);
			if (File.Exists(text2))
			{
				return text2;
			}
			num++;
			directoryInfo = directoryInfo.Parent;
		}
		return null;
	}
}
