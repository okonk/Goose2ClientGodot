using System;
using Godot;

namespace Goose2Client.UI;

public partial class CustomWindowSlot : Panel
{
    private TextureRect _icon;

    public ItemStats Stats { get; private set; }
    public bool HasItem => Stats != null;
    public int SlotId { get; set; }
    public Action<Godot.Collections.Dictionary> OnDrop { get; set; }

    public override void _Ready()
    {
        _icon = GetNode<TextureRect>("Icon");
    }

    public void SetItem(ItemStats stats)
    {
        Stats = stats;
        Icon.Apply(_icon, stats.GraphicFile, stats.GraphicId, stats.GraphicR, stats.GraphicG, stats.GraphicB, stats.GraphicA);
    }

    public void ClearItem()
    {
        Stats = null;
        Icon.Clear(_icon);
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (!HasItem) return default;

        var preview = new TextureRect
        {
            Texture = _icon.Texture,
            CustomMinimumSize = _icon.Size,
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspect
        };
        SetDragPreview(preview);

        return new Godot.Collections.Dictionary
        {
            { "kind", "item" },
            { "slot", this }
        };
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Dictionary) return false;
        var d = data.AsGodotDictionary();
        return d.ContainsKey("kind") && d["kind"].AsString() == "item";
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        var d = data.AsGodotDictionary();
        if (d.ContainsKey("kind") && d["kind"].AsString() == "item")
            OnDrop?.Invoke(d);
    }
}
