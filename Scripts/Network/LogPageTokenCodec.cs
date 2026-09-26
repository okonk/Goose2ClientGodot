namespace Goose2Client.Network
{
    public static class LogPageTokenCodec
    {
        public const int TokenLength = 22;
        public const int DecodedByteLength = 16;

        public static bool IsCanonical(string? token)
        {
            if (token == null || token.Length != TokenLength)
                return false;
            for (int i = 0; i < TokenLength; i++)
            {
                char c = token[i];
                bool ok = (c >= 'A' && c <= 'Z')
                    || (c >= 'a' && c <= 'z')
                    || (c >= '0' && c <= '9')
                    || c == '-'
                    || c == '_';
                if (!ok)
                    return false;
            }
            return true;
        }

        public static bool TryDecode(string? token, out byte[] bytes)
        {
            bytes = null;
            if (!IsCanonical(token))
                return false;
            bytes = new byte[DecodedByteLength];
            for (int grp = 0; grp < 5; grp++)
            {
                int a = Value(token[grp * 4]);
                int b = Value(token[grp * 4 + 1]);
                int c = Value(token[grp * 4 + 2]);
                int d = Value(token[grp * 4 + 3]);
                bytes[grp * 3] = (byte)((a << 2) | (b >> 4));
                bytes[grp * 3 + 1] = (byte)(((b & 0x0F) << 4) | (c >> 2));
                bytes[grp * 3 + 2] = (byte)(((c & 0x03) << 6) | d);
            }
            int x = Value(token[20]);
            int y = Value(token[21]);
            bytes[15] = (byte)((x << 2) | (y >> 4));
            return true;
        }

        private static int Value(char c)
        {
            if (c >= 'A' && c <= 'Z')
                return c - 'A';
            if (c >= 'a' && c <= 'z')
                return c - 'a' + 26;
            if (c >= '0' && c <= '9')
                return c - '0' + 52;
            if (c == '-')
                return 62;
            return 63;
        }
    }
}
