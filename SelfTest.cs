using System.Buffers.Binary;
using System.Security.Cryptography;

namespace DoomSharp;

public static class SelfTest
{
    public static void Run()
    {
        int checks = 0;
        void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException($"SELF-TEST FAILED: {message}");
            checks++; Console.WriteLine($"PASS {message}");
        }
        void Reject(Action action, string message)
        {
            try { action(); }
            catch (InvalidDataException) { Assert(true, message); return; }
            Assert(false, message);
        }
        Font.Validate();
        Assert(true, "all HUD glyphs are 5x7");
        byte[] demo = Demo.Create();
        Wad wad = new(demo);
        Assert(wad.Maps.SequenceEqual(["E1M1"]), "classic map discovery");
        Reject(() => _ = new Wad([1, 2, 3]), "reject truncated WAD");
        byte[] bad = (byte[])demo.Clone();
        BinaryPrimitives.WriteInt32LittleEndian(bad.AsSpan(8), int.MaxValue);
        Reject(() => _ = new Wad(bad), "reject out-of-file WAD directory");
        Wad broken = new(demo);
        BinaryPrimitives.WriteUInt16LittleEndian(broken.Require("NODES").AsSpan(24), 0);
        Reject(() => _ = new Map(broken, "E1M1"), "reject cyclic BSP");
        broken = new(demo);
        BinaryPrimitives.WriteUInt16LittleEndian(broken.Require("SIDEDEFS").AsSpan(28), 100);
        Reject(() => _ = new Map(broken, "E1M1"), "reject invalid sector reference");
        Game game = new(wad, "E1M1");
        Map map = game.Map;
        Assert(map.FindSector(new(128, 128)) == 0 && map.FindSector(new(272, 128)) == 1 &&
            map.FindSector(new(400, 128)) == 2, "BSP point queries choose all three sectors");
        Assert(map.SubsectorPolygons.All(p => p.Length == 4), "BSP-clipped convex floor polygons");
        Assert(map.BackToFront(new(128, 128)).SequenceEqual([2, 1, 0]), "BSP traversal is far-to-near");
        Assert(!map.CanMove(new(24, 128), new(8, 128), 16, 56, 0), "solid wall blocks player cylinder");
        Assert(!map.CanMove(new(232, 128), new(250, 128), 16, 56, 0), "closed door blocks movement");
        Assert(!map.ClearSight(new(128, 128), new(450, 128), 41), "closed door occludes hitscan");
        Picture patch = game.Assets.Patch("POSSA0")!;
        Assert(patch.Width == 32 && patch.Height == 56 && patch.Pixels.Contains(-1) && patch.Pixels.Any(p => p >= 0),
            "patch posts preserve opaque pixels and transparent holes");
        Assert(game.Assets.Texture("WALL").Pixels.All(p => p >= 0), "PNAMES texture patch composition");
        Assert(game.Assets.Sound("PISTOL") is { Length: 4410 }, "DMX resampling and guard removal");
        broken = new(demo);
        BinaryPrimitives.WriteInt32LittleEndian(broken.Require("POSSA0").AsSpan(8), int.MaxValue);
        Assets brokenAssets = new(broken, "E1M1");
        Reject(() => brokenAssets.Patch("POSSA0"), "reject invalid patch column");
        Renderer renderer = new(320, 200);
        renderer.Render(game);
        Assert(renderer.VisibleWallPixels > 1000 && renderer.VisiblePlanePixels > 1000,
            "textured walls AND perspective floors/ceilings cover meaningful pixels");
        int[] closedFrame = (int[])renderer.Pixels.Clone();
        Input input = new() { Held = Buttons.Forward };
        for (int i = 0; i < 25; i++) game.Tick(input, 1.0 / 35);
        Assert(game.HasShotgun && game.Shells == 8, "walking picks up shotgun and shells");
        Assert(game.Position.X <= 240.01 && game.Position.X >= 220, "swept movement stops at closed door");
        input.Held = Buttons.Use; game.Tick(input, 1.0 / 35);
        input.Held = Buttons.None;
        for (int i = 0; i < 60; i++) game.Tick(input, 1.0 / 35);
        Assert(map.Sectors[1].Ceiling >= 120, "use opens manual door to neighboring ceiling");
        Assert(map.ClearSight(new(128, 128), new(450, 128), 41), "open door permits line of sight");
        renderer.Render(game);
        Assert(renderer.VisibleSpritePixels > 50 && !closedFrame.SequenceEqual(renderer.Pixels),
            "sprite visible through open portal and rendered frame changes");
        input.Held = Buttons.Forward;
        for (int i = 0; i < 25; i++) game.Tick(input, 1.0 / 35);
        Assert(game.Position.X > 288 && game.Feet == 16, "walk through portal and step onto higher sector floor");
        input.Held = Buttons.Fire;
        for (int i = 0; i < 90 && game.Kills == 0; i++) game.Tick(input, 1.0 / 35);
        Assert(game.Kills == 1 && game.Shells < 8, "shotgun hitscan kills enemy and consumes ammo");
        Assert(game.Actors.Any(a => a.Type == 2007), "enemy death drops collectible ammunition");
        input.Held = Buttons.Forward;
        for (int i = 0; i < 90; i++) game.Tick(input, 1.0 / 35);
        Assert(game.Position.X <= 528.01 && game.Position.X > 490, "movement stops at exit wall");
        input.Held = Buttons.Use; game.Tick(input, 1.0 / 35);
        Assert(game.Finished, "exit use enters level-complete state");
        input.Held = Buttons.Restart; game.Tick(input, 1.0 / 35);
        Assert(!game.Finished && game.Health == 100 && game.Kills == 0 && !game.HasShotgun &&
            game.Position == new Vec(128, 128), "restart resets map, inventory and player");
        input.Held = Buttons.Fire;
        for (int i = 0; i < 15; i++) game.Tick(input, 1.0 / 35);
        Assert(game.Kills == 0 && game.Bullets < 50, "closed door prevents shooting enemy");
        input.Held = Buttons.None;
        renderer.Render(game);
        string first = Hash(renderer.Pixels);
        renderer.Render(game);
        Assert(first == Hash(renderer.Pixels), "fixed-state rendering is deterministic");
        Console.WriteLine($"SELF-TEST PASSED: {checks} behavioral checks; frame SHA256 {first}");
    }
    static string Hash(int[] pixels) => Convert.ToHexString(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(pixels.AsSpan())));
}
