using System.IO;
using System.Linq;
using System.Text;
using MapEditor.GameData.Schema;
using Xunit;

namespace MapEditor.GameData.Tests.Schema;

public class SchemaHeaderValidatorTests
{
    [Fact]
    public void Validate_WithExactHeaders_ReturnsEmpty()
    {
        var schema = BuildSchema(("a", "A"), ("b", "B"), ("c", "C"));

        var mismatches = SchemaHeaderValidator.Validate(schema, new[] { "A", "B", "C" });

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Validate_WithTrailingHeaders_ReturnsEmpty()
    {
        var schema = BuildSchema(("a", "A"), ("b", "B"));

        var mismatches = SchemaHeaderValidator.Validate(schema, new[] { "A", "B", "helper col", "notes" });

        Assert.Empty(mismatches);
    }

    [Fact]
    public void Validate_WithSwappedHeaders_ReturnsMismatchesAtBothPositions()
    {
        var schema = BuildSchema(("a", "A"), ("b", "B"), ("c", "C"));

        var mismatches = SchemaHeaderValidator.Validate(schema, new[] { "B", "A", "C" });

        Assert.Equal(
            new[]
            {
                new HeaderMismatch(0, "A", "B"),
                new HeaderMismatch(1, "B", "A")
            },
            mismatches);
    }

    [Fact]
    public void Validate_WithMissingHeaders_ReturnsMismatchesWithNullActual()
    {
        var schema = BuildSchema(("a", "A"), ("b", "B"), ("c", "C"));

        var mismatches = SchemaHeaderValidator.Validate(schema, new[] { "A" });

        Assert.Equal(
            new[]
            {
                new HeaderMismatch(1, "B", null),
                new HeaderMismatch(2, "C", null)
            },
            mismatches);
    }

    [Fact]
    public void Validate_WithCaseChangedHeader_ReturnsMismatch()
    {
        var schema = BuildSchema(("a", "A"), ("b", "B"));

        var mismatches = SchemaHeaderValidator.Validate(schema, new[] { "a", "B" });

        var mismatch = Assert.Single(mismatches);
        Assert.Equal(0, mismatch.ColumnIndex);
        Assert.Equal("A", mismatch.Expected);
        Assert.Equal("a", mismatch.Actual);
    }

    [Fact]
    public void Validate_WithWhitespaceChangedHeader_ReturnsMismatch()
    {
        var schema = BuildSchema(("a", "A"), ("b", "B"));

        var mismatches = SchemaHeaderValidator.Validate(schema, new[] { " A ", "B" });

        var mismatch = Assert.Single(mismatches);
        Assert.Equal(0, mismatch.ColumnIndex);
        Assert.Equal("A", mismatch.Expected);
        Assert.Equal(" A ", mismatch.Actual);
    }

    private static SheetSchema BuildSchema(params (string Name, string Header)[] columns)
    {
        var columnJson = string.Join(",", columns.Select(c =>
            $"{{\"name\":\"{c.Name}\",\"header\":\"{c.Header}\",\"kind\":\"Text\",\"sql\":\"TEXT\",\"required\":true,\"pk\":false}}"));
        var json = $"{{\"sheets\":[{{\"sheet\":\"S\",\"table\":\"t\",\"columns\":[{columnJson}]}}]}}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return GameDataSchema.Load(stream).GetRequiredSheet("S");
    }
}
