namespace Goose2.AssetConverter.Adf;

/// <summary>Decodes Aspereta-layout .adf files into Illutia-shaped <see cref="AdfFile"/>
/// objects. Port of gooseclient AdfFile.cs:53-146 (verified against AsperetaCS-1.8).</summary>
public static class AsperetaAdf
{
    public static AdfFile Load(string file)
    {
        byte[] bytes = File.ReadAllBytes(file);
        using var reader = new BinaryReader(new MemoryStream(bytes));

        var result = new AdfFile
        {
            FileNumber = int.Parse(Path.GetFileNameWithoutExtension(file)),
            Type = (AdfType)reader.ReadByte(),
            Animations = new Dictionary<int, Animation>(),
        };

        // Second-to-last byte of the file is the base de-obfuscation offset.
        byte offset = bytes[^2];

        int extraLength = reader.ReadInt32();
        result.ExtraBytes = reader.ReadBytes(extraLength);

        // Header byte adjusts the offset (gooseclient AdfFile.cs:71-73).
        byte offset2 = DecodeByte(reader.ReadByte(), offset);
        offset = (byte)(offset + offset2);

        int records = reader.ReadInt32() - offset;
        // Prefer LOCAL list for pending animations (not static list) — cleaner for concurrency
        var pendingAnimations = new List<(Animation animation, int[] frameIds)>();

        for (int i = 0; i < records; i++)
        {
            int id = reader.ReadInt32() - offset;
            byte n = DecodeByte(reader.ReadByte(), offset);

            if (n == 1)
            {
                int x = reader.ReadInt32() - offset;
                int y = reader.ReadInt32() - offset;
                int w = reader.ReadInt32() - offset;
                int h = reader.ReadInt32() - offset;
                result.Frames.Add(new Frame(id, x, y, w, h));
            }
            else
            {
                var animation = new Animation(id);
                var frameIds = new int[n];
                for (int f = 0; f < n; f++) frameIds[f] = reader.ReadInt32() - offset;
                DecodeByte(reader.ReadByte(), offset); // interval — unused, must still consume
                pendingAnimations.Add((animation, frameIds));
                result.Animations[id] = animation;
            }
        }

        // Resolve animation frame ids -> Frame objects now the table is complete.
        foreach (var (animation, frameIds) in pendingAnimations)
        {
            foreach (int fid in frameIds)
            {
                var frame = result.Frames.FirstOrDefault(fr => fr.Index == fid);
                if (frame is not null) animation.Frames.Add(frame);
            }
        }

        result.FirstFrameIndex = result.Frames.Count == 0 ? 0 : result.Frames[0].Index;
        result.FrameCount = result.Frames.Count;

        int unknown = reader.ReadInt32() - offset;
        if (unknown == 36) result.Type = AdfType.Sound;

        // Payload: de-offset every byte and remove the 790-byte interleave
        // (identical scheme to Illutia — IllutiaData.cs:218-225).
        int length = (int)(reader.BaseStream.Length - reader.BaseStream.Position);
        byte[] buffer = reader.ReadBytes(length);
        byte[] data = new byte[buffer.Length - (buffer.Length / 790)];
        for (int k = 0; k < buffer.Length; k++)
        {
            int idx = k - (k / 790);
            if (idx >= data.Length) continue;
            data[idx] = DecodeByte(buffer[k], offset);
        }
        result.FileData = data;

        return result;
    }

    private static byte DecodeByte(byte data, byte offset) => (byte)(data - offset);
}
