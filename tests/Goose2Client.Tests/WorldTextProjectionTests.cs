// tests/Goose2Client.Tests/WorldTextProjectionTests.cs — global namespace (repo convention, cf. WorldViewportScaleTests.cs)
using Godot;
using Goose2Client;
using Xunit;

public class WorldTextProjectionTests
{
    [Fact]
    public void Project_Identity()  // canvas identity, S=1, origin (0,0) → input unchanged
        => Assert.Equal(new Vector2(3, -7), WorldTextProjection.Project(new Vector2(3, -7), Transform2D.Identity, 1f, new Vector2I(0, 0)));

    [Fact]
    public void Project_CameraOffset()  // camera at viewport (500,300), S=2: world → 2× viewport offset
    {
        // C# API: Translated/Rotated/Scaled (the GDScript With* methods do not exist in GodotSharp).
        var canvas = Transform2D.Identity.Translated(new Vector2(500, 300));
        Assert.Equal(new Vector2(1000, 600), WorldTextProjection.Project(Vector2.Zero, canvas, 2f, new Vector2I(0, 0)));
        Assert.Equal(new Vector2(1020, 620), WorldTextProjection.Project(new Vector2(10, 10), canvas, 2f, new Vector2I(0, 0)));
    }

    [Fact]
    public void Project_FractionalCamera()  // camera lerp is fractional
        => Assert.Equal(new Vector2(201f, 100.5f), WorldTextProjection.Project(Vector2.Zero,
            Transform2D.Identity.Translated(new Vector2(100.5f, 50.25f)), 2f, new Vector2I(0, 0)));

    [Fact]
    public void Project_OriginOffset()  // display rect offset (odd-window gutter)
        => Assert.Equal(new Vector2(31, 30), WorldTextProjection.Project(new Vector2(10, 10), Transform2D.Identity, 3f, new Vector2I(1, 0)));

    [Fact]
    public void RoundTrip_ProjectThenWindowToWorld_ReturnsInput()
    {
        // ADVERSARIAL — mirrors WorldViewport.WindowToWorld's math verbatim (WorldViewport.cs:158-165):
        //   w2 = canvas.AffineInverse() * ((p - origin) / S)
        // Fails on any sign/order slip between the forward and inverse transforms.
        var canvas = Transform2D.Identity
            .Rotated(Mathf.DegToRad(90)).Scaled(new Vector2(1.5f, 1.5f)).Translated(new Vector2(321.25f, -88f));
        foreach (float s in new[] { 1f, 2f, 3f })
            foreach (var origin in new[] { new Vector2I(0, 0), new Vector2I(1, 0), new Vector2I(13, 7) })
                foreach (var w in new[] { Vector2.Zero, new Vector2(10, 10), new Vector2(-450.5f, 720.25f), new Vector2(12345, -6789) })
                {
                    var p = WorldTextProjection.Project(w, canvas, s, origin);
                    var w2 = canvas.AffineInverse() * ((p - new Vector2((float)origin.X, (float)origin.Y)) / s);
                    // 0.01f tolerance: float precision at ~10⁴ magnitudes (IsEqualApprox's 1e-6 default is too tight).
                    Assert.True(w2.DistanceTo(w) < 0.01f, $"round trip {w} → {p} → {w2} (s={s}, origin={origin})");
                }
    }

    [Fact]
    public void IsCulled_Inside_False()
        => Assert.False(WorldTextProjection.IsCulled(new Rect2(100, 100, 50, 50), new Rect2(0, 0, 1920, 1080)));

    [Fact]
    public void IsCulled_FlushInsideEdge_False()  // interior inside, right edge flush with display right → NOT culled (probed semantics)
        => Assert.False(WorldTextProjection.IsCulled(new Rect2(1870, 100, 50, 50), new Rect2(0, 0, 1920, 1080)));

    [Fact]
    public void IsCulled_PastEachEdge_True()  // 4 edges, fully outside
    {
        var display = new Rect2(0, 0, 1920, 1080);
        Assert.True(WorldTextProjection.IsCulled(new Rect2(-50, 100, 50, 50), display));    // left
        Assert.True(WorldTextProjection.IsCulled(new Rect2(1920, 100, 50, 50), display));   // right
        Assert.True(WorldTextProjection.IsCulled(new Rect2(100, -50, 50, 50), display));    // top
        Assert.True(WorldTextProjection.IsCulled(new Rect2(100, 1080, 50, 50), display));   // bottom
    }

    [Fact]
    public void IsCulled_OutsideTouchingEdge_True()  // entirely outside, touching the edge → no interior overlap → culled
        => Assert.True(WorldTextProjection.IsCulled(new Rect2(1920, 100, 50, 50), new Rect2(0, 0, 1920, 1080)));
}
