using Godot;
using Goose2Client.Map;
using MapEditor.Core;

namespace Goose2Client;

internal static class AmbienceSelfTest
{
    public static async System.Threading.Tasks.Task Run(GameManager gm)
    {
        // In-product gate behind a project arg; production with no arg never reaches this.
        await gm.ToSignal(gm.GetTree(), SceneTree.SignalName.ProcessFrame);
        bool failed = false;
        try
        {
            await SelfTestBody(gm);
            GD.Print("[ambience_selftest] PASS");
        }
        catch (System.Exception e)
        {
            failed = true;
            GD.PrintErr($"ERR_ambience_selftest: {e.Message}");
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

        int LightCount(Goose2Client.Character.Character c)
        {
            int n = 0;
            foreach (var child in c.GetChildren())
                if (child is LocalPlayerLight) n++;
            return n;
        }

        gm.CurrentMap = MapDocument.Create(4, 4);
        var mapScene = GD.Load<PackedScene>("res://Scenes/Map.tscn").Instantiate<SubViewport>();
        tree.Root.AddChild(mapScene);
        await Frame();
        var mm = gm.CurrentMapManager;
        Assert(mm != null, "MapManager did not register");
        var amb = mm!.Ambience;
        Assert(amb != null, "AmbienceManager missing");
        Assert(ReferenceEquals(amb.TintNode.GetViewport(), mapScene),
            "tint CanvasModulate is not in the map SubViewport");
        GD.Print("[ambience_selftest] OK tint lives in the map SubViewport");

        Assert(amb.TintNode.Color == Colors.White, $"default tint {amb.TintNode.Color} != white");
        GD.Print("[ambience_selftest] OK default tint is white");

        gm.PacketManager.Handle("AMB700,700,151587327,1321471,0,0,0");
        await Frame();
        Assert(Mathf.Abs(amb.Darkness - 0.7f) < 0.001f, $"cave darkness {amb.Darkness} != 0.7");
        Assert(amb.TintNode.Color != Colors.White, "cave tint still white");
        GD.Print($"[ambience_selftest] OK cave: darkness {amb.Darkness}, tint {amb.TintNode.Color}");

        gm.PacketManager.Handle("AMB0,700,151587327,1321471,0,1440000,1");
        await Frame();
        Assert(amb.CurrentWeather == WeatherType.Rain, $"weather {amb.CurrentWeather} != Rain");
        Assert(amb._rain!.Emitting && !amb._snow!.Emitting, "rain not emitting exclusively");
        gm.PacketManager.Handle("AMB0,700,151587327,1321471,0,1440000,4");
        await Frame();
        Assert(amb.CurrentWeather == WeatherType.Snow, $"weather {amb.CurrentWeather} != Snow");
        Assert(amb._snow!.Emitting && !amb._rain!.Emitting, "snow not emitting exclusively");
        gm.PacketManager.Handle("AMB0,700,151587327,1321471,0,1440000,2");
        var fogStart = Time.GetTicksMsec();
        while (Time.GetTicksMsec() - fogStart < 2200)
            await Frame();
        Assert(Mathf.Abs(amb._fog!.Color.A - 0.3f) < 0.01f, $"fog alpha {amb._fog.Color.A} != 0.3");
        GD.Print($"[ambience_selftest] OK weather: rain/snow exclusive, fog alpha {amb._fog.Color.A}");

        gm.PacketManager.Handle("MKC7,1,Mon,,,0,3,4,1,50,999998,255,0,0,255,0,0,0,999,0");
        gm.PacketManager.Handle("SUC7");
        await Frame();
        var mc = mm.GetCharacter(7);
        Assert(mc != null, "MKC did not spawn the character");
        Assert(LightCount(mc!) == 1, $"local player light count {LightCount(mc!)} != 1");
        gm.PacketManager.Handle("SUC7");
        await Frame();
        Assert(LightCount(mc!) == 1, $"second SUC added a light ({LightCount(mc!)})");
        gm.PacketManager.Handle("MKC8,1,Mon,,,0,3,4,1,50,999998,255,0,0,255,0,0,0,999,0");
        await Frame();
        var other = mm.GetCharacter(8);
        Assert(other != null, "second MKC did not spawn");
        Assert(LightCount(other!) == 0, "non-local character has a light");
        GD.Print("[ambience_selftest] OK exactly one light on the local player, idempotent, none on others");

        gm.PacketManager.Handle("AMB700,700,151587327,1321471,0,0,0");
        await Frame();
        await Frame();
        var light = mc!.GetNode<LocalPlayerLight>("Light");
        Assert(Mathf.Abs(light.Energy - 0.6f * 0.7f) < 0.001f, $"light energy {light.Energy} != 0.42");
        GD.Print($"[ambience_selftest] OK cave light energy {light.Energy}");

        mapScene.QueueFree();
        await Frame();
        await Frame();
        try
        {
            gm.PacketManager.Handle("AMB0,0,151587327,1321471,0,0,3");
        }
        catch (System.Exception e)
        {
            throw new System.InvalidOperationException($"AMB after map free threw: {e.Message}");
        }
        GD.Print("[ambience_selftest] OK AMB after map free does not throw");
    }
}
