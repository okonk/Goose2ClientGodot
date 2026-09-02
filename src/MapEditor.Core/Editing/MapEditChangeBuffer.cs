using System;
using System.Collections.Generic;

namespace MapEditor.Core;

internal sealed class MapEditChangeBuffer<T>
{
    private const int InitialSegmentCapacity = 4;
    private const int MaxSegmentCapacity = 4096;

    private readonly List<T[]> _segments = new();
    private int _count;
    private int _allocated;

    internal MapEditChangeBuffer()
    {
        _segments.Add(new T[InitialSegmentCapacity]);
        _allocated = InitialSegmentCapacity;
    }

    internal int Count => _count;

    internal int AllocatedSlotCount => _allocated;

    internal int SegmentCount => _segments.Count;

    internal T this[int index]
    {
        get
        {
            for (int s = 0; s < _segments.Count; s++)
            {
                int length = GetSegmentLength(s);
                if (index < length)
                {
                    return _segments[s][index];
                }

                index -= length;
            }

            throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    internal void Append(T change)
    {
        T[] segment = _segments[^1];
        if (_count == _allocated)
        {
            segment = new T[Math.Min(segment.Length * 2, MaxSegmentCapacity)];
            _segments.Add(segment);
            _allocated += segment.Length;
        }

        segment[_count - (_allocated - segment.Length)] = change;
        _count++;
    }

    internal T[] GetSegment(int index) => _segments[index];

    internal int GetSegmentCapacity(int index) => _segments[index].Length;

    internal int GetSegmentLength(int index)
    {
        if (index == _segments.Count - 1)
        {
            return _count - (_allocated - _segments[index].Length);
        }

        return _segments[index].Length;
    }
}
