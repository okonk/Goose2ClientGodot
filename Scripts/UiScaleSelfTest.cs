using System.Collections.Generic;
using Godot;
using Goose2Client.Network.Packets;
using Goose2Client.UI;

namespace Goose2Client;

internal static class UiScaleSelfTest
{
    public static async System.Threading.Tasks.Task Run(GameManager gm)
    {
        // In-product gate behind a project arg; production with no arg never reaches this.
        await gm.ToSignal(gm.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool failed = false;
        try
        {
            await SelfTestBody(gm);
            GD.Print("[ui_scale_selftest] PASS");
        }
        catch (System.Exception e)
        {
            failed = true;
            GD.PrintErr($"ERR_ui_scale_selftest: {e.Message}");
        }
        gm.GetTree().Quit(failed ? 1 : 0);
    }

    private static async System.Threading.Tasks.Task SelfTestBody(GameManager gm)
    {
        var tree = gm.GetTree();
        var applier = UiScaleApplier.Instance;
        int fontChecked = 0;
        var authored1 = new Dictionary<Control, bool>();

        async System.Threading.Tasks.Task Frame() => await gm.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        static (int, int, int, int) Patch(NinePatchRect n)
            => (n.PatchMarginLeft, n.PatchMarginTop, n.PatchMarginRight, n.PatchMarginBottom);

        void Assert(bool cond, string msg)
        {
            if (!cond)
                throw new System.InvalidOperationException(msg);
        }

        // Mirrors UiScaleLayout's walk: skip nodes are outside the snapshot model
        // (dynamic tooltips mutate their own size at runtime).
        void Walk(Control n, Dictionary<Control, Vector4> geo, Dictionary<Control, bool> authored)
        {
            if (n.HasMeta(UiScaleLayout.SkipMeta) && n.GetMeta(UiScaleLayout.SkipMeta).AsBool())
                return;
            geo[n] = new Vector4(n.OffsetLeft, n.OffsetTop, n.OffsetRight, n.OffsetBottom);
            authored[n] = n.HasThemeConstantOverride("separation")
                || n.HasThemeConstantOverride("h_separation")
                || n.HasThemeConstantOverride("v_separation");
            foreach (var child in n.GetChildren())
                if (child is Control c)
                    Walk(c, geo, authored);
        }

        void AuditFonts(Node n)
        {
            if (n is Control c)
            {
                foreach (var prop in new[] { new StringName("font_size"), new StringName("normal_font_size") })
                {
                    if (!c.HasThemeFontSizeOverride(prop))
                        continue;
                    Assert(applier.TryGetFontBase(c, prop, out float basePx),
                        $"font override outside registry: {c.GetPath()}");
                    Assert(c.GetThemeFontSize(prop) == applier.ScaleSize(basePx),
                        $"font {c.GetPath()}: {c.GetThemeFontSize(prop)} != {basePx} x 2");
                    fontChecked++;
                }
                if (authored1.TryGetValue(c, out bool hadAuthored) && !hadAuthored)
                    Assert(!c.HasThemeConstantOverride("separation")
                        && !c.HasThemeConstantOverride("h_separation")
                        && !c.HasThemeConstantOverride("v_separation"),
                        $"constants materialized on unauthored control: {c.GetPath()}");
            }
            foreach (var child in n.GetChildren())
                AuditFonts(child);
        }

        bool ContainsRoot(Node w)
        {
            foreach (var r in applier.RegisteredWindows)
                if (r.Window == w) return true;
            return false;
        }

        var canvas = (Vector2I)tree.Root.GetVisibleRect().Size;
        GD.Print($"[ui_scale_selftest] canvas={canvas.X}x{canvas.Y}");
        Assert(canvas.X > 0 && canvas.Y > 0, $"degenerate headless canvas {canvas}");

        // The selftest profile persists across runs; the explicit 1f apply keeps the
        // baseline independent of any persisted manual factor and of the canvas size.
        gm.LoadSettings("ui-scale-selftest");
        applier.Apply(1f, ApplyReason.Startup);
        gm.EnsureHud();
        await Frame();

        // Step 1: 1x no-op — Relayout at factor 1 must leave every registered root bit-identical.
        await Frame();
        var geo1 = new Dictionary<Control, Vector4>();
        var pos1 = new Dictionary<BaseWindow, Vector2>();
        foreach (var r in applier.RegisteredWindows)
        {
            Walk(r.ControlRef, geo1, authored1);
            if (r.Window is BaseWindow bw)
                pos1[bw] = bw.Position;
        }
        // A BaseWindow snapshot owns the root's offsets, so Relayout alone reverts the
        // window's placed position — mirror applier.Apply's Relayout+RepositionFromSaved pair.
        foreach (var r in applier.RegisteredWindows)
        {
            r.Window.Relayout();
            if (r.Window is BaseWindow bw)
                bw.RepositionFromSaved();
        }
        await Frame();
        var geo2 = new Dictionary<Control, Vector4>();
        var auth2 = new Dictionary<Control, bool>();
        foreach (var r in applier.RegisteredWindows)
            Walk(r.ControlRef, geo2, auth2);
        Assert(geo2.Count == geo1.Count, "1x no-op: control count changed");
        foreach (var kv in geo1)
            Assert(geo2.TryGetValue(kv.Key, out var v) && v == kv.Value,
                $"1x no-op: {kv.Key.GetPath()} {kv.Value} -> {v}");
        GD.Print($"[ui_scale_selftest] OK 1x no-op: {applier.RegisteredWindows.Count} roots, {geo1.Count} controls");

        // Leg 0: login 1x baseline — the current scene stays Login the whole run.
        var login = (LoginScene)tree.CurrentScene;
        var loginMargin = login.GetNode<MarginContainer>("MarginContainer");
        var loginVBox = login.GetNode<VBoxContainer>("MarginContainer/VBox");
        var nameInput = login.GetNode<LineEdit>("MarginContainer/VBox/NameInput");
        Assert(loginMargin.Size == new Vector2(300, 200), $"login margin size {loginMargin.Size} != (300, 200)");
        // Project-theme base is 10; a different value means the project theme stopped applying.
        var loginFont1 = nameInput.GetThemeFontSize("font_size");
        var loginSep1 = loginVBox.GetThemeConstant("separation");
        Assert(loginFont1 == 10, $"login font base {loginFont1} != 10");
        Assert(loginSep1 == 10, $"login separation {loginSep1} != 10");
        GD.Print("[ui_scale_selftest] OK login 1x baseline: 300x200, font 10, separation 10");

        applier.Apply(2f, ApplyReason.UserCommit);
        await Frame();

        Assert(applier.Theme.GetDefaultFontSize() == 20, $"theme default font {applier.Theme.GetDefaultFontSize()} != 20");
        AuditFonts(gm.UiLayer);

        // The migrated set is pinned explicitly so the walk can't pass vacuously.
        void CheckFont(Control c, StringName prop, float basePx)
        {
            Assert(applier.TryGetFontBase(c, prop, out float actual), $"font base not registered: {c.GetPath()} ({prop})");
            Assert(actual == basePx, $"font base {c.GetPath()} {actual} != {basePx}");
            Assert(c.GetThemeFontSize(prop) == applier.ScaleSize(basePx), $"font {c.GetPath()} {c.GetThemeFontSize(prop)} != {applier.ScaleSize(basePx)}");
        }
        CheckFont(gm.Hud.Chat.GetNode<RichTextLabel>("ChatLog"), new StringName("normal_font_size"), 12f);
        CheckFont(gm.Hud.Chat.GetNode<LineEdit>("Input"), new StringName("font_size"), 12f);
        CheckFont(gm.Hud.Debug.GetNode<Label>("FpsText"), new StringName("font_size"), 12f);
        CheckFont(gm.Hud.Debug.GetNode<Label>("VersionText"), new StringName("font_size"), 12f);
        CheckFont(gm.Hud.Bank.GetNode<Label>("TitleBar/TitleLabel"), new StringName("font_size"), 9f);
        CheckFont(gm.Hud.Vendor.GetNode<Label>("TitleBar/TitleLabel"), new StringName("font_size"), 10f);

        Assert(gm.Hud.Vitals.Size == new Vector2(366, 110), $"vitals size {gm.Hud.Vitals.Size} != (366, 110)");
        Assert(gm.Hud.Vitals.Position == new Vector2(16, 16), $"vitals pos {gm.Hud.Vitals.Position} != (16, 16)");
        var slot = (ItemSlot)gm.Hud.Inventory.GetNode<GridContainer>("Content/SlotGrid").GetChild(0);
        Assert(slot.CustomMinimumSize == new Vector2(64, 64), $"item slot min {slot.CustomMinimumSize} != (64, 64)");
        Assert(gm.Hud.Chat.OffsetTop == -426 && gm.Hud.Chat.OffsetBottom == -10,
            $"chat offsets top={gm.Hud.Chat.OffsetTop} bottom={gm.Hud.Chat.OffsetBottom} != (-426, -10)");

        var vendor = gm.Hud.Vendor;
        Assert(vendor.Position.X >= 0f && vendor.Position.X <= Mathf.Max(0f, canvas.X - vendor.Size.X),
            $"vendor x postcondition {vendor.Position}");
        Assert(vendor.Position.Y >= 0f && vendor.Position.Y <= Mathf.Max(0f, canvas.Y - applier.ScaleSize(24f)),
            $"vendor y postcondition {vendor.Position}");

        var memberList = gm.Hud.Party.GetNode<VBoxContainer>("MemberList");
        Assert(memberList.GetChildCount() == 8, $"party tiles {memberList.GetChildCount()} != 8");
        var sep = memberList.GetThemeConstant("separation");
        Assert(sep == 2, $"party separation {sep} != 2");
        var tile = (PartyMember)memberList.GetChild(0);
        Assert(tile.CustomMinimumSize == new Vector2(174, 66), $"party tile min {tile.CustomMinimumSize} != (174, 66)");
        var nameOffset = tile.GetNode<Label>("Content/NameText").OffsetBottom;
        Assert(nameOffset == 22, $"party name offset {nameOffset} != 22");

        var stamp = gm.GetNode<Label>("BuildStampOverlay/BuildIdLabel");
        var stampFont = stamp.GetThemeFontSize("font_size");
        Assert(stampFont == 10, $"build stamp font {stampFont} != 10");

        var tm = TooltipManager.Instance;
        Assert(tm != null, "tooltip manager missing");
        Assert(!tm.GetNode<ItemTooltipControl>("ItemTooltip").Visible
            && !tm.GetNode<SpellTooltipControl>("SpellTooltip").Visible
            && !tm.GetNode<TextTooltipControl>("TextTooltip").Visible
            && !tm.GetNode<MapItemTooltipControl>("MapItemTooltip").Visible, "tooltips not all hidden");

        tm.ShowSpellTooltip(new SpellInfo { Name = "Selftest" }, gm.Hud);
        await Frame();
        await Frame();
        var spellTt = tm.GetNode<SpellTooltipControl>("SpellTooltip");
        var spellLabel = spellTt.GetNode<Label>("Label");
        var labelMin = spellLabel.GetCombinedMinimumSize();
        var expectedSpell = labelMin + new Vector2(applier.ScaleSize(8f), applier.ScaleSize(4f));
        Assert(spellTt.Visible, "spell tooltip not visible after show");
        Assert(spellTt.Size == expectedSpell, $"spell tooltip size {spellTt.Size} != {expectedSpell}");
        Assert(spellLabel.OffsetLeft == 8 && spellLabel.OffsetTop == 4
            && spellLabel.OffsetRight == -8 && spellLabel.OffsetBottom == -4,
            $"spell label 2x offsets {spellLabel.OffsetLeft},{spellLabel.OffsetTop},{spellLabel.OffsetRight},{spellLabel.OffsetBottom} != (8,4,-8,-4)");
        Assert(spellTt.Position.Y + spellTt.Size.Y <= canvas.Y, $"spell tooltip y-clamp {spellTt.Position.Y}+{spellTt.Size.Y} > {canvas.Y}");
        tm.HideSpellTooltip();
        await Frame();
        var vitals = gm.Hud.Vitals;
        foreach (var barName in new[] { "HpBar", "MpBar", "SpBar" })
            Assert(vitals.GetNode<TextureProgressBar>(barName).NinePatchStretch,
                $"vitals {barName} nine_patch_stretch off");
        var hotbar = gm.Hud.Hotbar;
        Assert(hotbar.GetNode<TextureProgressBar>("Content/XpBar").NinePatchStretch,
            "hotbar XpBar nine_patch_stretch off");
        var partyMember = GD.Load<PackedScene>("res://Scenes/UI/PartyMember.tscn").Instantiate<Control>();
        gm.Hud.AddChild(partyMember);
        foreach (var barName in new[] { "HpBar", "MpBar" })
            Assert(partyMember.GetNode<TextureProgressBar>($"Content/{barName}").NinePatchStretch,
                $"party {barName} nine_patch_stretch off");
        partyMember.QueueFree();
        var hotSlot = hotbar.GetNode<Control>("Content/Pages/Page0").GetChild(0);
        Assert(hotSlot.GetNode<TextureProgressBar>("CooldownOverlay").NinePatchStretch,
            "hotbar slot cooldown nine_patch_stretch off");
        var hotbarBg = hotbar.GetNode<NinePatchRect>("Background");
        Assert(Patch(hotbarBg) == (6, 6, 6, 6),
            $"hotbar bg patch margin 2x {Patch(hotbarBg)} != (6,6,6,6)");
        Assert(hotbar.Position == WindowPlacement.HotbarDefault(canvas, hotbar.Size, 2f,
            DefaultWindowLayout.For("Hotbar"), hotbar.Size / 2f),
            $"hotbar 2x position {hotbar.Position} != HotbarDefault");
        GD.Print($"[ui_scale_selftest] OK 2x audit: theme=20, {fontChecked} font overrides, spell tooltip {expectedSpell}, 8 nine-patch bars");

        // Leg 1: login at 2x.
        Assert(loginMargin.Size == new Vector2(600, 400), $"login margin size 2x {loginMargin.Size} != (600, 400)");
        var loginFont2 = nameInput.GetThemeFontSize("font_size");
        var loginSep2 = loginVBox.GetThemeConstant("separation");
        Assert(loginFont2 == 20, $"login font 2x {loginFont2} != 20");
        Assert(loginSep2 == 20, $"login separation 2x {loginSep2} != 20");
        GD.Print("[ui_scale_selftest] OK login 2x: 600x400, font 20, separation 20");

        // Step 2b: runtime spawn at 2x. WindowSettings is a class mutated in place — the
        // record is deep-copied into locals, and restore runs in finally before anything that can throw.
        var cs = gm.CharacterSettings;
        bool bHad = false; Vector2 bPos = default; bool bVis = false; Vector2I bCanvas = default; Vector2 bSize = default; float bFactor = 0f; bool bPlaced = false;
        {
            var ws = cs.GetWindowSettings("Bank");
            bHad = ws != null;
            if (ws != null)
            { bPos = ws.Position; bVis = ws.Visible; bCanvas = ws.CanvasSize; bSize = ws.Size; bFactor = ws.Factor; bPlaced = ws.Placed; }
        }
        BankWindow bank = null;
        try
        {
            cs.WindowSettings.Remove("Bank");
            cs.Save();
            bank = GD.Load<PackedScene>("res://Scenes/UI/BankWindow.tscn").Instantiate<BankWindow>();
            gm.UiLayer.AddChild(bank);
            await Frame();
            var grid = bank.GetNode<GridContainer>("Content/SlotGrid");
            Assert(grid.OffsetLeft == 4 && grid.OffsetTop == 40 && grid.OffsetRight == 332 && grid.OffsetBottom == 434,
                $"bank slot grid 2x offsets {grid.OffsetLeft},{grid.OffsetTop},{grid.OffsetRight},{grid.OffsetBottom} != (4,40,332,434)");
        }
        finally
        {
            if (bHad)
            {
                var r = cs.WindowSettings.TryGetValue("Bank", out var cur) ? cur : cs.WindowSettings["Bank"] = new WindowSettings();
                r.Position = bPos; r.Visible = bVis; r.CanvasSize = bCanvas; r.Size = bSize; r.Factor = bFactor; r.Placed = bPlaced;
            }
            else
            {
                cs.WindowSettings.Remove("Bank");
            }
            cs.Save();
            if (bank != null)
                bank.QueueFree();
        }
        if (bank != null)
        {
            await Frame();
            Assert(!ContainsRoot(bank), "bank registration not pruned after free");
        }
        GD.Print("[ui_scale_selftest] OK 2b runtime spawn at 2x");

        // Step 2c: factor-aware multi-window lines; the trailing 1x apply also proves float
        // identity at 1x (no rounding).
        var info = GD.Load<PackedScene>("res://Scenes/UI/InfoWindow.tscn").Instantiate<InfoWindow>();
        gm.UiLayer.AddChild(info);
        await Frame();
        info.OnMakeWindow(new MakeWindowPacket
        {
            WindowId = 1001,
            WindowFrame = (WindowFrames)13,
            Title = "Welcome to my shop!",
            Buttons = new[] { false, true, false, false, false },
            NpcId = 1201,
            Unknown1 = 0,
            Unknown2 = 0,
        });
        info.OnWindowLine(new WindowLinePacket { WindowId = 1001, LineNumber = 0, Text = "line 0" });
        await Frame();
        var l0 = info.GetNode<Label>("Content/Line0");
        var l19 = info.GetNode<Label>("Content/Line19");
        Assert(l0.Position == MultiWindowMetrics.LinePosition(0, 2f), $"line 0 at 2x {l0.Position} != {MultiWindowMetrics.LinePosition(0, 2f)}");
        Assert(l19.Position == MultiWindowMetrics.LinePosition(19, 2f), $"line 19 at 2x {l19.Position} != {MultiWindowMetrics.LinePosition(19, 2f)}");
        var l0Font = l0.GetThemeFontSize("font_size");
        Assert(l0Font == 20, $"line 0 font {l0Font} != 20");
        var infoBg = info.GetNode<NinePatchRect>("Background");
        Assert(Patch(infoBg) == (6, 40, 20, 6),
            $"info bg patch margin 2x {Patch(infoBg)} != (6,40,20,6)");
        // Exact game packet spawn path (manager node parentage), pinned at 2x.
        {
            var infoMgr = new InfoWindowCreator();
            var questMgr = new QuestWindowManager();
            gm.Hud.AddChild(infoMgr);
            gm.Hud.AddChild(questMgr);
            await Frame();
            var wi = GD.Load<PackedScene>(infoMgr.PrefabPath).Instantiate<InfoWindow>();
            infoMgr.AddChild(wi);
            var wq = GD.Load<PackedScene>(questMgr.PrefabPath).Instantiate<QuestWindow>();
            questMgr.AddChild(wq);
            await Frame();
            var ib = wi.GetNode<NinePatchRect>("Background");
            var qb = wq.GetNode<NinePatchRect>("Background");
            Assert(wi.Size == new Vector2(504, 280), $"manager-spawned info size 2x {wi.Size} != (504,280)");
            Assert(Patch(ib) == (6, 40, 20, 6), $"manager-spawned info patch 2x {Patch(ib)} != (6,40,20,6)");
            Assert(wq.Size == new Vector2(520, 582), $"manager-spawned quest size 2x {wq.Size} != (520,582)");
            Assert(Patch(qb) == (10, 40, 24, 72), $"manager-spawned quest patch 2x {Patch(qb)} != (10,40,24,72)");
            wi.QueueFree();
            wq.QueueFree();
            await Frame();
            infoMgr.QueueFree();
            questMgr.QueueFree();
            await Frame();
        }
        applier.Apply(1f, ApplyReason.UserCommit);
        await Frame();
        Assert(l0.Position == new Vector2(6, 22), $"line 0 at 1x {l0.Position} != (6, 22)");
        Assert(l19.Position == new Vector2(6, 22 + 19 * 11.18f), $"line 19 at 1x {l19.Position} != (6, {22 + 19 * 11.18f})");
        Assert(Patch(infoBg) == (3, 20, 10, 3),
            $"info bg patch margin 1x {Patch(infoBg)} != (3,20,10,3)");
        Assert(gm.Hud.Hotbar.Position == WindowPlacement.HotbarDefault(canvas, gm.Hud.Hotbar.Size, 1f,
            DefaultWindowLayout.For("Hotbar"), gm.Hud.Hotbar.Size),
            $"hotbar 1x position {gm.Hud.Hotbar.Position} != HotbarDefault");
        info.QueueFree();
        await Frame();
        Assert(!ContainsRoot(info), "info registration not pruned after free");
        GD.Print("[ui_scale_selftest] OK 2c multi-window lines (2x + 1x identity)");

        // Step 2d: a saved (0,0) origin must survive; the persisted file is verified via a
        // throwaway instance because re-LoadSettings would swap the live object out from under the rest.
        var inv = gm.Hud.Inventory;
        bool iHad = false; Vector2 iPos = default; bool iVis = false; Vector2I iCanvas = default; Vector2 iSize = default; float iFactor = 0f; bool iPlaced = false;
        {
            var ws = cs.GetWindowSettings("Inventory");
            iHad = ws != null;
            if (ws != null)
            { iPos = ws.Position; iVis = ws.Visible; iCanvas = ws.CanvasSize; iSize = ws.Size; iFactor = ws.Factor; iPlaced = ws.Placed; }
        }
        var invPos1 = inv.Position;
        try
        {
            cs.SetWindowSetting("Inventory", new Vector2(0, 0), inv.Size, 1f, null, canvas);
            inv.RepositionFromSaved();
            await Frame();
            Assert(inv.Position == new Vector2(0, 0), $"inventory saved origin {inv.Position} != (0, 0)");
        }
        finally
        {
            if (iHad)
            {
                var r = cs.WindowSettings.TryGetValue("Inventory", out var cur) ? cur : cs.WindowSettings["Inventory"] = new WindowSettings();
                r.Position = iPos; r.Visible = iVis; r.CanvasSize = iCanvas; r.Size = iSize; r.Factor = iFactor; r.Placed = iPlaced;
            }
            else
            {
                cs.WindowSettings.Remove("Inventory");
            }
            cs.Save();
            inv.RepositionFromSaved();
            await Frame();
        }
        var verify = new CharacterSettings("ui-scale-selftest");
        var vr = verify.GetWindowSettings("Inventory");
        if (iHad)
            Assert(vr != null && vr.Position == iPos && vr.Visible == iVis && vr.CanvasSize == iCanvas
                && vr.Size == iSize && vr.Factor == iFactor && vr.Placed == iPlaced,
                "inventory record not restored on disk");
        else
            Assert(vr == null, "inventory record should be absent on disk");
        Assert(inv.Position == invPos1, $"inventory live pos {inv.Position} != {invPos1}");
        GD.Print("[ui_scale_selftest] OK 2d saved-origin round trip");

        // Step 2e: a visibility-only record must not demote a dialog to its default layout.
        bool eHad = false; Vector2 ePos = default; bool eVis = false; Vector2I eCanvas = default; Vector2 eSize = default; float eFactor = 0f; bool ePlaced = false;
        {
            var ws = cs.GetWindowSettings("Bank");
            eHad = ws != null;
            if (ws != null)
            { ePos = ws.Position; eVis = ws.Visible; eCanvas = ws.CanvasSize; eSize = ws.Size; eFactor = ws.Factor; ePlaced = ws.Placed; }
        }
        BankWindow dlg = null;
        try
        {
            cs.WindowSettings.Remove("Bank");
            cs.Save();
            dlg = GD.Load<PackedScene>("res://Scenes/UI/BankWindow.tscn").Instantiate<BankWindow>();
            gm.UiLayer.AddChild(dlg);
            await Frame();
            Assert(dlg.Position == WindowPlacement.Center(canvas, dlg.Size),
                $"bank no-record centering {dlg.Position} != {WindowPlacement.Center(canvas, dlg.Size)}");
            cs.SetWindowVisible("Bank", false);
            dlg.RepositionFromSaved();
            await Frame();
            Assert(dlg.Position == WindowPlacement.Center(canvas, dlg.Size),
                $"bank visibility-only centering {dlg.Position} != {WindowPlacement.Center(canvas, dlg.Size)}");
        }
        finally
        {
            if (eHad)
            {
                var r = cs.WindowSettings.TryGetValue("Bank", out var cur) ? cur : cs.WindowSettings["Bank"] = new WindowSettings();
                r.Position = ePos; r.Visible = eVis; r.CanvasSize = eCanvas; r.Size = eSize; r.Factor = eFactor; r.Placed = ePlaced;
            }
            else
            {
                cs.WindowSettings.Remove("Bank");
            }
            cs.Save();
            if (dlg != null)
                dlg.QueueFree();
        }
        if (dlg != null)
        {
            await Frame();
            Assert(!ContainsRoot(dlg), "dialog registration not pruned after free");
        }
        GD.Print("[ui_scale_selftest] OK 2e visibility-only record keeps dialog centering");

        // Step 3: back to 1x — the apply is the early-return probe; geometry and positions
        // must be bit-identical to the step-1 baseline.
        applier.Apply(1f, ApplyReason.UserCommit);
        await Frame();
        var geo3 = new Dictionary<Control, Vector4>();
        var auth3 = new Dictionary<Control, bool>();
        foreach (var r in applier.RegisteredWindows)
            Walk(r.ControlRef, geo3, auth3);
        Assert(geo3.Count == geo1.Count, $"restore: control count {geo3.Count} != {geo1.Count}");
        foreach (var kv in geo1)
            Assert(geo3.TryGetValue(kv.Key, out var v) && v == kv.Value,
                $"restore: {kv.Key.GetPath()} {kv.Value} != {v}");
        foreach (var kv in pos1)
            Assert(kv.Key.Position == kv.Value,
                $"restore: {kv.Key.WindowName} pos {kv.Key.Position} != {kv.Value}");
        var tile1 = (PartyMember)gm.Hud.Party.GetNode<VBoxContainer>("MemberList").GetChild(0);
        Assert(tile1.CustomMinimumSize == new Vector2(87, 33), $"party tile min {tile1.CustomMinimumSize} != (87, 33)");
        var sep1 = gm.Hud.Party.GetNode<VBoxContainer>("MemberList").GetThemeConstant("separation");
        Assert(sep1 == 1, $"party separation {sep1} != 1");
        Assert(slot.CustomMinimumSize == new Vector2(32, 32), $"item slot min {slot.CustomMinimumSize} != (32, 32)");
        var hotbarBg1 = gm.Hud.Hotbar.GetNode<NinePatchRect>("Background");
        Assert(Patch(hotbarBg1) == (3, 3, 3, 3),
            $"hotbar bg patch margin 1x restore {Patch(hotbarBg1)} != (3,3,3,3)");
        tm.ShowSpellTooltip(new SpellInfo { Name = "Selftest" }, gm.Hud);
        await Frame();
        await Frame();
        var labelMin1 = spellLabel.GetCombinedMinimumSize();
        Assert(spellLabel.OffsetLeft == 4 && spellLabel.OffsetTop == 2
            && spellLabel.OffsetRight == -4 && spellLabel.OffsetBottom == -2,
            $"spell label 1x offsets {spellLabel.OffsetLeft},{spellLabel.OffsetTop},{spellLabel.OffsetRight},{spellLabel.OffsetBottom} != (4,2,-4,-2)");
        Assert(spellTt.Size == labelMin1 + new Vector2(8, 4), $"spell tooltip 1x size {spellTt.Size} != {labelMin1} + (8,4)");
        tm.HideSpellTooltip();
        GD.Print("[ui_scale_selftest] OK 1x restore (idempotence: geometry + positions)");

        // Leg 2: login round trip — an explicit-override approach with a wrong base would not round-trip.
        Assert(loginMargin.Size == new Vector2(300, 200), $"login margin size 1x {loginMargin.Size} != (300, 200)");
        var loginFont3 = nameInput.GetThemeFontSize("font_size");
        var loginSep3 = loginVBox.GetThemeConstant("separation");
        Assert(loginFont3 == 10, $"login font 1x {loginFont3} != 10");
        Assert(loginSep3 == 10, $"login separation 1x {loginSep3} != 10");
        GD.Print("[ui_scale_selftest] OK login 1x round trip: 300x200, font 10, separation 10");

        // Leg 3: loading scene (SC-16) — instantiated directly; _Ready self-registers.
        var loading = GD.Load<PackedScene>("res://Scenes/LoadingMap.tscn").Instantiate<LoadingMapScene>();
        tree.Root.AddChild(loading);
        await Frame();
        var status = loading.GetNode<Label>("StatusLabel");
        var r1 = new Vector4(status.OffsetLeft, status.OffsetTop, status.OffsetRight, status.OffsetBottom);
        Assert(r1 != default, $"loading label rect degenerate {r1}");
        applier.Apply(2f, ApplyReason.UserCommit);
        await Frame();
        var r2 = new Vector4(status.OffsetLeft, status.OffsetTop, status.OffsetRight, status.OffsetBottom);
        Assert(r2 == new Vector4(r1.X * 2, r1.Y * 2, r1.Z * 2, r1.W * 2), $"loading label rect 2x {r2} != {r1} * 2");
        applier.Apply(1f, ApplyReason.UserCommit);
        await Frame();
        var r3 = new Vector4(status.OffsetLeft, status.OffsetTop, status.OffsetRight, status.OffsetBottom);
        Assert(r3 == r1, $"loading label rect 1x {r3} != {r1}");
        loading.QueueFree();
        await Frame();
        Assert(!ContainsRoot(loading), "loading registration not pruned after free");
        GD.Print("[ui_scale_selftest] OK loading leg: 2x double + 1x round trip + prune");
    }
}
