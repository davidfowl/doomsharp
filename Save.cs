using System.Text.Json.Serialization;

namespace DoomSharp;

/// <summary>
/// A serializable snapshot of in-progress play: current map, player position/stats/inventory and the
/// removed/health state of every actor spawned on that map (in <see cref="Game.Spawn"/> iteration order,
/// which is deterministic for a given map's <c>Things</c> list). In-flight projectiles are not preserved;
/// loading always resumes with no projectiles airborne, matching a player standing still to save.
/// </summary>
public sealed record SaveGameState(
    int Version, string Map, double PositionX, double PositionY, double Angle, double Feet,
    int Health, int Armor, int Bullets, int Shells, int Rockets,
    bool HasShotgun, bool HasChaingun, bool HasRocketLauncher, Weapon SelectedWeapon,
    int Keys, int Kills, int Secrets, double Time, int[] FoundSecretSectors,
    bool[] ActorRemoved, int[] ActorHealth);

[JsonSerializable(typeof(SaveGameState))]
public sealed partial class SaveJsonContext : JsonSerializerContext;
