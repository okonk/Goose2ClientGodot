using Godot;
using Goose2Client.Character;
using Goose2Client.Network.Packets;
using MapEditor.Core;

namespace Goose2Client;

internal static class CharacterIconSelfTest
{
    // Made-up id so the heights lookup always misses and falls back to the 64 default.
    private const int FixtureBodyId = 999998;
    private static readonly string FixtureBodyPath = $"res://Assets/Sprites/Bodies/{FixtureBodyId}/animations.tres";

    public static async System.Threading.Tasks.Task Run(GameManager gm)
    {
        // In-product gate behind a project arg; production with no arg never reaches this.
        await gm.ToSignal(gm.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool failed = false;
        try
        {
            await SelfTestBody(gm);
            GD.Print("[character_icon_selftest] PASS");
        }
        catch (System.Exception e)
        {
            failed = true;
            GD.PrintErr($"ERR_character_icon_selftest: {e.Message}");
        }
        gm.GetTree().Quit(failed ? 1 : 0);
    }

    private static async System.Threading.Tasks.Task SelfTestBody(GameManager gm)
    {
        var tree = gm.GetTree();

        async System.Threading.Tasks.Task Frame() => await gm.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        void Assert(bool cond, string msg)
        {
            if (!cond)
                throw new System.InvalidOperationException(msg);
        }

        Texture2D MakeTexture(int w, int h, Color c)
        {
            var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
            img.Fill(c);
            return ImageTexture.CreateFromImage(img);
        }

        Sprite2D FindIcon(Goose2Client.Character.Character c) => c.GetNodeOrNull<Sprite2D>("Icon");

        int IconCount(Goose2Client.Character.Character c)
        {
            int n = 0;
            foreach (var child in c.GetChildren())
                if (child is Sprite2D) n++;
            return n;
        }

        // Self-contained Body slot: a SpriteFrames saved from a generated texture, so the
        // character can gain a real Height without depending on pipeline-generated art.
        // The Bodies directory does not exist in every checkout; the recursive make is a no-op
        // when it already does.
        Assert(DirAccess.MakeDirRecursiveAbsolute("res://Assets/Sprites/Bodies") == Error.Ok,
            "could not create the fixture body directory");
        var fixtureDir = DirAccess.Open("res://Assets/Sprites/Bodies");
        if (!fixtureDir.FileExists($"{FixtureBodyId}/animations.tres"))
        {
            fixtureDir.MakeDir($"{FixtureBodyId}");
            var frames = new SpriteFrames();
            frames.AddAnimation("idle-down");
            frames.SetAnimationSpeed("idle-down", 1f);
            frames.AddFrame("idle-down", MakeTexture(32, 48, new Color(0, 1, 0)));
            Assert(ResourceSaver.Save(frames, FixtureBodyPath) == Error.Ok, "fixture body save failed");
        }

        try
        {
            var root = new Node2D { Name = "CharacterIconSelfTest" };
            tree.Root.AddChild(root);
            var c = new Goose2Client.Character.Character { Name = "IconChar" };
            root.AddChild(c);
            await Frame();

            var red = MakeTexture(32, 32, new Color(1, 0, 0));
            c.SetIcon(red);
            var icon = FindIcon(c);
            Assert(icon != null, "SetIcon did not create the icon sprite");
            Assert(IconCount(c) == 1, $"icon child count {IconCount(c)} != 1");
            Assert(icon.Texture == red, "icon texture not applied");
            Assert(icon.TextureFilter == CanvasItem.TextureFilterEnum.Nearest, "icon filter not Nearest");
            Assert(icon.ZIndex == 20 && !icon.ZAsRelative, $"icon z {icon.ZIndex} relative={icon.ZAsRelative} != absolute 20");
            Assert(icon.Scale == Vector2.One, $"icon scale {icon.Scale} != native");
            Assert(icon.Modulate == new Color(1, 1, 1), $"icon modulate {icon.Modulate} != white");
            Assert(icon.Position == CharacterAnchor.IconPosition(c.Height, new Vector2(32, 32),
                        Goose2Client.Character.Character.NameTopOffset, 2f), $"icon position {icon.Position}");
            GD.Print($"[character_icon_selftest] OK first apply: one child, nearest/native/absolute-z20, pos {icon.Position}");

            var odd = MakeTexture(33, 17, new Color(0, 0, 1));
            c.SetIcon(odd);
            Assert(ReferenceEquals(FindIcon(c), icon), "replacement allocated a second sprite");
            Assert(IconCount(c) == 1, $"icon child count after replace {IconCount(c)} != 1");
            Assert(icon.Texture == odd, "replacement did not swap the texture");
            Assert(icon.Position == CharacterAnchor.IconPosition(c.Height, new Vector2(33, 17),
                        Goose2Client.Character.Character.NameTopOffset, 2f), $"icon position after replace {icon.Position}");
            GD.Print($"[character_icon_selftest] OK replacement reuses the sprite, pos {icon.Position}");

            c.SetIcon(null);
            var cleared = FindIcon(c);
            Assert(cleared != null && cleared.Texture == null, "clear left a visible texture");
            GD.Print("[character_icon_selftest] OK clear removes the texture immediately");

            c.SetIcon(red);
            Assert(c.Height == 0, $"pre-body height {c.Height} != 0");
            Assert(icon.Position == CharacterAnchor.IconPosition(0, new Vector2(32, 32),
                        Goose2Client.Character.Character.NameTopOffset, 2f), $"fallback position {icon.Position}");
            c.SetAppearance(new MakeCharacterPacket
            {
                LoginId = 55,
                Name = "IconTest",
                MapX = 0,
                MapY = 0,
                BodyId = FixtureBodyId,
                BodyState = 3,
                HPPercent = 1f,
            });
            Assert(c.Height == 64, $"fixture body height {c.Height} != 64");
            Assert(icon.Position == CharacterAnchor.IconPosition(64, new Vector2(32, 32),
                        Goose2Client.Character.Character.NameTopOffset, 2f), $"height relayout position {icon.Position}");
            GD.Print($"[character_icon_selftest] OK height relayout repositioned the icon to {icon.Position}");

            // MapManager dispatch: unknown login is a no-op; CHI with sheet 0 clears a live icon.
            gm.CurrentMap = MapDocument.Create(4, 4);
            var mapScene = GD.Load<PackedScene>("res://Scenes/Map.tscn").Instantiate<SubViewport>();
            tree.Root.AddChild(mapScene);
            await Frame();
            var mm = gm.CurrentMapManager;
            Assert(mm != null, "MapManager did not register");

            gm.PacketManager.Handle("CHI424242,0,0");
            Assert(mm.GetCharacter(424242) == null, "unknown-login CHI spawned a character");

            gm.PacketManager.Handle($"MKC7,1,Mon,,,0,3,4,1,50,{FixtureBodyId},255,0,0,255,0,0,999,0");
            await Frame();
            var mc = mm.GetCharacter(7);
            Assert(mc != null, "MKC did not spawn the character");
            mc.SetIcon(red);
            Assert(FindIcon(mc) is { Texture: not null }, "dispatched character has no icon");
            gm.PacketManager.Handle("CHI7,0,0");
            Assert(FindIcon(mc) is { Texture: null }, "CHI clear did not clear the icon");
            GD.Print("[character_icon_selftest] OK MapManager dispatch: unknown no-op, CHI clear");

            mapScene.QueueFree();
            await Frame();

            c.QueueFree();
            await Frame();
            Assert(!GodotObject.IsInstanceValid(icon), "icon not freed with its character");
            GD.Print("[character_icon_selftest] OK parent removal frees the icon");

            root.QueueFree();
            await Frame();
        }
        finally
        {
            var bodies = DirAccess.Open("res://Assets/Sprites/Bodies");
            if (bodies != null && bodies.FileExists($"{FixtureBodyId}/animations.tres"))
                bodies.Remove($"{FixtureBodyId}/animations.tres");
            RemoveDirIfEmpty($"res://Assets/Sprites/Bodies/{FixtureBodyId}");
            // Never delete a pre-existing Bodies directory that still holds real bodies.
            RemoveDirIfEmpty("res://Assets/Sprites/Bodies");
        }
    }

    private static void RemoveDirIfEmpty(string path)
    {
        var dir = DirAccess.Open(path);
        if (dir == null) return;
        if (dir.GetFiles().Length == 0 && dir.GetDirectories().Length == 0)
            DirAccess.RemoveAbsolute(path);
    }
}
