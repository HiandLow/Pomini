using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PokemonHelper.Services.Recognition;

public sealed class LogTextCorrector
{
	private readonly record struct Candidate(int Index, string Word, int Dist);

	private const int AmbiguityMargin = 2;

	private static readonly string[] AnchorVocab = new string[18]
	{
		"곁으로돌아간다", "능력변화를복사했다", "잠이들어건강해졌다", "풀파워로만들었다", "체력을깎아서", "배틀에끌려나왔다", "볼트체인지", "배턴터치", "흑안개", "클리어스모그",
		"배수의진", "메가진화", "내보냈다", "발밑에서이상한느낌이든다", "발밑이안개로자욱해졌다", "발밑에전기가흐르기시작했다", "발밑에풀이무성해졌다", "발밑의이상한느낌이사라졌다"
	};

	private IReadOnlyList<string> _speciesVocab = Array.Empty<string>();

	public void SetSpeciesVocabulary(IEnumerable<string> names)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
		List<string> list = new List<string>();
		foreach (string name in names)
		{
			if (!string.IsNullOrEmpty(name) && name.Length >= 3 && hashSet.Add(name))
			{
				list.Add(name);
			}
		}
		_speciesVocab = list;
	}

	public string Correct(string raw)
	{
		if (string.IsNullOrEmpty(raw))
		{
			return raw;
		}
		if (!ContainsHangul(raw))
		{
			return raw;
		}
		if (raw.IndexOf('[') >= 0 || raw.IndexOf(']') >= 0)
		{
			raw = raw.Replace("[", "").Replace("]", "");
		}
		List<Candidate> list = new List<Candidate>();
		CollectCandidates(raw, _speciesVocab, list);
		CollectCandidates(raw, AnchorVocab, list);
		if (list.Count == 0)
		{
			return raw;
		}
		List<Candidate> list2 = (from c in list
			where c.Dist > 0
			orderby c.Dist, c.Word.Length descending, c.Index
			select c).ToList();
		List<Candidate> list3 = new List<Candidate>();
		foreach (Candidate cand in list2)
		{
			if (!list3.Any((Candidate a) => Overlaps(a, cand)) && !list.Any((Candidate o) => o.Word != cand.Word && Overlaps(o, cand) && o.Dist - cand.Dist < 2))
			{
				list3.Add(cand);
			}
		}
		if (list3.Count == 0)
		{
			return raw;
		}
		StringBuilder stringBuilder = new StringBuilder(raw);
		foreach (Candidate item in list3)
		{
			for (int num = 0; num < item.Word.Length; num++)
			{
				stringBuilder[item.Index + num] = item.Word[num];
			}
		}
		return stringBuilder.ToString();
	}

	private static void CollectCandidates(string raw, IReadOnlyList<string> vocab, List<Candidate> acc)
	{
		foreach (string item in vocab)
		{
			if (item.Length < 3 || item.Length > raw.Length)
			{
				continue;
			}
			int num = raw.IndexOf(item, StringComparison.Ordinal);
			if (num >= 0)
			{
				while (num >= 0)
				{
					acc.Add(new Candidate(num, item, 0));
					num = raw.IndexOf(item, num + 1, StringComparison.Ordinal);
				}
				continue;
			}
			int num2 = BudgetFor(item.Length);
			int num3 = -1;
			int num4 = int.MaxValue;
			for (int i = 0; i + item.Length <= raw.Length; i++)
			{
				int num5 = 0;
				for (int j = 0; j < item.Length; j++)
				{
					num5 += RankUpParser.SyllableJamoDistance(raw[i + j], item[j]);
					if (num5 > num2)
					{
						break;
					}
				}
				if (num5 < num4)
				{
					num4 = num5;
					num3 = i;
				}
			}
			if (num3 >= 0 && num4 > 0 && num4 <= num2)
			{
				acc.Add(new Candidate(num3, item, num4));
			}
			CollectWildcardCandidate(raw, item, acc);
		}
	}

	private static void CollectWildcardCandidate(string raw, string word, List<Candidate> acc)
	{
		int num = -1;
		int num2 = int.MaxValue;
		for (int i = 0; i + word.Length <= raw.Length; i++)
		{
			int num3 = 0;
			bool flag = true;
			for (int j = 0; j < word.Length; j++)
			{
				char c = raw[i + j];
				if (c != word[j])
				{
					if (c >= '가' && c <= '힣')
					{
						flag = false;
						break;
					}
					num3++;
					if (num3 > word.Length - 2)
					{
						flag = false;
						break;
					}
				}
			}
			if (flag && num3 != 0 && (raw[i] == word[0] || i <= 0 || !char.IsAsciiLetterOrDigit(raw[i - 1])))
			{
				if ((raw[i + word.Length - 1] == word[word.Length - 1] || i + word.Length >= raw.Length || !char.IsAsciiLetterOrDigit(raw[i + word.Length])) && num3 < num2)
				{
					num2 = num3;
					num = i;
				}
			}
		}
		if (num >= 0)
		{
			acc.Add(new Candidate(num, word, num2));
		}
	}

	private static int BudgetFor(int len)
	{
		if (len <= 4)
		{
			if (len <= 2)
			{
				return 0;
			}
			return 1;
		}
		if (len <= 6)
		{
			return 2;
		}
		return 3;
	}

	private static bool Overlaps(Candidate a, Candidate b)
	{
		if (a.Index < b.Index + b.Word.Length)
		{
			return b.Index < a.Index + a.Word.Length;
		}
		return false;
	}

	private static bool ContainsHangul(string s)
	{
		foreach (char c in s)
		{
			if (c >= '가' && c <= '힣')
			{
				return true;
			}
		}
		return false;
	}
}
