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
/// </summary>
public sealed record SaveGameState(
    int Version, string Map, double PositionX, double PositionY, double Angle, double Feet,
    int Health, int Armor, int Bullets, int Shells, int Rockets,
    bool HasShotgun, bool HasChaingun, bool HasRocketLauncher, Weapon SelectedWeapon,
    int Keys, int Kills, int Secrets, double Time, int[] FoundSecretSectors,
    bool[] ActorRemoved, int[] ActorHealth, int Items = 0,
    bool[]? ActorAwake = null, double[]? ActorAlertX = null, double[]? ActorAlertY = null);

[JsonSerializable(typeof(SaveGameState))]
public sealed partial class SaveJsonContext : JsonSerializerContext;
