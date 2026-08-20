using System;
using System.Collections.Generic;
using System.Drawing;
using OpenCvSharp;
using OpenCvSharp.Extensions;

namespace PokemonHelper.Services.Recognition;

public sealed class LogFrameFusion : IDisposable
{
	private Mat? _refMask;

	private int _refCount;

	private Mat? _stackGray;

	private int _frames;

	private bool _firstEmitDone;

	private int _countAtFirstEmit;

	private long _groupStartMs;

	private int _groupId;

	private Mat? _vanishMask;

	public double BinarizeThreshold { get; set; } = 180.0;

	public bool VotingMode { get; set; }

	public double MentMinFrac { get; set; } = 0.002;

	public double SameMentIouMin { get; set; } = 0.6;

	public double VanishContainMin { get; set; } = 0.85;

	public double VanishCountMaxRatio { get; set; } = 0.65;

	public double GrowContainMin { get; set; } = 0.9;

	public double GrowCountMinRatio { get; set; } = 1.3;

	public int FirstEmitFrames { get; set; } = 2;

	public int FirstEmitFlushMs { get; set; } = 700;

	public double RefineMinShrink { get; set; } = 0.03;

	internal bool GroupOpen => _refMask != null;

	internal int FramesInGroup => _frames;

	public IReadOnlyList<LogFusionAction> Advance(Bitmap? frame, bool changed, long nowMs)
	{
		List<LogFusionAction> list = new List<LogFusionAction>(2);
		if (changed && frame != null && frame.Width > 0 && frame.Height > 0)
		{
			using Mat mat = ToGray(frame);
			using Mat mat2 = new Mat();
			Cv2.Threshold(mat, mat2, BinarizeThreshold, 255.0, ThresholdTypes.Binary);
			int num = Cv2.CountNonZero(mat2);
			long num2 = (long)mat.Width * (long)mat.Height;
			bool num3 = (double)num >= MentMinFrac * (double)num2;
			if (_refMask != null && (_refMask.Width != mat2.Width || _refMask.Height != mat2.Height))
			{
				ClearGroup();
			}
			if (_vanishMask != null && (_vanishMask.Width != mat2.Width || _vanishMask.Height != mat2.Height))
			{
				_vanishMask.Dispose();
				_vanishMask = null;
			}
			if (!num3)
			{
				CloseGroup(list);
				ClearVanish();
				list.Add(new LogFusionAction(new Bitmap(frame), IsFused: false));
			}
			else if (_vanishMask != null && Containment(mat2, num, _vanishMask) >= VanishContainMin && (double)num <= VanishCountMaxRatio * (double)Cv2.CountNonZero(_vanishMask))
			{
				_vanishMask.Dispose();
				_vanishMask = mat2.Clone();
			}
			else if (_refMask == null)
			{
				ClearVanish();
				StartGroup(mat, mat2, num, nowMs);
				if (VotingMode)
				{
					EmitCandidate(list, frame);
				}
			}
			else
			{
				int num4 = Intersection(mat2, _refMask);
				double num5 = ((num > 0) ? ((double)num4 / (double)num) : 0.0);
				double num6 = ((_refCount > 0) ? ((double)num4 / (double)_refCount) : 0.0);
				double num7 = ((num + _refCount - num4 > 0) ? ((double)num4 / (double)(num + _refCount - num4)) : 0.0);
				if (num5 >= VanishContainMin && (double)num <= VanishCountMaxRatio * (double)_refCount)
				{
					CloseGroup(list);
					_vanishMask = mat2.Clone();
				}
				else if (num6 >= GrowContainMin && (double)num >= GrowCountMinRatio * (double)_refCount)
				{
					ClearGroup();
					StartGroup(mat, mat2, num, nowMs);
					if (VotingMode)
					{
						EmitCandidate(list, frame);
					}
				}
				else if (num7 >= SameMentIouMin)
				{
					Cv2.Min(_stackGray, mat, _stackGray);
					_frames++;
					if (VotingMode)
					{
						EmitCandidate(list, frame);
					}
					else if (!_firstEmitDone && _frames >= FirstEmitFrames)
					{
						EmitFused(list, markFirst: true);
					}
				}
				else
				{
					CloseGroup(list);
					StartGroup(mat, mat2, num, nowMs);
					if (VotingMode)
					{
						EmitCandidate(list, frame);
					}
				}
			}
		}
		if (!VotingMode && _refMask != null && !_firstEmitDone && nowMs - _groupStartMs >= FirstEmitFlushMs)
		{
			EmitFused(list, markFirst: true);
		}
		return list;
	}

	public void Reset()
	{
		ClearGroup();
		ClearVanish();
	}

	public void Dispose()
	{
		Reset();
	}

	private void StartGroup(Mat gray, Mat mask, int count, long nowMs)
	{
		_refMask = mask.Clone();
		_refCount = count;
		_stackGray = gray.Clone();
		_frames = 1;
		_firstEmitDone = false;
		_countAtFirstEmit = 0;
		_groupStartMs = nowMs;
		_groupId++;
	}

	private void CloseGroup(List<LogFusionAction> actions)
	{
		if (_refMask != null)
		{
			if (VotingMode)
			{
				EmitStack(actions, LogFusionActionKind.CompositeOnClose);
			}
			else if (!_firstEmitDone)
			{
				EmitFused(actions, markFirst: false);
			}
			else if ((double)CurrentStackCount() <= (double)_countAtFirstEmit * (1.0 - RefineMinShrink))
			{
				EmitFused(actions, markFirst: false);
			}
			ClearGroup();
		}
	}

	private void EmitFused(List<LogFusionAction> actions, bool markFirst)
	{
		EmitStack(actions, LogFusionActionKind.Fused);
		if (markFirst)
		{
			_firstEmitDone = true;
			_countAtFirstEmit = CurrentStackCount();
		}
	}

	private void EmitStack(List<LogFusionAction> actions, LogFusionActionKind kind)
	{
		if (_stackGray == null)
		{
			return;
		}
		using Mat mat = new Mat();
		Cv2.CvtColor(_stackGray, mat, ColorConversionCodes.GRAY2BGR);
		actions.Add(new LogFusionAction(mat.ToBitmap(), IsFused: true, kind, _groupId, _frames));
	}

	private void EmitCandidate(List<LogFusionAction> actions, Bitmap frame)
	{
		actions.Add(new LogFusionAction(new Bitmap(frame), IsFused: false, LogFusionActionKind.Candidate, _groupId));
	}

	private int CurrentStackCount()
	{
		if (_stackGray == null)
		{
			return 0;
		}
		using Mat mat = new Mat();
		Cv2.Threshold(_stackGray, mat, BinarizeThreshold, 255.0, ThresholdTypes.Binary);
		return Cv2.CountNonZero(mat);
	}

	private void ClearGroup()
	{
		_refMask?.Dispose();
		_refMask = null;
		_stackGray?.Dispose();
		_stackGray = null;
		_refCount = 0;
		_frames = 0;
		_firstEmitDone = false;
		_countAtFirstEmit = 0;
	}

	private void ClearVanish()
	{
		_vanishMask?.Dispose();
		_vanishMask = null;
	}

	private static double Containment(Mat mask, int count, Mat other)
	{
		if (count <= 0)
		{
			return 0.0;
		}
		return (double)IntersectionOf(mask, other) / (double)count;
	}

	private static int Intersection(Mat a, Mat b)
	{
		return IntersectionOf(a, b);
	}

	private static int IntersectionOf(Mat a, Mat b)
	{
		using Mat mat = new Mat();
		Cv2.BitwiseAnd(a, b, mat);
		return Cv2.CountNonZero(mat);
	}

	private static Mat ToGray(Bitmap src)
	{
		using Mat mat = src.ToMat();
		Mat mat2 = new Mat();
		if (mat.Channels() == 4)
		{
			Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGRA2GRAY);
		}
		else if (mat.Channels() == 3)
		{
			Cv2.CvtColor(mat, mat2, ColorConversionCodes.BGR2GRAY);
		}
		else
		{
			mat.CopyTo(mat2);
		}
		return mat2;
	}
}
