using Goose2.AssetConverter.Adf;

namespace AssetConverter.Tests.Fixtures;

public static class AnimationSourceFixture
{
    public static string CreateDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "ac_animation_manifest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "data"));
        return root;
    }

    public static string DataDir(string root) => Path.Combine(root, "data");

    public static string CompiledEncPath(string root) => Path.Combine(root, "compiled.enc");

    public static string AsperetaDataDir(string root) => Path.Combine(root, "aspereta");

    public static string AsperetaCompiledEncPath(string root) => Path.Combine(root, "aspereta_compiled.enc");

    public static void WriteAdf(
        string dataDir, int fileNumber, int firstFrameIndex, int frameCount,
        params (int Id, int[] FrameIndices)[] animations)
    {
        using var writer = new BinaryWriter(File.Create(Path.Combine(dataDir, $"{fileNumber}.adf")));
        writer.Write((byte)AdfType.Graphic);
        writer.Write((byte)1);
        writer.Write(0);
        writer.Write((byte)0);
        writer.Write(firstFrameIndex);
        writer.Write(frameCount);
        writer.Write(animations.Length == 0 ? 0 : animations[0].Id);
        writer.Write(animations.Length);
        for (int i = 0; i < frameCount; i++)
        {
            writer.Write(0);
            writer.Write(0);
            writer.Write(48);
            writer.Write(64);
        }
        foreach (var (_, frames) in animations)
        {
            writer.Write((byte)frames.Length);
            foreach (var frame in frames)
                writer.Write(frame);
        }
        writer.Write(0);
        writer.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
    }

    public static void WriteSoundAdf(string dataDir, int fileNumber)
    {
        using var writer = new BinaryWriter(File.Create(Path.Combine(dataDir, $"{fileNumber}.adf")));
        writer.Write((byte)AdfType.Sound);
        writer.Write((byte)1);
        writer.Write(0);
        writer.Write((byte)0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
    }

    public static void WriteCompiledEnc(string path, params (AnimationType Type, int Id, int[] Files)[] records)
    {
        using var writer = new BinaryWriter(File.Create(path));
        foreach (var (type, id, files) in records)
        {
            writer.Write((short)((int)type + 1));
            writer.Write(id);
            for (int i = 0; i < 44; i++)
                writer.Write(0);
            for (int i = 0; i < 11; i++)
                writer.Write(i < files.Length ? files[i] : 0);
        }
    }

    public static void WriteAsperetaAdf(
        string dataDir, int fileNumber,
        (int Index, int X, int Y, int W, int H)[] frames,
        (int Id, int[] FrameIds)[] animations)
    {
        Directory.CreateDirectory(dataDir);
        using var writer = new BinaryWriter(File.Create(Path.Combine(dataDir, $"{fileNumber}.adf")));
        writer.Write((byte)AdfType.Graphic);
        writer.Write(0);
        writer.Write((byte)0);
        writer.Write(frames.Length + animations.Length);
        foreach (var (index, x, y, w, h) in frames)
        {
            writer.Write(index);
            writer.Write((byte)1);
            writer.Write(x);
            writer.Write(y);
            writer.Write(w);
            writer.Write(h);
        }
        foreach (var (id, frameIds) in animations)
        {
            writer.Write(id);
            writer.Write((byte)frameIds.Length);
            foreach (var frameId in frameIds)
                writer.Write(frameId);
            writer.Write((byte)0);
        }
        writer.Write(0);
        writer.Write(new byte[] { 0, 0 });
    }

    public static void WriteAsperetaSoundAdf(string dataDir, int fileNumber)
    {
        Directory.CreateDirectory(dataDir);
        using var writer = new BinaryWriter(File.Create(Path.Combine(dataDir, $"{fileNumber}.adf")));
        writer.Write((byte)AdfType.Sound);
        writer.Write(0);
        writer.Write((byte)0);
        writer.Write(0);
        writer.Write(0);
        writer.Write(new byte[] { 0, 0 });
    }

    public static void WriteAsperetaMalformedAdf(string dataDir, int fileNumber)
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllBytes(Path.Combine(dataDir, $"{fileNumber}.adf"), new byte[] { 1 });
    }

    public static void WriteAsperetaCompiledEnc(
        string path, params (AnimationType Type, int Id, int[] Indexes)[] records)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new BinaryWriter(File.Create(path));
        foreach (var (type, id, indexes) in records)
        {
            writer.Write((short)((int)type + 1));
            writer.Write(id);
            for (int i = 0; i < 32; i++)
                writer.Write(i < indexes.Length ? indexes[i] : 0);
        }
    }
}
