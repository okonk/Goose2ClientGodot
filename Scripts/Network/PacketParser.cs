using System;
using System.Collections;
using System.Collections.Generic;

namespace Goose2Client.Network
{
    public class PacketParser
    {
        private string packet;
        private string prefix;
        private int index;

        public char Delimeter { get; set; } = ',';

        public PacketParser(string packet, string prefix)
        {
            this.packet = packet;
            this.prefix = prefix;
            this.index = prefix.Length;
        }

        public string GetWholePacket()
        {
            return packet;
        }

        public string GetRemaining()
        {
            if (index >= packet.Length)
                throw new InvalidOperationException($"Index {index} is out of bounds for packet {prefix}");

            return packet.Substring(index);
        }

        private string GetNextToken()
        {
            if (index >= packet.Length)
                throw new InvalidOperationException($"Index {index} is out of bounds for packet {prefix}");

            string strValue = null;

            for (int i = index; i < packet.Length; i++)
            {
                if (packet[i] == Delimeter)
                {
                    strValue = packet.Substring(index, i - index);
                    index = i + 1;
                    break;
                }
            }

            if (strValue == null)
            {
                strValue = packet.Substring(index);
                index = packet.Length;
            }

            //Console.WriteLine(strValue);

            return strValue;
        }

        public int GetInt32()
        {
            return Convert.ToInt32(GetNextToken());
        }

        public long GetInt64()
        {
            return Convert.ToInt64(GetNextToken());
        }

        public bool GetBool()
        {
            return GetNextToken() != "0";
        }

        public string GetString()
        {
            return GetNextToken();
        }

        public string GetSubstring(int length)
        {
            if (index + length >= packet.Length)
                throw new InvalidOperationException($"Substring is out of bounds for packet {prefix}");

            string result = packet.Substring(index, length);
            index += length;
            return result;
        }

        public char Peek()
        {
            return packet[index];
        }

        public int LengthRemaining()
        {
            if (index >= packet.Length)
                return 0;

            return packet.Length - index;
        }
    }
}
