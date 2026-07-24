using Goose2.AssetConverter.Adf;

namespace Goose2.AssetConverter.Aspereta;

/// <summary>
/// One entry from Aspereta's <c>compiled.enc</c> (4 facings × 8 slots = 32 animation indexes).
/// Layout matches AsperetaClient: walk at <c>facing * 4</c>, attack at <c>16 + facing * 4</c>.
/// </summary>
public sealed record AsperetaCompiledAnimation(AnimationType Type, int Id, int[] Indexes)
{
    public int Walk(int facing) => Indexes[facing * 4];
    public int Attack(int facing) => Indexes[16 + facing * 4];
}

/// <summary>Loads Aspereta's compact <c>compiled.enc</c> (type, id, 32 indexes per entry).</summary>
public static class AsperetaCompiledEnc
{
    public static List<AsperetaCompiledAnimation> Load(string path)
    {
        var result = new List<AsperetaCompiledAnimation>();
        using var reader = new BinaryReader(File.OpenRead(path));
        while (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            int rawType = reader.ReadInt16() - 1;
            int id = reader.ReadInt32();
            var indexes = new int[32];
            for (int i = 0; i < 32; i++)
                indexes[i] = reader.ReadInt32();

            // Aspereta lacks Eyes (Illutia slot 2); Body is the only type we rely on here.
            AnimationType type = rawType == 0 ? AnimationType.Body : (AnimationType)rawType;
            result.Add(new AsperetaCompiledAnimation(type, id, indexes));
        }
        return result;
    }
}
