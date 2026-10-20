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
/// the player), which stays valid across save/load because <c>Spawn</c> rebuilds that list identically.
/// </summary>
public sealed record SaveGameState(
    int Version, string Map, double PositionX, double PositionY, double Angle, double Feet,
    int Health, int Armor, int Bullets, int Shells, int Rockets,
    bool HasShotgun, bool HasChaingun, bool HasRocketLauncher, Weapon SelectedWeapon,
    int Keys, int Kills, int Secrets, double Time, int[] FoundSecretSectors,
    bool[] ActorRemoved, int[] ActorHealth, int Items = 0,
    bool[]? ActorAwake = null, double[]? ActorAlertX = null, double[]? ActorAlertY = null,
    double[]? ActorChaseMemory = null, double[]? ActorLastKnownTargetX = null,
    double[]? ActorLastKnownTargetY = null, int[]? ActorTargetIndex = null);

// AllowNamedFloatingPointLiterals is required because ActorAlertX/ActorAlertY/ActorLastKnownTargetX/
// ActorLastKnownTargetY encode "no position" as double.NaN per-actor; without this option, the default
// writer throws ArgumentException the instant any array contains a NaN, which System.Text.Json otherwise
// rejects as invalid JSON. That made Game.Save() throw for every actor not currently alert/chasing (i.e.
// virtually every real save), a pre-existing latent bug never exercised by prior tests (which only ever
// saved maps with a single already-awake actor) until the chase-memory/infighting save round-trip checks
// below started calling Save() on fixtures with a resting monster.
[JsonSourceGenerationOptions(NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals)]
[JsonSerializable(typeof(SaveGameState))]
public sealed partial class SaveJsonContext : JsonSerializerContext;
