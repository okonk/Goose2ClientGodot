using System;
using System.Collections.Generic;
using Godot;

namespace Goose2Client;

public static class UiScaleLayout
{
    public const string SkipMeta = "ui_scale_skip";

    // Only separation names authored in Scenes/UI tscn files (grep theme_override_constants);
    // extend only with cited occurrences actually found.
    private static readonly StringName[] ConstantNames =
    {
        new("separation"),
        new("h_separation"),
        new("v_separation")
    };

    public sealed record GeomRecord(
        Control C,
        float OLeft, float OTop, float ORight, float OBottom,
        bool ContainerManaged,
        Vector2 MinSize, bool HasMinSize,
        (StringName Name, int Value)[] Constants,
        int PatchL, int PatchT, int PatchR, int PatchB, bool HasPatch);

    // Call exactly once at end of _Ready, after build-time geometry: the snapshot IS the
    // 1x base, so build code must never scale at build time.
    public static List<GeomRecord> Snapshot(Control root)
    {
        var records = new List<GeomRecord>();
        Walk(root, records);
        return records;
    }

    public static void Apply(List<GeomRecord> records, float factor)
    {
        foreach (var rec in records)
        {
            if (!GodotObject.IsInstanceValid(rec.C) || !rec.C.IsInsideTree())
                continue;

            Control c = rec.C;
            if (!rec.ContainerManaged)
            {
                if (factor == 1f)
                {
                    // Raw floats: base geometry is fractional (11.18f row pitch), and any
                    // rounding would break 1x bit-identity.
                    c.OffsetLeft = rec.OLeft;
                    c.OffsetTop = rec.OTop;
                    c.OffsetRight = rec.ORight;
                    c.OffsetBottom = rec.OBottom;
                }
                else
                {
                    // Offsets are coordinates: 0 must stay 0, so no min-1 floor here.
                    c.OffsetLeft = (float)MathF.Round(rec.OLeft * factor, MidpointRounding.AwayFromZero);
                    c.OffsetTop = (float)MathF.Round(rec.OTop * factor, MidpointRounding.AwayFromZero);
                    c.OffsetRight = (float)MathF.Round(rec.ORight * factor, MidpointRounding.AwayFromZero);
                    c.OffsetBottom = (float)MathF.Round(rec.OBottom * factor, MidpointRounding.AwayFromZero);
                }
            }

            if (rec.HasMinSize)
            {
                c.CustomMinimumSize = factor == 1f ? rec.MinSize
                    : new Vector2(
                        (float)MathF.Round(rec.MinSize.X * factor, MidpointRounding.AwayFromZero),
                        (float)MathF.Round(rec.MinSize.Y * factor, MidpointRounding.AwayFromZero));
            }

            if (rec.Constants.Length > 0)
            {
                foreach (var (name, value) in rec.Constants)
                    c.AddThemeConstantOverride(name, factor == 1f ? value
                        : (int)MathF.Round(value * factor, MidpointRounding.AwayFromZero));
            }

            if (rec.HasPatch)
            {
                var npr = (NinePatchRect)c;
                npr.PatchMarginLeft = factor == 1f ? rec.PatchL : (int)MathF.Round(rec.PatchL * factor, MidpointRounding.AwayFromZero);
                npr.PatchMarginTop = factor == 1f ? rec.PatchT : (int)MathF.Round(rec.PatchT * factor, MidpointRounding.AwayFromZero);
                npr.PatchMarginRight = factor == 1f ? rec.PatchR : (int)MathF.Round(rec.PatchR * factor, MidpointRounding.AwayFromZero);
                npr.PatchMarginBottom = factor == 1f ? rec.PatchB : (int)MathF.Round(rec.PatchB * factor, MidpointRounding.AwayFromZero);
            }
        }
    }

    private static void Walk(Node n, List<GeomRecord> sink)
    {
        if (n.HasMeta(SkipMeta) && n.GetMeta(SkipMeta).AsBool())
            return;

        if (n is Control c)
            sink.Add(MakeRecord(c));

        foreach (Node child in n.GetChildren())
            Walk(child, sink);
    }

    private static GeomRecord MakeRecord(Control c)
    {
        Vector2 min = c.CustomMinimumSize;
        var constants = new List<(StringName, int)>();
        foreach (StringName name in ConstantNames)
        {
            // Authored overrides only: GetThemeConstant falls back through theme defaults,
            // so capturing effective values would invent then scale unauthored geometry.
            if (!c.HasThemeConstantOverride(name))
                continue;

            int value = c.GetThemeConstant(name);
            if (value == 0)
                continue;

            constants.Add((name, value));
        }

        // Container children: the container owns their offsets (its layout pass can run
        // after the snapshot), and scaling rides on min-size/separation/font minimums.
        // Nine-patch edge art is source pixels, not stretch: the margins must scale with the
        // factor or the border stays 1x-thick on a scaled window.
        var npr = c as NinePatchRect;
        bool hasPatch = npr != null
            && (npr.PatchMarginLeft != 0 || npr.PatchMarginTop != 0 || npr.PatchMarginRight != 0 || npr.PatchMarginBottom != 0);
        return new GeomRecord(
            c,
            c.OffsetLeft, c.OffsetTop, c.OffsetRight, c.OffsetBottom,
            c.GetParent() is Container,
            min, min != Vector2.Zero,
            constants.ToArray(),
            hasPatch ? npr.PatchMarginLeft : 0,
            hasPatch ? npr.PatchMarginTop : 0,
            hasPatch ? npr.PatchMarginRight : 0,
            hasPatch ? npr.PatchMarginBottom : 0,
            hasPatch);
    }
}
