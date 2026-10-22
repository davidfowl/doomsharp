namespace DoomSharp;

/// <summary>
/// Clean-room animated flat/wall texture cycles. Vanilla DOOM advances several
/// flats and wall textures (water, nukage, slime, blood, lava, fire) through a
/// fixed-length frame list on a fixed tic interval, independent of framerate.
/// The lump-name cycles below are plain WAD asset identifiers, not proprietary
/// engine source, and are only used when the named frame actually exists in
/// the loaded WAD; otherwise the original (static) name passes through
/// untouched so missing-texture diagnostics never regress.
/// </summary>
public static class TextureAnimation
{
    /// <summary>Vanilla DOOM holds each non-switch animated frame for 8 tics (35 tics/second).</summary>
    public const int TicsPerFrame = 8;

    static readonly string[][] FlatCycles =
    [
        ["NUKAGE1", "NUKAGE2", "NUKAGE3"],
        ["FWATER1", "FWATER2", "FWATER3", "FWATER4"],
        ["SWATER1", "SWATER2", "SWATER3", "SWATER4"],
        ["LAVA1", "LAVA2", "LAVA3", "LAVA4"],
        ["BLOOD1", "BLOOD2", "BLOOD3"],
        ["RROCK05", "RROCK06", "RROCK07", "RROCK08"],
        ["SLIME01", "SLIME02", "SLIME03", "SLIME04"],
        ["SLIME05", "SLIME06", "SLIME07", "SLIME08"],
        ["SLIME09", "SLIME10", "SLIME11", "SLIME12"],
    ];

    static readonly string[][] WallCycles =
    [
        ["BLODGR1", "BLODGR2", "BLODGR3", "BLODGR4"],
        ["BLODRIP1", "BLODRIP2", "BLODRIP3", "BLODRIP4"],
        ["FIREBLU1", "FIREBLU2"],
        ["FIRELAVA", "FIRELAV3"],
        ["FIREMAG1", "FIREMAG2", "FIREMAG3"],
        ["FIREWALA", "FIREWALB", "FIREWALL"],
        ["ROCKRED1", "ROCKRED2", "ROCKRED3"],
        ["SLADRIP1", "SLADRIP2", "SLADRIP3"],
        ["WFALL1", "WFALL2", "WFALL3", "WFALL4"],
        ["SFALL1", "SFALL2", "SFALL3", "SFALL4"],
    ];

    static readonly Dictionary<string, (string[] Frames, int Index)> flatLookup = Build(FlatCycles);
    static readonly Dictionary<string, (string[] Frames, int Index)> wallLookup = Build(WallCycles);

    static Dictionary<string, (string[] Frames, int Index)> Build(string[][] cycles)
    {
        Dictionary<string, (string[], int)> lookup = new(StringComparer.Ordinal);
        foreach (string[] cycle in cycles)
            for (int i = 0; i < cycle.Length; i++) lookup[cycle[i]] = (cycle, i);
        return lookup;
    }

    /// <summary>The 0-based frame within an N-frame cycle for a given tic count, independent of framerate.</summary>
    public static int FrameIndex(int tics, int frameCount) => tics / TicsPerFrame % frameCount;

    /// <summary>Resolves an animated flat name to its current-tic frame, or returns <paramref name="name"/> unchanged if it is not animated or the frame is unavailable.</summary>
    public static string Flat(string name, int tics, Func<string, bool> exists) => Resolve(flatLookup, name, tics, exists);

    /// <summary>Resolves an animated wall texture name to its current-tic frame, or returns <paramref name="name"/> unchanged if it is not animated or the frame is unavailable.</summary>
    public static string Wall(string name, int tics, Func<string, bool> exists) => Resolve(wallLookup, name, tics, exists);

    static string Resolve(Dictionary<string, (string[] Frames, int Index)> lookup, string name, int tics, Func<string, bool> exists)
    {
        if (!lookup.TryGetValue(name, out var entry)) return name;
        string candidate = entry.Frames[FrameIndex(tics, entry.Frames.Length)];
        return exists(candidate) ? candidate : name;
    }
}
