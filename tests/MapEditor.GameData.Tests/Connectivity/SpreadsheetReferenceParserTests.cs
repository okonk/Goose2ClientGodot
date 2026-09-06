using System;
using MapEditor.GameData.Connectivity;
using Xunit;

namespace MapEditor.GameData.Tests.Connectivity;

public class SpreadsheetReferenceParserTests
{
    [Theory]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123", "abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123/edit", "abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123/", "abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123/edit/", "abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123?gid=0", "abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123#gid=0", "abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123?usp=sharing&authuser=0#gid=17", "abc123")]
    [InlineData("  https://docs.google.com/spreadsheets/d/abc123  ", "abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/Abc123_-Zz99", "Abc123_-Zz99")]
    public void TryParse_ValidSpreadsheetUrl_ReturnsCanonicalReference(string value, string id)
    {
        var parsed = SpreadsheetReferenceParser.TryParse(value, out var reference);

        Assert.True(parsed);
        Assert.Equal(id, reference.Id);
        Assert.Equal($"https://docs.google.com/spreadsheets/d/{id}", reference.CanonicalUrl);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc123")]
    [InlineData("/spreadsheets/d/abc123")]
    [InlineData("docs.google.com/spreadsheets/d/abc123")]
    [InlineData("http://docs.google.com/spreadsheets/d/abc123")]
    [InlineData("ftp://docs.google.com/spreadsheets/d/abc123")]
    [InlineData("https://user@docs.google.com/spreadsheets/d/abc123")]
    [InlineData("https://user:pass@docs.google.com/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com@evil.com/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com\\@evil.com/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com\\spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com:8443/spreadsheets/d/abc123")]
    [InlineData("https://evil.docs.google.com/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com.evil.com/spreadsheets/d/abc123")]
    [InlineData("https://notdocs.google.com/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.co.uk/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com/SPREADSHEETS/d/abc123")]
    [InlineData("https://docs.google.com/spreadsheets/abc123")]
    [InlineData("https://docs.google.com/spreadsheets")]
    [InlineData("https://docs.google.com/spreadsheets/d")]
    [InlineData("https://docs.google.com/spreadsheets/d/")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123/extra")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123/edit/extra")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123/edit2")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc123//edit")]
    [InlineData("https://other.com/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com/spreadsheets%2Fd/abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d%2Fabc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc.123")]
    [InlineData("https://docs.google.com/spreadsheets/d/abc+123")]
    [InlineData("https://docs.google.com/spreadsheets/d/ab%2Fc")]
    [InlineData("https://docs.google.com/spreadsheets/d/ab c")]
    public void TryParse_InvalidValue_ReturnsFalse(string? value)
    {
        var parsed = SpreadsheetReferenceParser.TryParse(value, out var reference);

        Assert.False(parsed);
        Assert.Equal(default, reference);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("http://docs.google.com/spreadsheets/d/abc123")]
    [InlineData("https://docs.google.com/spreadsheets/d/")]
    public void Parse_InvalidValue_ThrowsFormatException(string value)
    {
        Assert.Throws<FormatException>(() => SpreadsheetReferenceParser.Parse(value));
    }

    [Fact]
    public void Parse_ValidValue_ReturnsCanonicalReference()
    {
        var reference = SpreadsheetReferenceParser.Parse("https://docs.google.com/spreadsheets/d/abc123/edit?x=1");

        Assert.Equal("abc123", reference.Id);
        Assert.Equal("https://docs.google.com/spreadsheets/d/abc123", reference.CanonicalUrl);
    }
}
