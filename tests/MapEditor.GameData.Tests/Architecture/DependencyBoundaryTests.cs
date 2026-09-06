using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using Xunit;

namespace MapEditor.GameData.Tests.Architecture;

public class DependencyBoundaryTests
{
    [Fact]
    public void GameData_Assembly_DoesNotReferenceGoogleOrAvalonia()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "MapEditor.GameData.dll");
        Assert.True(File.Exists(path), $"Expected built assembly at {path}.");

        using var stream = File.OpenRead(path);
        using var peReader = new PEReader(stream);
        var metadata = peReader.GetMetadataReader();

        foreach (var handle in metadata.AssemblyReferences)
        {
            var name = metadata.GetString(metadata.GetAssemblyReference(handle).Name);
            Assert.False(
                name.StartsWith("Google", StringComparison.Ordinal),
                $"MapEditor.GameData must not reference '{name}'.");
            Assert.False(
                name.StartsWith("Avalonia", StringComparison.Ordinal),
                $"MapEditor.GameData must not reference '{name}'.");
        }
    }
}
