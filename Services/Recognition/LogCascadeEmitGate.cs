using System;
using System.Collections.Generic;

namespace PokemonHelper.Services.Recognition;

public sealed class LogCascadeEmitGate
{
	private static readonly string[] Empty = Array.Empty<string>();

	private readonly object _sync = new object();

	private readonly List<string> _held = new List<string>();

	private int _gen;

	private bool _holding;

	private long _holdStartMs;

	public int HoldTimeoutMs { get; set; } = 3000;

	public IReadOnlyList<string> Submit(string raw, long nowMs)
	{
		lock (_sync)
		{
			if (!_holding)
			{
				return new string[1] { raw };
			}
			if (nowMs - _holdStartMs >= HoldTimeoutMs)
			{
				_holding = false;
				List<string> list = new List<string>(_held.Count + 1);
				list.AddRange(_held);
				list.Add(raw);
				_held.Clear();
				return list;
			}
			_held.Add(raw);
			return Empty;
		}
	}

	public int BeginHold(long nowMs)
	{
		lock (_sync)
		{
			_gen++;
			_holding = true;
			_holdStartMs = nowMs;
			return _gen;
		}
	}

	public IReadOnlyList<string> Complete(int gen, string? adopted)
	{
		lock (_sync)
		{
			List<string> list = null;
			if (adopted != null)
			{
				(list = new List<string>(_held.Count + 1)).Add(adopted);
			}
			if (_holding && gen == _gen)
			{
				(list ?? (list = new List<string>(_held.Count))).AddRange(_held);
				_held.Clear();
				_holding = false;
			}
			IReadOnlyList<string> readOnlyList = list;
			return readOnlyList ?? Empty;
		}
	}

	public void Reset()
	{
		lock (_sync)
		{
			_gen++;
			_holding = false;
			_held.Clear();
		}
	}
}
