using Godot;

namespace Goose2Client;

internal static class CrowdBenchSelfTest
{
    public static async System.Threading.Tasks.Task Run(GameManager gm)
    {
        await gm.ToSignal(gm.GetTree(), SceneTree.SignalName.ProcessFrame);
        try
        {
            await Body(gm);
        }
        catch (System.Exception e)
        {
            GD.PrintErr($"ERR_crowd_bench: {e}");
        }
        gm.GetTree().Quit();
    }

    private static string Arg(string name, string fallback)
    {
        foreach (var a in OS.GetCmdlineUserArgs())
            if (a.StartsWith($"+{name}=")) return a.Substring(name.Length + 2);
        return fallback;
    }

    private static async System.Threading.Tasks.Task Body(GameManager gm)
    {
        var tree = gm.GetTree();
        async System.Threading.Tasks.Task Frame() => await gm.ToSignal(tree, SceneTree.SignalName.ProcessFrame);

        int count = int.Parse(Arg("crowd", "500"));
        string mapFile = Arg("crowdmap", "Map1.map");
        Engine.MaxFps = 0;
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);

        gm.ChangeMap(mapFile, "Bench");
        while (gm.CurrentMapManager == null || gm.WorldViewport.Current == null) await Frame();
        for (int i = 0; i < 30; i++) await Frame();
        var map = gm.CurrentMap!;
        var mm = gm.CurrentMapManager!;

        var rng = new System.Random(1234);
        int[] bodies = { 1, 10001, 10002, 10003, 10011, 10012 };
        int Pick(string folder)
        {
            var dirs = DirAccess.GetDirectoriesAt($"res://Assets/Sprites/{folder}");
            int variety = System.Math.Min(dirs.Length, int.Parse(Arg("crowdvariety", "100000")));
            return int.Parse(dirs[rng.Next(variety)]);
        }
        string Item(string folder) => rng.Next(3) == 0
            ? $"{Pick(folder)},*"
            : $"{Pick(folder)},{rng.Next(256)},{rng.Next(256)},{rng.Next(256)},{rng.Next(100, 256)}";

        int cx = map.Width / 2, cy = map.Height / 2;
        int side = (int)System.Math.Ceiling(System.Math.Sqrt(count));
        var positions = new (int x, int y)[count + 1];
        for (int id = 0; id <= count; id++)
        {
            int x = id == 0 ? cx : cx - side / 2 + (id - 1) % side;
            int y = id == 0 ? cy : cy - side / 2 + (id - 1) / side;
            x = System.Math.Clamp(x, 1, map.Width - 2);
            y = System.Math.Clamp(y, 1, map.Height - 2);
            positions[id] = (x, y);
            string pkt = $"MKC{id + 1},{(id % 3 == 0 ? 1 : 10)},Npc{id},,,,{x + 1},{y + 1},3,{(id % 3 == 0 ? 50 : 100)},{bodies[rng.Next(bodies.Length)]},0,0,0,0,3," +
                         $"{Pick("Hair")},{Item("Chest")},{Item("Helms")},{Item("Legs")},{Item("Feet")},{Item("Hands")},{Item("Hands")}," +
                         $"{rng.Next(256)},{rng.Next(256)},{rng.Next(256)},255,0,0,1,250,0,0,*";
            gm.PacketManager.Handle(pkt);
        }
        gm.PacketManager.Handle("SUC1");
        for (int i = 0; i < 60; i++) await Frame();

        var characters = gm.GetTree().Root.FindChildren("Char_*", "", true, false);
        GD.Print($"[crowd_bench] map={mapFile} {map.Width}x{map.Height} characters={characters.Count} nodes={Performance.GetMonitor(Performance.Monitor.ObjectNodeCount)}");

        bool walk = Arg("crowdwalk", "1") == "1";
        async System.Threading.Tasks.Task Measure(string label, double seconds)
        {
            for (int i = 0; i < 30; i++) await Frame();
            ulong start = Time.GetTicksUsec();
            int frames = 0;
            double proc = 0, draws = 0, objs = 0;
            double nextWalk = 0;
            while ((Time.GetTicksUsec() - start) / 1e6 < seconds)
            {
                double t = (Time.GetTicksUsec() - start) / 1e6;
                if (walk && t >= nextWalk)
                {
                    nextWalk = t + 0.3;
                    for (int n = 0; n < count / 10; n++)
                    {
                        int id = 1 + rng.Next(count);
                        var (x, y) = positions[id];
                        int d = rng.Next(4);
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0), ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 1 || ny < 1 || nx >= map.Width - 1 || ny >= map.Height - 1) continue;
                        positions[id] = (nx, ny);
                        gm.PacketManager.Handle($"MOC{id + 1},{nx + 1},{ny + 1}");
                    }
                }
                await Frame();
                frames++;
                proc += Performance.GetMonitor(Performance.Monitor.TimeProcess);
                draws += Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
                objs += Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
            }
            double elapsed = (Time.GetTicksUsec() - start) / 1e6;
            GD.Print($"[crowd_bench] {label,-28} fps={frames / elapsed,7:F1} frame={1000 * elapsed / frames,6:F2}ms process={1000 * proc / frames,6:F2}ms draws={draws / frames,7:F0} objects={objs / frames,7:F0}");
        }

        double secs = double.Parse(Arg("crowdsecs", "5"), System.Globalization.CultureInfo.InvariantCulture);
        if (Arg("crowdshot", "") is { Length: > 0 } shot)
            gm.GetViewport().GetTexture().GetImage().SavePng(shot);
        if (Arg("crowdbridge", "0") == "1")
        {
            var bridge = gm.WorldTextBridge;
            bridge.SetProcess(false);
            var sw = new System.Diagnostics.Stopwatch();
            int n = 0;
            ulong until = Time.GetTicksUsec() + (ulong)(secs * 1e6);
            while (Time.GetTicksUsec() < until)
            {
                if (n % 20 == 0)
                    for (int k = 0; k < count / 10; k++)
                    {
                        int id = 1 + rng.Next(count);
                        var (x, y) = positions[id];
                        int nx = System.Math.Clamp(x + rng.Next(3) - 1, 1, map.Width - 2);
                        positions[id] = (nx, y);
                        gm.PacketManager.Handle($"MOC{id + 1},{nx + 1},{y + 1}");
                    }
                await Frame();
                sw.Start();
                bridge._Process(0);
                sw.Stop();
                n++;
            }
            GD.Print($"[crowd_bench] bridge projection avg={sw.Elapsed.TotalMilliseconds * 1000 / n:F1}us over {n} frames, elements={bridge.GetChildCount()}");
            return;
        }
        await Measure("all", secs);
        if (Arg("crowdquick", "0") == "1") return;

        gm.WorldTextBridge.Visible = false;
        gm.WorldTextBridge.ProcessMode = Node.ProcessModeEnum.Disabled;
        await Measure("no overhead text", secs);

        foreach (var c in characters) ((Node)c).SetProcess(false);
        await Measure("no text, char script off", secs);

        foreach (var c in characters) ((Node)c).ProcessMode = Node.ProcessModeEnum.Disabled;
        await Measure("no text, chars frozen", secs);

        foreach (var c in characters) ((CanvasItem)c).Visible = false;
        await Measure("no text, chars hidden", secs);
    }
}
