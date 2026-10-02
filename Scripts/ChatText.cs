namespace Goose2Client;

/// <summary>
/// Chat text as sent versus as shown: the protocol is ASCII and the server stores a heart as a
/// backtick, so ♥ never goes on the wire.
/// </summary>
public static class ChatText
{
    public const char HeartCode = '`';
    public const char HeartGlyph = '♥';

    public static string ToDisplay(string text) => text.Replace(HeartCode, HeartGlyph);

    public static string ToWire(string text) => text.Replace(HeartGlyph, HeartCode);
}
