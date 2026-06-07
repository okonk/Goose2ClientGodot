using Godot;
using Goose2Client;
using Goose2Client.Character;

namespace Goose2Client.UI;

public partial class VitalsCharacterDisplay : Control
{
    public override void _Ready()
    {
        GameManager.Instance.CharacterUpdated += OnCharacterUpdated;
        Refresh();
    }

    public override void _ExitTree() => GameManager.Instance.CharacterUpdated -= OnCharacterUpdated;

    private void OnCharacterUpdated(Character.Character c)
    {
        if (c == GameManager.Instance.CurrentMapManager?.LocalPlayer) Refresh();
    }

    public void Refresh()
    {
        var localPlayer = GameManager.Instance.CurrentMapManager?.LocalPlayer;
        if (localPlayer == null) { HideAll(); return; }
        var a = localPlayer.GetAppearance();

        // Monster bodies (Unity: id >= 100) are shown centered; humanoids drop so the head/upper
        // body fills the circle.
        SetLayer("Mask/Body", "Bodies", a.BodyId, a.BodyColor, a.IsMonster ? 0f : HeadDropPixels);
        if (a.IsMonster) { ClearLayer("Mask/Hair"); ClearLayer("Mask/Eyes"); ClearLayer("Mask/Chest"); ClearLayer("Mask/Helmet"); return; }
        SetLayer("Mask/Hair", "Hair", a.HairId, a.HairColor);
        SetLayer("Mask/Eyes", "Eyes", a.FaceId, new Color(0,0,0,0));
        SetLayer("Mask/Chest", "Chest", a.ChestId, a.ChestColor);
        SetLayer("Mask/Helmet", "Helms", a.HelmId, a.HelmColor);
    }

    private void HideAll()
    {
        ClearLayer("Mask/Body");
        ClearLayer("Mask/Hair");
        ClearLayer("Mask/Eyes");
        ClearLayer("Mask/Chest");
        ClearLayer("Mask/Helmet");
    }

    // Unity VitalsCharacterDisplay: sizeDelta = frame * 1.25, localPosition.y = -20 (humanoids).
    private const float PortraitZoom = 1.25f;
    private const float PortraitSize = 53f;       // Mask is 53x53 (matches VitalsWindow.tscn).
    private const float HeadDropPixels = 20f;     // push the figure down so the head frames in the circle.

    private void SetLayer(string nodePath, string folder, int graphicId, Color tint, float dropPixels = HeadDropPixels)
    {
        var rect = GetNode<TextureRect>(nodePath);
        if (graphicId <= 0) { ClearLayer(nodePath); return; }
        var path = $"res://Assets/Sprites/{folder}/{graphicId}/animations.tres";
        if (!ResourceLoader.Exists(path)) { ClearLayer(nodePath); return; }
        var frames = GD.Load<SpriteFrames>(path);
        string anim = frames.HasAnimation("idle-down") ? "idle-down"
                    : frames.HasAnimation("idle") ? "idle" : null;
        if (anim == null || frames.GetFrameCount(anim) == 0) { ClearLayer(nodePath); return; }
        var tex = frames.GetFrameTexture(anim, 0);
        rect.Texture = tex;
        rect.Visible = true;
        rect.TextureFilter = CanvasItem.TextureFilterEnum.Nearest;

        // Draw at native size * zoom (preserve aspect — never stretch to the square), centered
        // horizontally on the circle and dropped so the head/upper body fills it. The Mask node's
        // clip_children crops the overflow to the circle, mirroring Unity's UI Mask. Layers are
        // center-registered (like the in-world character), so a common center keeps them aligned.
        var size = tex.GetSize() * PortraitZoom;
        rect.Size = size;
        rect.Position = new Vector2(
            PortraitSize / 2f - size.X / 2f,
            PortraitSize / 2f + dropPixels - size.Y / 2f);

        ApplyTint(rect, tint);
    }

    private void ClearLayer(string nodePath)
    {
        var rect = GetNode<TextureRect>(nodePath);
        rect.Texture = null;
        rect.Visible = false;
        rect.Material = null;
    }

    private void ApplyTint(CanvasItem item, Color tint)
    {
        if (tint.A <= 0f)
        {
            item.Material = null;
        }
        else
        {
            if (item.Material is not ShaderMaterial mat)
                item.Material = mat = new ShaderMaterial();
            mat.Shader ??= TintShader;
            mat.SetShaderParameter("tint", tint);
        }
    }

    // Faithful to Character.cs and Icon.cs — tint.a is a BLEND factor, not opacity.
    private static Shader _tintShader;
    private static Shader TintShader => _tintShader ??= new Shader()
    {
        Code = @"shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.0);
void fragment() { vec4 t = texture(TEXTURE, UV); COLOR = vec4(mix(t.rgb, tint.rgb, tint.a), t.a) * COLOR; }"
    };
}
