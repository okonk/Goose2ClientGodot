using Godot;

namespace Goose2Client;

/// <summary>Spell targeting reticle — positioned at the target character's feet.</summary>
public partial class SpellTarget : Node2D
{
    private Sprite2D _reticle;
    
    public override void _Ready()
    {
        _reticle = new Sprite2D
        {
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            ZIndex = 30,
            Name = "ReticleSprite",
            // Simple diamond-shaped reticle (no external texture needed)
            Texture = CreateReticleTexture(),
        };
        AddChild(_reticle);
    }

    /// <summary>Create a minimal diamond reticle texture (no external asset needed).</summary>
    private static ImageTexture CreateReticleTexture()
    {
        var img = Image.Create(16, 16, false, Image.Format.Rgba8);
        img.Fill(new Color(0, 0, 0, 0));
        // Draw a diamond outline
        var c = new Color(1f, 1f, 0.4f, 1f);
        int[] dx = { 0, 1, 2, 3, 4, 5, 6, 7 };
        int[] dy = { 7, 6, 5, 4, 3, 2, 1, 0 };
        for (int i = 0; i < dx.Length; i++)
        {
            int x = dx[i], y = dy[i];
            img.SetPixel(7 - x, y, c); img.SetPixel(7 + x, y, c);
            img.SetPixel(7 - x, 15 - y, c); img.SetPixel(7 + x, 15 - y, c);
        }
        return ImageTexture.CreateFromImage(img);
    }
    
    /// <summary>Size the reticle to match the target character's dimensions.</summary>
    public void ResizeTarget(int characterHeight)
    {
        if (characterHeight <= 0) characterHeight = 64;
        int h = characterHeight / 32; // scale factor
        if (h < 1) h = 1;
        int w = (int)System.Math.Max(1, h * 0.75f);
        _reticle.Scale = new Vector2(w, h);
        // Position offset so reticle sits under character feet
        int yOffset = (h - 1) * 16; // half the scaled height offset
        Position = new Vector2(0, -yOffset);
    }
}
