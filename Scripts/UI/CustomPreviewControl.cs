using System.Collections.Generic;
using System.Linq;
using Godot;
using Goose2Client.Character;

namespace Goose2Client.UI;

public partial class CustomPreviewControl : Control
{
    private readonly Dictionary<CharacterSlot, TextureRect> _layers = new();

    private CharacterSlot? _customSlot;
    private int _customId;
    private int _customPose;
    private int _tintR, _tintG, _tintB, _tintA;
    private Direction _facing;

    public override void _Ready()
    {
        foreach (var slot in CharacterLayout.All)
        {
            if (slot == CharacterSlot.Mount) continue;
            // Initial child order = CharacterLayout.All back-to-front (first child draws bottom); Refresh re-sorts per facing.
            _layers[slot] = new TextureRect
            {
                Name = slot.ToString(),
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                Visible = false,
            };
            AddChild(_layers[slot]);
        }

        GameManager.Instance.CharacterUpdated += OnCharacterUpdated;
        Refresh();
    }

    // CharacterUpdated fires only on appearance changes, so poll for facing changes.
    public override void _Process(double delta)
    {
        var source = GameManager.Instance.CurrentMapManager?.LocalPlayer;
        if (source == null || source.Facing == _facing) return;
        _facing = source.Facing;
        Refresh();
    }

    public override void _ExitTree()
    {
        GameManager.Instance.CharacterUpdated -= OnCharacterUpdated;
        HideAll();
    }

    public void Refresh()
    {
        var source = GameManager.Instance.CurrentMapManager?.LocalPlayer;
        if (source == null)
        {
            HideAll();
            return;
        }
        var facing = source.Facing;
        _facing = facing;

        // Shield/weapon flip in front of/behind the body per facing (mirrors Character.ApplyDrawOrder).
        int i = 0;
        foreach (var layer in _layers.OrderBy(kv => CharacterLayout.SortOrder(kv.Key, facing)).Select(kv => kv.Value))
            MoveChild(layer, i++);

        foreach (var (slot, layer) in _layers)
        {
            bool replaced = slot == _customSlot;
            int id;
            Color tint;
            int state;
            if (replaced)
            {
                id = _customId;
                tint = TintColor();
                state = _customPose;
            }
            else if (!source.TryGetSlotGraphic(slot, out id, out tint))
            {
                HideLayer(layer);
                continue;
            }
            else
            {
                state = source.BodyState;
            }

            if (id <= 0)
            {
                HideLayer(layer);
                continue;
            }

            var path = $"res://Assets/Sprites/{CharacterLayout.TypeFolder(slot)}/{id}/animations.tres";
            if (!ResourceLoader.Exists(path))
            {
                HideLayer(layer);
                continue;
            }

            var frames = GD.Load<SpriteFrames>(path);
            string clip = null;
            foreach (var cand in AnimationNames.Candidates("idle", state, facing))
            {
                if (frames.HasAnimation(cand))
                {
                    clip = cand;
                    break;
                }
            }
            if (clip == null || frames.GetFrameCount(clip) == 0)
            {
                HideLayer(layer);
                continue;
            }

            var tex = frames.GetFrameTexture(clip, 0);
            if (tex == null)
            {
                HideLayer(layer);
                continue;
            }

            layer.Texture = tex;
            layer.Visible = true;
            var (size, pos) = CustomPreviewMetrics.Layout(tex.GetSize(), Size);
            layer.Size = size;
            layer.Position = pos;
            ApplyTint(layer, tint);
        }
    }

    public void SetCustomGraphic(CharacterSlot? slot, int equippedId, int pose)
    {
        _customSlot = slot;
        _customId = equippedId;
        _customPose = pose;
        Refresh();
    }

    public void ClearCustomGraphic() => SetCustomGraphic(null, 0, 0);

    public void SetTint(int r, int g, int b, int a)
    {
        _tintR = r;
        _tintG = g;
        _tintB = b;
        _tintA = a;
        if (_customSlot is not { } slot || !_layers.TryGetValue(slot, out var layer)) return;
        if (a == 0)
        {
            layer.Material = null;
            return;
        }
        if (layer.Material is not ShaderMaterial mat)
            layer.Material = mat = new ShaderMaterial { Shader = TintMaterial.Shader };
        mat.SetShaderParameter("tint", TintColor());
    }

    public void HideAll()
    {
        foreach (var layer in _layers.Values) HideLayer(layer);
    }

    // Alpha is always /255 even though the picker's A maxes at 200.
    private Color TintColor() => new Color(_tintR / 255f, _tintG / 255f, _tintB / 255f, _tintA / 255f);

    private void OnCharacterUpdated(Character.Character c)
    {
        if (c == GameManager.Instance.CurrentMapManager?.LocalPlayer) Refresh();
    }

    private static void ApplyTint(TextureRect layer, Color tint)
    {
        if (tint.A <= 0f)
        {
            layer.Material = null;
            return;
        }
        if (layer.Material is not ShaderMaterial mat)
            layer.Material = mat = new ShaderMaterial { Shader = TintMaterial.Shader };
        mat.SetShaderParameter("tint", tint);
    }

    private static void HideLayer(TextureRect layer)
    {
        layer.Texture = null;
        layer.Visible = false;
        layer.Material = null;
    }
}
