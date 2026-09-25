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

        // Headless canvases are 64x64; the placement math assumes the 1280x720 design space.
        tree.Root.Size = new Vector2I(1280, 720);
        var canvas = (Vector2I)tree.Root.GetVisibleRect().Size;
        GD.Print($"[ui_scale_selftest] canvas={canvas.X}x{canvas.Y}");
        Assert(canvas.X > 0 && canvas.Y > 0, $"degenerate headless canvas {canvas}");

        // The selftest profile persists across runs; the explicit 1f apply keeps the
        // baseline independent of any persisted manual factor and of the canvas size.
        gm.LoadSettings("ui-scale-selftest");
        applier.Apply(1f, ApplyReason.Startup);
        gm.EnsureHud();
        await Frame();

        // Party buffs: exercise the internal packet-application path at 1x before the
        // baseline so live effect nodes are part of the round-trip geometry.
        var party = gm.Hud.Party;
        var memberList = party.GetNode<VBoxContainer>("MemberList");
        Assert(memberList.GetChildCount() == PartyWindow.MaxMembers,
            $"party rows {memberList.GetChildCount()} != {PartyWindow.MaxMembers}");
        Assert(memberList.OffsetBottom == 509, $"member list base height {memberList.OffsetBottom} != 509");
        Assert(!party.ApplyPartyBuffAdd(new PartyBuffAddPacket
            { LoginId = 7999, EffectId = 1, GraphicId = 1, GraphicFile = 1, RemainingMs = 60000, TotalMs = 120000, Name = "E1" }),
            "PBA for unknown member must be rejected");
        party.ApplyGroupUpdate(new GroupUpdatePacket { LineNumber = 0, LoginId = 7001, Name = "Al", LevelClassName = "1 Warrior" });
        Assert(!party.ApplyPartyBuffAdd(new PartyBuffAddPacket
            { LoginId = 7001, EffectId = 1, GraphicId = 1, GraphicFile = 1, RemainingMs = 60000, TotalMs = 120000, Name = "E1" }),
            "PBA before MKC must be rejected");
        party.ApplyMakeCharacter(new MakeCharacterPacket { LoginId = 7001, HPPercent = 1 });
        party.ApplyPartyBuffClear(new PartyBuffClearPacket { LoginId = 7001 });
        for (int i = 1; i <= 6; i++)
            Assert(party.ApplyPartyBuffAdd(new PartyBuffAddPacket
                { LoginId = 7001, EffectId = i, GraphicId = i, GraphicFile = i, RemainingMs = 60000, TotalMs = 120000, Name = $"E{i}" }),
                $"PBA {i} rejected");
        await Frame();
        var row0 = (PartyMember)memberList.GetChild(0);
        var effRow = row0.GetNode<HBoxContainer>("Content/EffectRow");
        Assert(effRow.GetChildCount() == 6, $"effect row children {effRow.GetChildCount()} != 6");
        var e1 = (PartyEffect)effRow.GetChild(0);
        Assert(e1.GetNode<TextureRect>("Icon") != null && e1.GetNode<BuffSweepBar>("Sweep") != null,
            "effect node must expose Icon and Sweep");
        Assert(e1.Size == new Vector2(12, 12), $"effect size 1x {e1.Size} != (12, 12)");
        Assert(e1.GetNode<TextureRect>("Icon").Size == new Vector2(12, 12),
            $"effect icon size 1x {e1.GetNode<TextureRect>("Icon").Size} != (12, 12)");
        Assert(e1.GetNode<BuffSweepBar>("Sweep").Size == new Vector2(12, 12),
            $"effect sweep size 1x {e1.GetNode<BuffSweepBar>("Sweep").Size} != (12, 12)");
        Assert(effRow.GetThemeConstant("separation") == 1,
            $"effect separation 1x {effRow.GetThemeConstant("separation")} != 1");
        Assert((float)(((Control)effRow.GetChild(1)).OffsetLeft - e1.OffsetRight) == 1f, "effect gap 1x != 1");
        Assert(effRow.OffsetTop > row0.GetNode<TextureProgressBar>("Content/MpBar").OffsetBottom,
            "effect row must begin below MP");
        Assert(row0.Size.X == 87, $"party frame width {row0.Size.X} != 87");
        var e6 = (PartyEffect)effRow.GetChild(5);
        Assert(e6.OffsetRight <= 87, $"sixth effect right {e6.OffsetRight} > 87 (must fit)");
        Assert(!effRow.ClipContents && !row0.GetNode<Control>("Content").ClipContents && !row0.ClipContents,
            "effect overflow must not be clipped");
        party.ApplyPartyBuffAdd(new PartyBuffAddPacket
            { LoginId = 7001, EffectId = 1, GraphicId = 1, GraphicFile = 1, RemainingMs = 60000, TotalMs = 120000, Name = "E1" });
        await Frame();
        Assert(ReferenceEquals(effRow.GetChild(0), e1), "renewal must retain the same effect node");
        Assert(effRow.GetChildCount() == 6, "renewal must not add a node");
        Assert(party.ApplyPartyBuffRemove(new PartyBuffRemovePacket { LoginId = 7001, EffectId = 3 }),
            "PBR for live effect must be accepted");
        await Frame();
        Assert(effRow.GetChildCount() == 5, $"effect count after PBR {effRow.GetChildCount()} != 5");
        party.ApplyEraseCharacter(new EraseCharacterPacket { LoginId = 7001 });
        await Frame();
        Assert(effRow.GetChildCount() == 0, $"effect count after ERC {effRow.GetChildCount()} != 0");
        Assert(!party.ApplyPartyBuffAdd(new PartyBuffAddPacket
            { LoginId = 7001, EffectId = 9, GraphicId = 9, GraphicFile = 9, RemainingMs = 60000, TotalMs = 120000, Name = "E9" }),
            "PBA after ERC must be rejected");
        party.ApplyGroupUpdate(new GroupUpdatePacket { LineNumber = 0, LoginId = 0, Name = "", LevelClassName = "" });
        await Frame();
        Assert(!row0.GetNode<Control>("Content").Visible, "empty GUD slot must hide row content");
        party.ApplyGroupUpdate(new GroupUpdatePacket { LineNumber = 0, LoginId = 7002, Name = "Bo", LevelClassName = "2 Mage" });
        party.ApplyMakeCharacter(new MakeCharacterPacket { LoginId = 7002, HPPercent = 1 });
        for (int i = 1; i <= 6; i++)
            Assert(party.ApplyPartyBuffAdd(new PartyBuffAddPacket
                { LoginId = 7002, EffectId = i, GraphicId = i, GraphicFile = i, RemainingMs = 60000, TotalMs = 120000, Name = $"E{i}" }),
                $"PBA reseed {i} rejected");
        await Frame();
        Assert(effRow.GetChildCount() == 6, $"effect count after reseed {effRow.GetChildCount()} != 6");
        GD.Print("[ui_scale_selftest] OK party buff packet path (GUD/MKC/PBC/PBA/renewal/PBR/ERC)");

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
        var loginCard = login.GetNode<PanelContainer>("LoginLayout/Center/LoginCard");
        var loginVBox = login.GetNode<VBoxContainer>("LoginLayout/Center/LoginCard/CardPadding/VBox");
        var nameInput = login.GetNode<LineEdit>("LoginLayout/Center/LoginCard/CardPadding/VBox/NameInput");
        Assert(loginCard.CustomMinimumSize == new Vector2(340, 0), $"login card min {loginCard.CustomMinimumSize} != (340, 0)");
        var loginFont1 = nameInput.GetThemeFontSize("font_size");
        var loginSep1 = loginVBox.GetThemeConstant("separation");
        Assert(loginFont1 == 12, $"login font base {loginFont1} != 12");
        Assert(loginSep1 == 7, $"login separation {loginSep1} != 7");
        GD.Print("[ui_scale_selftest] OK login 1x baseline: card min 340x0, font 12, separation 7");

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
        CheckFont(gm.Hud.Chat.GetNode<RichTextLabel>("Content/ChatLog"), new StringName("normal_font_size"), 12f);
        CheckFont(gm.Hud.Chat.GetNode<LineEdit>("Content/Input"), new StringName("font_size"), 12f);
        CheckFont(gm.Hud.Debug.GetNode<Label>("FpsText"), new StringName("font_size"), 12f);
        CheckFont(gm.Hud.Debug.GetNode<Label>("VersionText"), new StringName("font_size"), 12f);
        CheckFont(gm.Hud.Bank.GetNode<Label>("TitleBar/TitleLabel"), new StringName("font_size"), 9f);
        CheckFont(gm.Hud.Vendor.GetNode<Label>("TitleBar/TitleLabel"), new StringName("font_size"), 10f);

        Assert(gm.Hud.Vitals.Size == new Vector2(366, 110), $"vitals size {gm.Hud.Vitals.Size} != (366, 110)");
        Assert(gm.Hud.Vitals.Position == new Vector2(16, 16), $"vitals pos {gm.Hud.Vitals.Position} != (16, 16)");
        var slot = (ItemSlot)gm.Hud.Inventory.GetNode<GridContainer>("Content/SlotGrid").GetChild(0);
        Assert(slot.CustomMinimumSize == new Vector2(64, 64), $"item slot min {slot.CustomMinimumSize} != (64, 64)");
        Assert(gm.Hud.Chat.Size == new Vector2(1000, 416), $"chat size {gm.Hud.Chat.Size} != (1000, 416)");
        Assert(gm.Hud.Chat.Position == new Vector2(16, canvas.Y - 426),
            $"chat pos {gm.Hud.Chat.Position} != (16, {canvas.Y - 426})");

        var vendor = gm.Hud.Vendor;
        Assert(vendor.Position.X >= 0f && vendor.Position.X <= Mathf.Max(0f, canvas.X - vendor.Size.X),
            $"vendor x postcondition {vendor.Position}");
        Assert(vendor.Position.Y >= 0f && vendor.Position.Y <= Mathf.Max(0f, canvas.Y - applier.ScaleSize(24f)),
            $"vendor y postcondition {vendor.Position}");

        var pList = gm.Hud.Party.GetNode<VBoxContainer>("MemberList");
        Assert(pList.GetChildCount() == 10, $"party tiles {pList.GetChildCount()} != 10");
        var sep = pList.GetThemeConstant("separation");
        Assert(sep == 2, $"party separation {sep} != 2");
        var tile = (PartyMember)pList.GetChild(0);
        Assert(tile.CustomMinimumSize == new Vector2(174, 100), $"party tile min {tile.CustomMinimumSize} != (174, 100)");
        var nameOffset = tile.GetNode<Label>("Content/NameText").OffsetBottom;
        Assert(nameOffset == 22, $"party name offset {nameOffset} != 22");
        var effRow2 = tile.GetNode<HBoxContainer>("Content/EffectRow");
        var e2 = (PartyEffect)effRow2.GetChild(0);
        Assert(e2.Size == new Vector2(24, 24), $"effect size 2x {e2.Size} != (24, 24)");
        Assert(e2.GetNode<TextureRect>("Icon").Size == new Vector2(24, 24),
            $"effect icon size 2x {e2.GetNode<TextureRect>("Icon").Size} != (24, 24)");
        Assert(e2.GetNode<BuffSweepBar>("Sweep").Size == new Vector2(24, 24),
            $"effect sweep size 2x {e2.GetNode<BuffSweepBar>("Sweep").Size} != (24, 24)");
        Assert(effRow2.GetThemeConstant("separation") == 2,
            $"effect separation 2x {effRow2.GetThemeConstant("separation")} != 2");
        Assert((float)(((Control)effRow2.GetChild(1)).OffsetLeft - e2.OffsetRight) == 2f, "effect gap 2x != 2");

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
        Assert(hotSlot.GetNode<CooldownOverlay>("CooldownOverlay") != null,
            "hotbar slot cooldown overlay missing");
        Assert(hotbar.GetNode<TextureRect>("Background") != null,
            "hotbar bg must be a stretched TextureRect");
        Assert(hotbar.Position == WindowPlacement.HotbarDefault(canvas, hotbar.Size, 2f,
            DefaultWindowLayout.For("Hotbar"), hotbar.Size / 2f),
            $"hotbar 2x position {hotbar.Position} != HotbarDefault");
        GD.Print($"[ui_scale_selftest] OK 2x audit: theme=20, {fontChecked} font overrides, spell tooltip {expectedSpell}, 7 nine-patch bars");

        // Leg 1: login at 2x.
        Assert(loginCard.CustomMinimumSize == new Vector2(680, 0), $"login card min 2x {loginCard.CustomMinimumSize} != (680, 0)");
        var loginFont2 = nameInput.GetThemeFontSize("font_size");
        var loginSep2 = loginVBox.GetThemeConstant("separation");
        Assert(loginFont2 == 24, $"login font 2x {loginFont2} != 24");
        Assert(loginSep2 == 14, $"login separation 2x {loginSep2} != 14");
        GD.Print("[ui_scale_selftest] OK login 2x: card min 680x0, font 24, separation 14");

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
        Assert(info.GetNode<Panel>("Background") != null,
            "info bg must be the themed Panel");
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
            Assert(wi.Size == new Vector2(504, 280), $"manager-spawned info size 2x {wi.Size} != (504,280)");
            Assert(wi.GetNode<Panel>("Background") != null, "manager-spawned info bg must be the themed Panel");
            Assert(wq.Size == new Vector2(520, 582), $"manager-spawned quest size 2x {wq.Size} != (520,582)");
            Assert(wq.GetNode<Panel>("Background") != null, "manager-spawned quest bg must be the themed Panel");
            wi.QueueFree();
            wq.QueueFree();
            await Frame();
            infoMgr.QueueFree();
            questMgr.QueueFree();
            await Frame();
        }
        // OptionList leg: dynamic height over a stretched frame.
        var ol = GD.Load<PackedScene>(gm.Hud.OptionListWindows.PrefabPath).Instantiate<OptionListWindow>();
        gm.Hud.OptionListWindows.AddChild(ol);
        await Frame();
        ol.OnMakeWindow(new MakeWindowPacket
        {
            WindowId = 1002,
            WindowFrame = WindowFrames.OptionList,
            Title = "Pick",
            Buttons = new[] { false, true, true, false, true },
            NpcId = 1201,
        });
        for (int i = 0; i < 4; i++)
            ol.OnWindowLine(new WindowLinePacket { WindowId = 1002, LineNumber = i, Text = $"opt {i}" });
        ol.OnEndWindow();
        await Frame();
        Assert(ol.Size == new Vector2(520, 292), $"option-list 4-line size 2x {ol.Size} != (520,292)");
        var ol0 = ol.GetNode<Button>("Content/Line0");
        Assert(ol0.Position == OptionListMetrics.LinePosition(0, 2f, false, false), $"option-list line0 pos 2x {ol0.Position} != {OptionListMetrics.LinePosition(0, 2f, false, false)}");
        Assert(ol0.Size == OptionListMetrics.LineSize(2f, false), $"option-list line0 size 2x {ol0.Size} != {OptionListMetrics.LineSize(2f, false)}");
        Assert(!ol.GetNode<Button>("Content/Line4").Visible, "empty option-list line must be invisible");
        Assert(ol.GetNode<Panel>("Background") != null, "option-list bg must be the themed Panel");
        // A repopulated page shrinks to its populated lines; stale lines are cleared by MakeWindow.
        ol.OnMakeWindow(new MakeWindowPacket
        {
            WindowId = 1002,
            WindowFrame = WindowFrames.OptionList,
            Title = "Pick",
            Buttons = new[] { false, true, true, false, true },
            NpcId = 1201,
        });
        ol.OnWindowLine(new WindowLinePacket { WindowId = 1002, LineNumber = 0, Text = "a" });
        ol.OnWindowLine(new WindowLinePacket { WindowId = 1002, LineNumber = 1, Text = "b" });
        ol.OnEndWindow();
        await Frame();
        Assert(ol.Size == new Vector2(520, 200), $"option-list 2-line size 2x {ol.Size} != (520,200)");
        Assert(!ol.GetNode<Button>("Content/Line2").Visible, "stale option-list line must be hidden after repopulate");
        applier.Apply(1f, ApplyReason.UserCommit);
        await Frame();
        Assert(l0.Position == new Vector2(6, 22), $"line 0 at 1x {l0.Position} != (6, 22)");
        Assert(l19.Position == new Vector2(6, 22 + 19 * 11.18f), $"line 19 at 1x {l19.Position} != (6, {22 + 19 * 11.18f})");
        Assert(ol.Size == new Vector2(260, 100), $"option-list 2-line size 1x {ol.Size} != (260,100)");
        Assert(ol0.Position == new Vector2(6, 22), $"option-list line0 pos 1x {ol0.Position} != (6,22)");
        Assert(gm.Hud.Hotbar.Position == WindowPlacement.HotbarDefault(canvas, gm.Hud.Hotbar.Size, 1f,
            DefaultWindowLayout.For("Hotbar"), gm.Hud.Hotbar.Size),
            $"hotbar 1x position {gm.Hud.Hotbar.Position} != HotbarDefault");
        info.QueueFree();
        ol.QueueFree();
        await Frame();
        Assert(!ContainsRoot(info), "info registration not pruned after free");
        Assert(!ContainsRoot(ol), "option-list registration not pruned after free");
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
        Assert(tile1.CustomMinimumSize == new Vector2(87, 50), $"party tile min {tile1.CustomMinimumSize} != (87, 50)");
        var sep1 = gm.Hud.Party.GetNode<VBoxContainer>("MemberList").GetThemeConstant("separation");
        Assert(sep1 == 1, $"party separation {sep1} != 1");
        Assert(gm.Hud.Party.GetNode<VBoxContainer>("MemberList").OffsetBottom == 509,
            "member list base height must survive the round trip");
        var effRow1 = tile1.GetNode<HBoxContainer>("Content/EffectRow");
        var e1r = (PartyEffect)effRow1.GetChild(0);
        Assert(e1r.Size == new Vector2(12, 12), $"effect size 1x restore {e1r.Size} != (12, 12)");
        Assert(e1r.GetNode<TextureRect>("Icon").Size == new Vector2(12, 12),
            $"effect icon size 1x restore {e1r.GetNode<TextureRect>("Icon").Size} != (12, 12)");
        Assert(e1r.GetNode<BuffSweepBar>("Sweep").Size == new Vector2(12, 12),
            $"effect sweep size 1x restore {e1r.GetNode<BuffSweepBar>("Sweep").Size} != (12, 12)");
        Assert((float)(((Control)effRow1.GetChild(1)).OffsetLeft - e1r.OffsetRight) == 1f, "effect gap 1x restore != 1");
        Assert(slot.CustomMinimumSize == new Vector2(32, 32), $"item slot min {slot.CustomMinimumSize} != (32, 32)");
        Assert(gm.Hud.Hotbar.GetNode<TextureRect>("Background") != null,
            "hotbar bg must stay a stretched TextureRect");
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
        Assert(loginCard.CustomMinimumSize == new Vector2(340, 0), $"login card min 1x {loginCard.CustomMinimumSize} != (340, 0)");
        var loginFont3 = nameInput.GetThemeFontSize("font_size");
        var loginSep3 = loginVBox.GetThemeConstant("separation");
        Assert(loginFont3 == 12, $"login font 1x {loginFont3} != 12");
        Assert(loginSep3 == 7, $"login separation 1x {loginSep3} != 7");
        GD.Print("[ui_scale_selftest] OK login 1x round trip: card min 340x0, font 12, separation 7");

        // Leg 3: loading scene (SC-16) — instantiated directly; _Ready self-registers.
        var loading = GD.Load<PackedScene>("res://Scenes/LoadingMap.tscn").Instantiate<LoadingMapScene>();
        tree.Root.AddChild(loading);
        await Frame();
        var panel = loading.GetNode<PanelContainer>("LoadingPanel");
        var r1 = new Vector4(panel.OffsetLeft, panel.OffsetTop, panel.OffsetRight, panel.OffsetBottom);
        Assert(r1 != default, $"loading panel rect degenerate {r1}");
        applier.Apply(2f, ApplyReason.UserCommit);
        await Frame();
        var r2 = new Vector4(panel.OffsetLeft, panel.OffsetTop, panel.OffsetRight, panel.OffsetBottom);
        Assert(r2 == new Vector4(r1.X * 2, r1.Y * 2, r1.Z * 2, r1.W * 2), $"loading panel rect 2x {r2} != {r1} * 2");
        applier.Apply(1f, ApplyReason.UserCommit);
        await Frame();
        var r3 = new Vector4(panel.OffsetLeft, panel.OffsetTop, panel.OffsetRight, panel.OffsetBottom);
        Assert(r3 == r1, $"loading panel rect 1x {r3} != {r1}");
        loading.QueueFree();
        await Frame();
        Assert(!ContainsRoot(loading), "loading registration not pruned after free");
        GD.Print("[ui_scale_selftest] OK loading leg: 2x double + 1x round trip + prune");
    }
}
