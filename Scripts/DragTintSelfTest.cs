using System;
using System.Collections.Generic;
using Godot;
using Goose2Client.UI;

namespace Goose2Client;

internal static class DragTintSelfTest
{
    private static readonly string[] SlotScenes =
    {
        "res://Scenes/UI/ItemSlot.tscn",
        "res://Scenes/UI/HotbarSlot.tscn",
        "res://Scenes/UI/SpellSlot.tscn",
        "res://Scenes/UI/CustomWindow.tscn"
    };

    public static async System.Threading.Tasks.Task Run(GameManager gm)
    {
        // In-product gate behind a project arg; production with no arg never reaches this.
        await gm.ToSignal(gm.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool failed = false;
        try
        {
            await SelfTestBody(gm);
            GD.Print("[drag_tint_selftest] PASS");
        }
        catch (Exception e)
        {
            failed = true;
            GD.PrintErr($"ERR_drag_tint_selftest: {e.Message}");
        }
        gm.GetTree().Quit(failed ? 1 : 0);
    }

    private static async System.Threading.Tasks.Task SelfTestBody(GameManager gm)
    {
        var tree = gm.GetTree();
        var tint = new Color(0.8f, 0.4f, 0.2f, 0.6f);

        var img = Image.CreateEmpty(4, 4, false, Image.Format.Rgba8);
        img.Fill(new Color(0.2f, 0.6f, 0.9f));
        var texture = ImageTexture.CreateFromImage(img);

        void Assert([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool cond, string msg)
        {
            if (!cond) throw new InvalidOperationException(msg);
        }

        // The engine keeps no public handle on the live drag preview, so find the node the
        // slot published by its material. Skip the icon's own branch: it carries the same
        // material and, while attached, would otherwise always match first.
        TextureRect? FindPreview(Node node, Node skipBranch, Material mark)
        {
            if (ReferenceEquals(node, skipBranch)) return null;
            if (node is TextureRect rect && ReferenceEquals(rect.Material, mark)) return rect;
            foreach (var child in node.GetChildren())
            {
                var found = FindPreview(child, skipBranch, mark);
                if (found != null) return found;
            }
            return null;
        }

        HashSet<ulong> Subtree(Node node)
        {
            var ids = new HashSet<ulong>();
            void Walk(Node n)
            {
                ids.Add(n.GetInstanceId());
                foreach (var child in n.GetChildren()) Walk(child);
            }
            Walk(node);
            return ids;
        }

        var host = new Node { Name = "DragTintSelfTest" };
        gm.AddChild(host);

        foreach (var path in SlotScenes)
        {
            var packed = GD.Load<PackedScene>(path);
            Assert(packed != null, $"{path} did not load");

            var node = packed.Instantiate();
            host.AddChild(node);
            await gm.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

            var slot = FindSlot(node);
            Assert(slot != null, $"{path} has no draggable slot");

            var viewport = slot.GetViewport();
            Assert(viewport != null, $"{path}: the slot is not in a viewport");

            // Fill through the slot's own API so its "is there anything to drag" gate opens,
            // then swap in a self-contained texture: the tint is what this test is about.
            var stats = new ItemStats { GraphicFile = 1, GraphicId = 100, StackSize = 1 };
            switch (slot)
            {
                case ItemSlot item: item.SetItem(stats); break;
                case HotbarSlot hotbar: hotbar.SetItem(stats); break;
                case CustomWindowSlot custom: custom.SetItem(stats); break;
                case SpellSlot spell:
                    spell.SetSpell(new SpellInfo { Name = "DragTintSelftest", GraphicFile = 1, GraphicId = 100 });
                    break;
            }

            var icon = slot.GetNode<TextureRect>("Icon");
            icon.Texture = texture;
            TintMaterial.Apply(icon, texture, tint);
            var source = icon.Material;
            Assert(source is ShaderMaterial, $"{path}: the icon tint did not apply");

            // SetDragPreview only does anything while a drag is live, and the engine parents the
            // preview into the window that owns the slot (not the slot), so snapshot the whole
            // viewport and recognise the published preview as a node that was not there before.
            slot.ForceDrag("drag-tint-selftest", new Control());
            Assert(viewport.GuiIsDragging(), $"{path}: the forced drag did not start");
            var before = Subtree(viewport);

            var data = slot._GetDragData(Vector2.Zero);
            Assert(data.VariantType == Variant.Type.Dictionary, $"{path}: no drag data");
            var kind = data.AsGodotDictionary()["kind"].AsString();
            Assert(kind is "item" or "spell" or "hotbar", $"{path}: unexpected drag kind '{kind}'");

            var preview = FindPreview(viewport, icon, source);
            Assert(preview != null, $"{path}: the drag preview never reached the tree");
            Assert(!before.Contains(preview.GetInstanceId()), $"{path}: the preview was not published by the drag");
            Assert(preview.Texture == texture, $"{path}: the preview lost the icon texture");
            Assert(preview.Material is ShaderMaterial, $"{path}: the preview lost the icon tint material");
            var mat = (ShaderMaterial)preview.Material;
            Assert((Color)mat.GetShaderParameter("tint") == tint, $"{path}: the preview tint colour changed");
            Assert(Mathf.IsEqualApprox((float)mat.GetShaderParameter("mean_lum"), TintMaterial.MeanLuminance(texture)),
                $"{path}: the preview shading parameter changed");

            viewport.GuiCancelDrag();
            node.QueueFree();
            await gm.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }

        host.QueueFree();
    }

    private static Control? FindSlot(Node node)
    {
        if (node is ItemSlot or HotbarSlot or SpellSlot or CustomWindowSlot)
            return (Control)node;

        foreach (var child in node.GetChildren())
        {
            var found = FindSlot(child);
            if (found != null) return found;
        }

        return null;
    }
}
