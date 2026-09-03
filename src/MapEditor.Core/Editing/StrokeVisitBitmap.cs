namespace MapEditor.Core;

internal sealed class StrokeVisitBitmap
{
    private readonly ulong[] _words;

    internal StrokeVisitBitmap(int tileCount)
    {
        _words = new ulong[(tileCount + 63) / 64];
    }

    internal int WordCount => _words.Length;

    internal bool IsVisited(int index)
    {
        return (_words[index >> 6] & (1UL << (index & 63))) != 0;
    }

    internal bool TryMark(int index)
    {
        int word = index >> 6;
        ulong bit = 1UL << (index & 63);
        if ((_words[word] & bit) != 0)
        {
            return false;
        }

        _words[word] |= bit;
        return true;
    }
}
