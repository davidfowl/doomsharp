using System.Text.Json.Serialization;

namespace DoomSharp;

/// <summary>
/// A serializable snapshot of in-progress play: current map, player position/stats/inventory and the
/// removed/health/alert state of every actor spawned on that map (in <see cref="Game.Spawn"/> iteration
/// order, which is deterministic for a given map's <c>Things</c> list). In-flight projectiles are not
/// preserved; loading always resumes with no projectiles airborne, matching a player standing still to save.
/// <see cref="Items"/> is a trailing optional field (version 2); version-1 saves load with it defaulted
/// to zero, understating the restored intermission "items" percentage but otherwise loading unchanged.
/// <see cref="ActorAwake"/>/<see cref="ActorAlertX"/>/<see cref="ActorAlertY"/> are trailing optional
/// fields (version 3, null on versions 1-2): a monster's noise-alert state (see <c>Game.NoiseAlert</c>)
/// used to be silently dropped on load, so every alerted monster went back to sleep the instant a save was
/// restored; version-1/2 saves still load that way (null means "not awake"), but version-3 saves round-trip
/// it, so a monster that was blindly chasing a remembered sound keeps doing so after loading.
/// <see cref="ActorChaseMemory"/>/<see cref="ActorLastKnownTargetX"/>/<see cref="ActorLastKnownTargetY"/>/
/// <see cref="ActorTargetIndex"/> are trailing optional fields (version 4, null on versions 1-3): a
/// monster's "chase memory" countdown/remembered position (see <c>Game.UpdateActor</c>) and its infighting
/// <c>Target</c> (see <c>Game.Hurt</c>) used to be silently dropped on load, so a monster that was
/// pursuing a remembered position or fighting another monster instead reverted to idle or re-acquiring the
/// player the instant a save was restored; version-1/2/3 saves still load that way (null means "no
/// memory"/"no infighting target"), but version-4 saves round-trip both, so the monster keeps doing
/// whichever of the two it was doing after loading. <see cref="ActorTargetIndex"/> stores each actor's
/// <c>Target</c> as an index into the deterministic spawn-order actor list (-1 for "no target", i.e. fight
/// the player), which stays valid across save/load because <c>Spawn</c> rebuilds that list identically; any
/// other negative value or an index at/beyond the actor count is corrupt save data, rejected by
/// <c>Game.TryLoad</c> rather than silently clamped. <see cref="ActorAlertX"/>/<see cref="ActorAlertY"/>/
/// <see cref="ActorLastKnownTargetX"/>/<see cref="ActorLastKnownTargetY"/> encode "no position" as an
/// ordinary JSON <c>null</c> per-actor element (nullable array elements), not a <c>double.NaN</c> sentinel,
/// so number parsing/writing for every other (non-coordinate) field in this type stays strict about
/// rejecting non-finite values rather than needing to relax <c>System.Text.Json</c>'s number handling for
/// the whole save format.
/// <see cref="ActorPositionX"/>/<see cref="ActorPositionY"/> are trailing optional fields (version 5, null
/// on versions 1-4): an actor's live <c>Position</c> (see <c>Game.UpdateActor</c>, which mutates it as a
/// monster walks/chases/infights) used to be silently dropped on load, so any monster that had moved from
/// its map-defined spawn spot snapped back there on load while incorrectly keeping its (now-restored)
/// alert/chase-memory/infighting state for that moved-from position; version-1/2/3/4 saves still load that
/// way (null means "use this map's deterministic spawn position for every actor", exactly what
/// <c>Game.BuildSpawnState</c> already produces for the candidate actor list), but version-5 saves
/// round-trip each actor's actual position, so a moved monster reloads where it actually was. Unlike the
/// nullable alert/chase-memory coordinate pairs above, every actor always has a position, so these two
/// arrays are each non-nullable <c>double[]</c> once present; only the whole field pair is optional
/// (present together, or both absent together) to stay absent on older saves. There is no separate
/// per-actor facing/angle field: <see cref="Game.Actor"/> never stores one (monsters do not have a
/// persisted facing distinct from their position), so there is nothing else live to round-trip here.
/// </summary>
public sealed record SaveGameState(
    int Version, string Map, double PositionX, double PositionY, double Angle, double Feet,
    int Health, int Armor, int Bullets, int Shells, int Rockets,
    bool HasShotgun, bool HasChaingun, bool HasRocketLauncher, Weapon SelectedWeapon,
    int Keys, int Kills, int Secrets, double Time, int[] FoundSecretSectors,
    bool[] ActorRemoved, int[] ActorHealth, int Items = 0,
    bool[]? ActorAwake = null, double?[]? ActorAlertX = null, double?[]? ActorAlertY = null,
    double[]? ActorChaseMemory = null, double?[]? ActorLastKnownTargetX = null,
    double?[]? ActorLastKnownTargetY = null, int[]? ActorTargetIndex = null,
    double[]? ActorPositionX = null, double[]? ActorPositionY = null);

[JsonSerializable(typeof(SaveGameState))]
public sealed partial class SaveJsonContext : JsonSerializerContext;
