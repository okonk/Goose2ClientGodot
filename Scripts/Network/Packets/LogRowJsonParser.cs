using System;
using System.Text.Json;
using Goose2Client.Logs;

namespace Goose2Client.Network.Packets
{
    public static class LogRowJsonParser
    {
        public const int MaxDecodedBytes = 262144;

        public static bool TryParse(byte[] json, out LogRow row)
        {
            row = null;
            if (json == null || json.Length == 0 || json.Length > MaxDecodedBytes)
                return false;
            try
            {
                var reader = new Utf8JsonReader(json);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
                    return false;

                long rowId;
                long utc;
                long typeId;
                bool typeIsInteger;
                string eventLabel;
                string eventGroup;
                string otherKindName;
                LogRowEntity primary;
                LogRowEntity related;
                LogRowMap map;
                LogRowRaw raw;
                string summary;
                string originalText;

                if (!ReadProperty(ref reader, "rowId")) return false;
                if (!ReadInt64(ref reader, out rowId)) return false;
                if (!ReadProperty(ref reader, "utcMilliseconds")) return false;
                if (!ReadInt64(ref reader, out utc)) return false;
                if (!ReadProperty(ref reader, "typeId")) return false;
                if (!ReadInt64(ref reader, out typeId)) return false;
                if (!ReadProperty(ref reader, "typeIsInteger")) return false;
                if (!ReadBool(ref reader, out typeIsInteger)) return false;
                if (!ReadProperty(ref reader, "eventLabel")) return false;
                if (!ReadString(ref reader, out eventLabel)) return false;
                if (!ReadProperty(ref reader, "eventGroup")) return false;
                if (!ReadString(ref reader, out eventGroup)) return false;
                if (!ReadProperty(ref reader, "otherIdKind")) return false;
                if (!ReadString(ref reader, out otherKindName)) return false;
                if (!TryParseOtherIdKind(otherKindName, out LogOtherIdKind otherIdKind)) return false;
                if (!ReadProperty(ref reader, "primary")) return false;
                if (!TryReadEntity(ref reader, out primary)) return false;
                if (primary == null) return false;
                if (!ReadProperty(ref reader, "related")) return false;
                if (!TryReadEntity(ref reader, out related)) return false;
                if (!ReadProperty(ref reader, "map")) return false;
                if (!TryReadMap(ref reader, out map)) return false;
                if (!ReadProperty(ref reader, "raw")) return false;
                if (!TryReadRaw(ref reader, out raw)) return false;
                if (!ReadProperty(ref reader, "summary")) return false;
                if (!ReadString(ref reader, out summary)) return false;
                if (!ReadProperty(ref reader, "originalText")) return false;
                if (!ReadString(ref reader, out originalText)) return false;
                if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
                    return false;
                if (reader.Read())
                    return false;

                row = new LogRow(rowId, utc, typeId, typeIsInteger, eventLabel, eventGroup, otherIdKind, primary, related, map, raw, summary, originalText);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadProperty(ref Utf8JsonReader r, string name)
        {
            if (!r.Read() || r.TokenType != JsonTokenType.PropertyName)
                return false;
            string actual = r.GetString();
            return actual != null && actual == name;
        }

        private static bool ReadInt64(ref Utf8JsonReader r, out long value)
        {
            if (!r.Read() || r.TokenType != JsonTokenType.Number)
            {
                value = 0;
                return false;
            }
            value = r.GetInt64();
            return true;
        }

        private static bool ReadBool(ref Utf8JsonReader r, out bool value)
        {
            if (!r.Read())
            {
                value = false;
                return false;
            }
            if (r.TokenType == JsonTokenType.True)
            {
                value = true;
                return true;
            }
            if (r.TokenType == JsonTokenType.False)
            {
                value = false;
                return true;
            }
            value = false;
            return false;
        }

        private static bool ReadString(ref Utf8JsonReader r, out string value)
        {
            if (!r.Read() || r.TokenType != JsonTokenType.String)
            {
                value = null;
                return false;
            }
            value = r.GetString();
            return value != null;
        }

        private static bool TryReadEntity(ref Utf8JsonReader r, out LogRowEntity entity)
        {
            entity = null;
            if (!r.Read())
                return false;
            if (r.TokenType == JsonTokenType.Null)
                return true;
            if (r.TokenType != JsonTokenType.StartObject)
                return false;
            string label;
            string kindName;
            string name;
            if (!ReadProperty(ref r, "label")) return false;
            if (!ReadString(ref r, out label)) return false;
            if (!ReadProperty(ref r, "kind")) return false;
            if (!ReadString(ref r, out kindName)) return false;
            if (!TryParseEntityKind(kindName, out LogEntityKind kind)) return false;
            if (!ReadProperty(ref r, "id")) return false;
            if (!r.Read()) return false;
            long? id;
            if (r.TokenType == JsonTokenType.Null)
                id = null;
            else if (r.TokenType == JsonTokenType.Number)
                id = r.GetInt64();
            else
                return false;
            if (!ReadProperty(ref r, "name")) return false;
            if (!ReadString(ref r, out name)) return false;
            if (!ReadProperty(ref r, "canQuickFilter")) return false;
            if (!ReadBool(ref r, out bool canQuickFilter)) return false;
            if (!r.Read() || r.TokenType != JsonTokenType.EndObject)
                return false;
            entity = new LogRowEntity(label, kind, id, name, canQuickFilter);
            return true;
        }

        private static bool TryReadMap(ref Utf8JsonReader r, out LogRowMap map)
        {
            map = null;
            if (!r.Read())
                return false;
            if (r.TokenType == JsonTokenType.Null)
                return true;
            if (r.TokenType != JsonTokenType.StartObject)
                return false;
            if (!ReadProperty(ref r, "id")) return false;
            if (!ReadInt64(ref r, out long id)) return false;
            if (!ReadProperty(ref r, "name")) return false;
            if (!ReadString(ref r, out string name)) return false;
            if (!ReadProperty(ref r, "canQuickFilter")) return false;
            if (!ReadBool(ref r, out bool canQuickFilter)) return false;
            if (!r.Read() || r.TokenType != JsonTokenType.EndObject)
                return false;
            map = new LogRowMap(id, name, canQuickFilter);
            return true;
        }

        private static bool TryReadRaw(ref Utf8JsonReader r, out LogRowRaw raw)
        {
            raw = null;
            if (!r.Read() || r.TokenType != JsonTokenType.StartObject)
                return false;
            long playerId;
            bool playerIdIsInteger;
            long otherId;
            bool otherIdIsInteger;
            long mapId;
            bool mapIdIsInteger;
            long mapX;
            bool mapXIsInteger;
            long mapY;
            bool mapYIsInteger;
            if (!ReadProperty(ref r, "playerId")) return false;
            if (!ReadInt64(ref r, out playerId)) return false;
            if (!ReadProperty(ref r, "playerIdIsInteger")) return false;
            if (!ReadBool(ref r, out playerIdIsInteger)) return false;
            if (!ReadProperty(ref r, "otherId")) return false;
            if (!ReadInt64(ref r, out otherId)) return false;
            if (!ReadProperty(ref r, "otherIdIsInteger")) return false;
            if (!ReadBool(ref r, out otherIdIsInteger)) return false;
            if (!ReadProperty(ref r, "mapId")) return false;
            if (!ReadInt64(ref r, out mapId)) return false;
            if (!ReadProperty(ref r, "mapIdIsInteger")) return false;
            if (!ReadBool(ref r, out mapIdIsInteger)) return false;
            if (!ReadProperty(ref r, "mapX")) return false;
            if (!ReadInt64(ref r, out mapX)) return false;
            if (!ReadProperty(ref r, "mapXIsInteger")) return false;
            if (!ReadBool(ref r, out mapXIsInteger)) return false;
            if (!ReadProperty(ref r, "mapY")) return false;
            if (!ReadInt64(ref r, out mapY)) return false;
            if (!ReadProperty(ref r, "mapYIsInteger")) return false;
            if (!ReadBool(ref r, out mapYIsInteger)) return false;
            if (!r.Read() || r.TokenType != JsonTokenType.EndObject)
                return false;
            raw = new LogRowRaw(playerId, playerIdIsInteger, otherId, otherIdIsInteger, mapId, mapIdIsInteger, mapX, mapXIsInteger, mapY, mapYIsInteger);
            return true;
        }

        private static bool TryParseOtherIdKind(string name, out LogOtherIdKind kind)
        {
            switch (name)
            {
                case "Unused": kind = LogOtherIdKind.Unused; return true;
                case "Player": kind = LogOtherIdKind.Player; return true;
                case "Guild": kind = LogOtherIdKind.Guild; return true;
                case "Item": kind = LogOtherIdKind.Item; return true;
                case "NpcTemplate": kind = LogOtherIdKind.NpcTemplate; return true;
                default: kind = LogOtherIdKind.Unused; return false;
            }
        }

        private static bool TryParseEntityKind(string name, out LogEntityKind kind)
        {
            switch (name)
            {
                case "Player": kind = LogEntityKind.Player; return true;
                case "Item": kind = LogEntityKind.Item; return true;
                case "Guild": kind = LogEntityKind.Guild; return true;
                case "NpcTemplate": kind = LogEntityKind.NpcTemplate; return true;
                case "Map": kind = LogEntityKind.Map; return true;
                case "StoredValue": kind = LogEntityKind.StoredValue; return true;
                default: kind = LogEntityKind.Player; return false;
            }
        }
    }
}
