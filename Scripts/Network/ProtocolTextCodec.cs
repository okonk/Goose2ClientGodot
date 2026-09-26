using System;
using System.Text;

namespace Goose2Client.Network
{
    public static class ProtocolTextCodec
    {
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        public static bool TryEncode(string text, int maxUtf8Bytes, out string? base64)
        {
            base64 = null;
            if (text == null || maxUtf8Bytes < 0)
                return false;
            byte[] bytes;
            try
            {
                bytes = StrictUtf8.GetBytes(text);
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
            if (bytes.Length > maxUtf8Bytes)
                return false;
            base64 = Convert.ToBase64String(bytes);
            return true;
        }

        public static bool TryDecode(string? base64, out string? text)
        {
            text = null;
            if (base64 == null)
                return false;
            if (!IsCanonicalPaddedBase64(base64))
                return false;
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64);
            }
            catch (FormatException)
            {
                return false;
            }
            try
            {
                text = StrictUtf8.GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }
            return true;
        }

        private static bool IsCanonicalPaddedBase64(string s)
        {
            int len = s.Length;
            if (len % 4 != 0)
                return false;
            for (int i = 0; i < len; i++)
            {
                char c = s[i];
                if (c == '=')
                {
                    if (i < len - 2)
                        return false;
                    continue;
                }
                if (IndexOf(c) < 0)
                    return false;
            }
            int pad = 0;
            if (len >= 4)
            {
                if (s[len - 1] == '=')
                    pad++;
                if (s[len - 2] == '=')
                    pad++;
            }
            if (pad == 2)
            {
                int v = IndexOf(s[len - 3]);
                if (v < 0 || v % 16 != 0)
                    return false;
            }
            else if (pad == 1)
            {
                int v = IndexOf(s[len - 2]);
                if (v < 0 || v % 4 != 0)
                    return false;
            }
            return true;
        }

        private static int IndexOf(char c)
        {
            if (c >= 'A' && c <= 'Z')
                return c - 'A';
            if (c >= 'a' && c <= 'z')
                return c - 'a' + 26;
            if (c >= '0' && c <= '9')
                return c - '0' + 52;
            if (c == '+')
                return 62;
            if (c == '/')
                return 63;
            return -1;
        }
    }
}
