using System;
using System.Collections.Generic;
using System.Linq;
using MapEditor.Core;
using Xunit;

namespace MapEditor.Core.Tests;

public class TerrainCatalogValidatorTests
{
    [Fact]
    public void Validate_ValidCatalog_IsValidWithIndex()
    {
        var result = TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid());

        Assert.True(result.IsValid);
        Assert.NotNull(result.Index);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == TerrainValidationSeverity.Error);
    }

    [Fact]
    public void Validate_EmptyCatalog_IsValidWithEmptyIndex()
    {
        var catalog = new TerrainCatalog(
            Array.Empty<TerrainDefinition>(),
            Array.Empty<TerrainGraphicDefinition>());

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Index);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_Issues_ExposeReadOnlyView()
    {
        var result = TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid());

        Assert.Throws<NotSupportedException>(
            () => ((IList<TerrainValidationIssue>)result.Issues).Add(new TerrainValidationIssue(
                TerrainValidationSeverity.Warning,
                TerrainValidationCode.MissingCoveragePattern,
                "injected")));
    }

    [Fact]
    public void Validate_EmptyTerrainId_ReturnsErrorAndNoIndex()
    {
        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(Guid.Empty, "Ghost"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass")
            },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Solid(Guid.Empty))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issue = Assert.Single(result.Issues, i => i.Code == TerrainValidationCode.EmptyTerrainId);
        Assert.Equal(TerrainValidationSeverity.Error, issue.Severity);
    }

    [Fact]
    public void Validate_DuplicateTerrainNamesCaseInsensitive_ReturnsErrorPerTerrain()
    {
        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Water, "GRASS")
            },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Water))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issues = result.Issues.Where(i => i.Code == TerrainValidationCode.DuplicateTerrainName).ToList();
        Assert.Equal(2, issues.Count);
        Assert.Equal(
            new Guid?[] { TerrainCatalogFixture.Grass, TerrainCatalogFixture.Water },
            issues.Select(i => i.TerrainId).OrderBy(id => id).ToArray());
    }

    [Fact]
    public void Validate_CenterlessOrUnknownCenter_ReturnsContextualError()
    {
        var unknown = Guid.NewGuid();
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Pattern(null)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Pattern(unknown))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var centerless = Assert.Single(result.Issues, i => i.Code == TerrainValidationCode.CenterlessGraphic);
        Assert.Equal(TerrainCatalogFixture.Ref(0, 1), centerless.GraphicReference);
        Assert.Equal(TerrainPeer.Center, centerless.Peer);
        var unknownCenter = Assert.Single(result.Issues, i => i.Code == TerrainValidationCode.UnknownPeer);
        Assert.Equal(TerrainCatalogFixture.Ref(0, 2), unknownCenter.GraphicReference);
        Assert.Equal(TerrainPeer.Center, unknownCenter.Peer);
        Assert.Equal(unknown, unknownCenter.TerrainId);
    }

    [Fact]
    public void Validate_UnknownPeer_ReturnsErrorWithExactPeerContext()
    {
        var unknown = Guid.NewGuid();
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass, (TerrainPeer.SouthWest, unknown)))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issue = Assert.Single(result.Issues, i => i.Code == TerrainValidationCode.UnknownPeer);
        Assert.Equal(TerrainValidationSeverity.Error, issue.Severity);
        Assert.Equal(TerrainCatalogFixture.Ref(0, 2), issue.GraphicReference);
        Assert.Equal(TerrainPeer.SouthWest, issue.Peer);
        Assert.Equal(unknown, issue.TerrainId);
    }

    [Fact]
    public void Validate_TerrainWithoutCenteredGraphic_ReturnsError()
    {
        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Water, "Water")
            },
            new[] { TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)) });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issue = Assert.Single(result.Issues, i => i.Code == TerrainValidationCode.MissingCenteredGraphic);
        Assert.Equal(TerrainCatalogFixture.Water, issue.TerrainId);
    }

    [Fact]
    public void Validate_DuplicateGraphicReferences_ReturnsError()
    {
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issue = Assert.Single(result.Issues, i => i.Code == TerrainValidationCode.DuplicateGraphicReference);
        Assert.Equal(TerrainCatalogFixture.Ref(0, 1), issue.GraphicReference);
    }

    [Fact]
    public void Validate_GraphicZero_IsRejected()
    {
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 0, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(3, 0, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issues = result.Issues.Where(i => i.Code == TerrainValidationCode.InvalidGraphicNumber).ToList();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.GraphicReference == TerrainCatalogFixture.Ref(0, 0));
        Assert.Contains(issues, i => i.GraphicReference == TerrainCatalogFixture.Ref(3, 0));
    }

    [Fact]
    public void Validate_SheetOutsideShortRange_ReturnsError()
    {
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(short.MaxValue + 1, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(short.MinValue - 1, 2, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issues = result.Issues.Where(i => i.Code == TerrainValidationCode.InvalidSheetNumber).ToList();
        Assert.Equal(2, issues.Count);
        Assert.Contains(issues, i => i.GraphicReference == TerrainCatalogFixture.Ref(short.MaxValue + 1, 1));
        Assert.Contains(issues, i => i.GraphicReference == TerrainCatalogFixture.Ref(short.MinValue - 1, 2));
    }

    [Fact]
    public void Validate_ValidDuplicateCompletePatterns_AreAccepted()
    {
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(1, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Index);
        Assert.DoesNotContain(result.Issues, issue => issue.Severity == TerrainValidationSeverity.Error);
    }

    [Fact]
    public void Validate_DuplicateTerrainId_ReturnsErrorAndNoIndex()
    {
        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass Clone"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Water, "Water")
            },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Water))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.False(result.IsValid);
        Assert.Null(result.Index);
        var issues = result.Issues.Where(i => i.Code == TerrainValidationCode.DuplicateTerrainId).ToList();
        Assert.Equal(2, issues.Count);
        Assert.All(issues, issue => Assert.Equal(TerrainCatalogFixture.Grass, issue.TerrainId));
    }

    [Fact]
    public void Validate_MissingStraightSouthRotation_ReturnsWarning()
    {
        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Water, "Water")
            },
            new[]
            {
                TerrainCatalogFixture.Graphic(0, 1, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 3, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass,
                    (TerrainPeer.North, TerrainCatalogFixture.Water),
                    (TerrainPeer.NorthEast, TerrainCatalogFixture.Water),
                    (TerrainPeer.NorthWest, TerrainCatalogFixture.Water),
                    (TerrainPeer.East, TerrainCatalogFixture.Grass),
                    (TerrainPeer.South, TerrainCatalogFixture.Grass),
                    (TerrainPeer.West, TerrainCatalogFixture.Grass),
                    (TerrainPeer.SouthEast, TerrainCatalogFixture.Grass),
                    (TerrainPeer.SouthWest, TerrainCatalogFixture.Grass))),
                TerrainCatalogFixture.Graphic(0, 4, TerrainCatalogFixture.Solid(TerrainCatalogFixture.Water)),
                TerrainCatalogFixture.Graphic(0, 5, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Water))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.True(result.IsValid);
        var waterWarnings = result.Issues
            .Where(issue => issue.Code == TerrainValidationCode.MissingCoveragePattern
                && issue.TerrainId == TerrainCatalogFixture.Grass
                && issue.Message.Contains("against 'Water'"))
            .Select(issue => issue.Message)
            .ToList();
        Assert.Contains(waterWarnings, message => message.Contains("straight south"));
        Assert.DoesNotContain(waterWarnings, message => message.Contains("straight north"));
    }

    [Fact]
    public void Validate_MissingCoveragePatterns_ReturnsWarningsWithoutBlocking()
    {
        var result = TerrainCatalogValidator.Validate(TerrainCatalogFixture.Valid());

        Assert.True(result.IsValid);
        Assert.NotNull(result.Index);
        var warnings = result.Issues.Where(issue => issue.Severity == TerrainValidationSeverity.Warning).ToList();
        Assert.NotEmpty(warnings);
        Assert.All(warnings, issue => Assert.Equal(TerrainValidationCode.MissingCoveragePattern, issue.Code));
    }

    [Fact]
    public void Validate_FullyCoveredCatalog_ReturnsNoIssues()
    {
        var graphics = new List<TerrainGraphicDefinition>();
        var graphicNumber = 0;
        foreach (var center in new[] { TerrainCatalogFixture.Grass, TerrainCatalogFixture.Water })
        {
            graphics.Add(TerrainCatalogFixture.Graphic(0, ++graphicNumber, TerrainCatalogFixture.Solid(center)));
            foreach (var other in new Guid?[] { null, Other(center) })
            {
                foreach (var pattern in TerrainCatalogFixture.CoveragePatterns(center, other))
                {
                    graphics.Add(TerrainCatalogFixture.Graphic(0, ++graphicNumber, pattern));
                }
            }
        }

        var catalog = new TerrainCatalog(
            new[]
            {
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass"),
                TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Water, "Water")
            },
            graphics);

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Index);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Validate_IssuesAreOrderedBySeverityThenCode()
    {
        var catalog = new TerrainCatalog(
            new[] { TerrainCatalogFixture.Terrain(TerrainCatalogFixture.Grass, "Grass") },
            new[]
            {
                TerrainCatalogFixture.Graphic(short.MaxValue + 1, 1, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 0, TerrainCatalogFixture.Pattern(TerrainCatalogFixture.Grass)),
                TerrainCatalogFixture.Graphic(0, 2, TerrainCatalogFixture.Pattern(null))
            });

        var result = TerrainCatalogValidator.Validate(catalog);

        Assert.Equal(
            new[]
            {
                TerrainValidationCode.CenterlessGraphic,
                TerrainValidationCode.InvalidGraphicNumber,
                TerrainValidationCode.InvalidSheetNumber
            },
            result.Issues
                .Where(issue => issue.Severity == TerrainValidationSeverity.Error)
                .Select(issue => issue.Code)
                .ToArray());
    }

    private static Guid Other(Guid center)
        => center == TerrainCatalogFixture.Grass ? TerrainCatalogFixture.Water : TerrainCatalogFixture.Grass;
}
