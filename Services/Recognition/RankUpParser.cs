using System;
using System.Collections.Generic;
using System.Linq;

namespace PokemonHelper.Services.Recognition;

public static class RankUpParser
{
	public sealed record MultiStatMove(string AnchorKo, IReadOnlyList<StatRank> Stats, int Delta);

	private static readonly (string Keyword, StatRank Stat)[] StatKeywords = new(string, StatRank)[7]
	{
		("특수공격", StatRank.Spa),
		("특공", StatRank.Spa),
		("특수방어", StatRank.Spd),
		("특방", StatRank.Spd),
		("스피드", StatRank.Spe),
		("공격", StatRank.Atk),
		("방어", StatRank.Def)
	};

	internal const int MaxVerbJamoDistance = 3;

	internal const int MaxStatJamoDistance = 1;

	internal const int MaxAdverbJamoDistance = 1;

	public const int BellyDrumDelta = 12;

	public static readonly StatRank[] NoRetreatStats = new StatRank[5]
	{
		StatRank.Atk,
		StatRank.Def,
		StatRank.Spa,
		StatRank.Spd,
		StatRank.Spe
	};

	public static readonly MultiStatMove[] MultiStatMoves = new MultiStatMove[3]
	{
		new MultiStatMove("배수의진", NoRetreatStats, 1),
		new MultiStatMove("원시의힘", NoRetreatStats, 1),
		new MultiStatMove("은빛바람", NoRetreatStats, 1)
	};

	private const int HangulBase = 44032;

	private const int HangulEnd = 55203;

	private const int JungCount = 21;

	private const int JongCount = 28;

	public static IReadOnlyList<RankChange> Parse(string normalized)
	{
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return Array.Empty<RankChange>();
		}
		List<RankChange> list = new List<RankChange>();
		int num = FuzzyIndexOf(normalized, "풀파워로만들었다", 0, 3);
		if (num > 0)
		{
			int num2 = FuzzyIndexOf(normalized, "체력을깎아서", 0, 2);
			if (num2 < 0 || num2 > num)
			{
				num2 = num;
			}
			string text = normalized.Substring(0, num2);
			if (text.EndsWith("는", StringComparison.Ordinal) || text.EndsWith("은", StringComparison.Ordinal))
			{
				string text2 = text;
				text = text2.Substring(0, text2.Length - 1);
			}
			list.Add(new RankChange(text, StatRank.Atk, 12));
		}
		int num3 = 0;
		while (num3 < normalized.Length)
		{
			int num4 = FuzzyIndexOf(normalized, "올라갔다", num3, 3);
			int num5 = FuzzyIndexOf(normalized, "떨어졌다", num3, 3);
			if (num4 < 0 && num5 < 0)
			{
				break;
			}
			int num6;
			int num7;
			if (num4 < 0)
			{
				num6 = num5;
				num7 = -1;
			}
			else if (num5 < 0)
			{
				num6 = num4;
				num7 = 1;
			}
			else if (num4 < num5)
			{
				num6 = num4;
				num7 = 1;
			}
			else
			{
				num6 = num5;
				num7 = -1;
			}
			int num8 = 1;
			int num9 = Math.Max(0, num6 - 8);
			string haystack = normalized.Substring(num9, num6 - num9);
			if (FuzzyIndexOf(haystack, "매우크게", 0, 1) >= 0)
			{
				num8 = 3;
			}
			else if (FuzzyIndexOf(haystack, "크게", 0, 1) >= 0)
			{
				num8 = 2;
			}
			int num10 = Math.Max(num3, num6 - 15);
			string text3 = normalized.Substring(num10, num6 - num10);
			bool[] array = new bool[text3.Length];
			HashSet<StatRank> hashSet = new HashSet<StatRank>();
			List<(StatRank Stat, int AbsIdx, int Len)> list2 = new List<(StatRank Stat, int AbsIdx, int Len)>();
			(string, StatRank)[] statKeywords = StatKeywords;
			for (int i = 0; i < statKeywords.Length; i++)
			{
				(string, StatRank) tuple = statKeywords[i];
				string item = tuple.Item1;
				StatRank item2 = tuple.Item2;
				int num11 = 0;
				while (num11 < text3.Length)
				{
					int num12 = FuzzyIndexOf(text3, item, num11, 1);
					if (num12 < 0)
					{
						break;
					}
					bool flag = false;
					for (int j = num12; j < num12 + item.Length; j++)
					{
						if (array[j])
						{
							flag = true;
							break;
						}
					}
					if (!flag && hashSet.Add(item2))
					{
						for (int k = num12; k < num12 + item.Length; k++)
						{
							array[k] = true;
						}
						list2.Add((item2, num10 + num12, item.Length));
						break;
					}
					num11 = num12 + 1;
				}
			}
			if (list2.Count == 0)
			{
				num3 = num6 + 4;
				continue;
			}
			list2.Sort(((StatRank Stat, int AbsIdx, int Len) a, (StatRank Stat, int AbsIdx, int Len) b) => a.AbsIdx.CompareTo(b.AbsIdx));
			if (list2.Count >= 2)
			{
				bool flag2 = false;
				for (int num13 = 0; num13 < list2.Count - 1; num13++)
				{
					int num14 = list2[num13].Item2 + list2[num13].Item3;
					int item3 = list2[num13 + 1].Item2;
					if (item3 > num14)
					{
						string text4 = normalized.Substring(num14, item3 - num14);
						if (text4.Contains("와", StringComparison.Ordinal) || text4.Contains("과", StringComparison.Ordinal) || text4.Contains("및", StringComparison.Ordinal))
						{
							flag2 = true;
							break;
						}
					}
				}
				if (!flag2)
				{
					(StatRank Stat, int AbsIdx, int Len) item4 = (from c in list2
						orderby c.Len descending, c.AbsIdx
						select c).First();
					list2.Clear();
					list2.Add(item4);
				}
			}
			int item5 = list2[0].Item2;
			string subjectRaw = "";
			int num15 = normalized.LastIndexOf("의", item5, item5 - num3, StringComparison.Ordinal);
			if (num15 >= num3)
			{
				subjectRaw = normalized.Substring(num3, num15 - num3);
			}
			else if (item5 > num3)
			{
				subjectRaw = normalized.Substring(num3, item5 - num3);
			}
			int delta = num7 * num8;
			foreach (var item7 in list2)
			{
				StatRank item6 = item7.Item1;
				list.Add(new RankChange(subjectRaw, item6, delta));
			}
			num3 = num6 + 4;
		}
		return list;
	}

	public static (string Subject, MultiStatMove Move)? TryParseMultiStatMove(string normalized)
	{
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return null;
		}
		MultiStatMove[] multiStatMoves = MultiStatMoves;
		foreach (MultiStatMove multiStatMove in multiStatMoves)
		{
			int num = FuzzyIndexOf(normalized, multiStatMove.AnchorKo, 0, 1);
			if (num > 0)
			{
				string text = normalized.Substring(0, num);
				if (text.EndsWith("의", StringComparison.Ordinal) || text.EndsWith("는", StringComparison.Ordinal) || text.EndsWith("은", StringComparison.Ordinal))
				{
					string text2 = text;
					text = text2.Substring(0, text2.Length - 1);
				}
				if (text.Length != 0)
				{
					return (text, multiStatMove);
				}
			}
		}
		return null;
	}

	public static IReadOnlyList<StatRank> CollectStatKeywords(string normalized)
	{
		if (string.IsNullOrWhiteSpace(normalized))
		{
			return Array.Empty<StatRank>();
		}
		bool[] array = new bool[normalized.Length];
		HashSet<StatRank> hashSet = new HashSet<StatRank>();
		List<(StatRank, int)> list = new List<(StatRank, int)>();
		(string, StatRank)[] statKeywords = StatKeywords;
		for (int i = 0; i < statKeywords.Length; i++)
		{
			(string, StatRank) tuple = statKeywords[i];
			string item = tuple.Item1;
			StatRank item2 = tuple.Item2;
			int num = 0;
			while (num < normalized.Length)
			{
				int num2 = FuzzyIndexOf(normalized, item, num, 1);
				if (num2 < 0)
				{
					break;
				}
				bool flag = false;
				for (int j = num2; j < num2 + item.Length && j < array.Length; j++)
				{
					if (array[j])
					{
						flag = true;
						break;
					}
				}
				if (!flag && hashSet.Add(item2))
				{
					for (int k = num2; k < num2 + item.Length && k < array.Length; k++)
					{
						array[k] = true;
					}
					list.Add((item2, num2));
					break;
				}
				num = num2 + 1;
			}
		}
		list.Sort(((StatRank Stat, int Idx) a, (StatRank Stat, int Idx) b) => a.Idx.CompareTo(b.Idx));
		return list.Select<(StatRank, int), StatRank>(((StatRank Stat, int Idx) f) => f.Stat).ToList();
	}

	public static string? TryParseNoRetreatSubject(string normalized)
	{
		(string, MultiStatMove)? tuple = TryParseMultiStatMove(normalized);
		if (tuple.HasValue)
		{
			(string, MultiStatMove) valueOrDefault = tuple.GetValueOrDefault();
			if (valueOrDefault.Item2.AnchorKo == "배수의진")
			{
				return valueOrDefault.Item1;
			}
		}
		return null;
	}

	public static bool HasRankUpVerb(string normalized)
	{
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			return FuzzyIndexOf(normalized, "올라갔다", 0, 3) >= 0;
		}
		return false;
	}

	public static bool HasMoveFailed(string normalized)
	{
		if (!string.IsNullOrWhiteSpace(normalized))
		{
			if (FuzzyIndexOf(normalized, "실패했다", 0, 1) < 0)
			{
				return FuzzyIndexOf(normalized, "통하지않았다", 0, 2) >= 0;
			}
			return true;
		}
		return false;
	}

	public static int FuzzyIndexOf(string haystack, string needle, int startIndex, int maxJamoDistance)
	{
		if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle))
		{
			return -1;
		}
		if (startIndex >= haystack.Length)
		{
			return -1;
		}
		if (needle.Length > haystack.Length - startIndex)
		{
			return -1;
		}
		int num = haystack.IndexOf(needle, startIndex, StringComparison.Ordinal);
		if (num >= 0)
		{
			return num;
		}
		if (maxJamoDistance <= 0)
		{
			return -1;
		}
		for (int i = startIndex; i <= haystack.Length - needle.Length; i++)
		{
			int num2 = 0;
			for (int j = 0; j < needle.Length; j++)
			{
				num2 += SyllableJamoDistance(haystack[i + j], needle[j]);
				if (num2 > maxJamoDistance)
				{
					break;
				}
			}
			if (num2 <= maxJamoDistance)
			{
				return i;
			}
		}
		return -1;
	}

	internal static int SyllableJamoDistance(char a, char b)
	{
		if (a == b)
		{
			return 0;
		}
		bool num = a >= '가' && a <= '힣';
		bool flag = b >= '가' && b <= '힣';
		if (num && flag)
		{
			int num2 = a - 44032;
			int num3 = b - 44032;
			int num4 = num2 / 588;
			int num5 = num3 / 588;
			int num6 = num2 / 28 % 21;
			int num7 = num3 / 28 % 21;
			int num8 = num2 % 28;
			int num9 = num3 % 28;
			int num10 = 0;
			if (num4 != num5)
			{
				num10++;
			}
			if (num6 != num7)
			{
				num10++;
			}
			if (num8 != num9)
			{
				num10++;
			}
			return num10;
		}
		return 3;
	}
}
