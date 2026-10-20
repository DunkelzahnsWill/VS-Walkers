using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Walkers.Harassment;

/// <summary>
/// The vanilla sounds the acts use, from the Art and sound thread's sound list (stand-ins where
/// vanilla has nothing better, such as the breath), and the two ways to play them: positional for
/// everyone in range, or for the victim alone.
/// </summary>
public static class HarassSounds
{
    public static readonly AssetLocation KnockBody = new("game", "sounds/block/planks");
    public static readonly AssetLocation KnockClick = new("game", "sounds/block/stickhit");
    public static readonly AssetLocation Extinguish = new("game", "sounds/effect/extinguish");
    public static readonly AssetLocation Scrape = new("game", "sounds/player/chalkdraw3");
    public static readonly AssetLocation WoodCreak = new("game", "sounds/block/creak/woodcreak_long3");
    public static readonly AssetLocation BreathOut = new("game", "sounds/block/bellowslarge/bellowlarge-out3");
    public static readonly AssetLocation BreathIn = new("game", "sounds/block/bellowslarge/bellowlarge-in1");
    public static readonly AssetLocation Sniff = new("game", "sounds/creature/hooved/generic/sniff");
    public static readonly AssetLocation DoorSound = new("game", "sounds/block/door");
    public static readonly AssetLocation LargeDoorClose = new("game", "sounds/block/largedoor-close");
    public static readonly AssetLocation Eat = new("game", "sounds/player/eat");
    public static readonly AssetLocation DeerCall = new("game", "sounds/creature/hooved/medium/deer-call");
    public static readonly AssetLocation SmallCall = new("game", "sounds/creature/hooved/small/smallscream");
    public static readonly AssetLocation ElkBellow = new("game", "sounds/creature/animal/mammal/hooved/deer/elk-bellow");
    public static readonly AssetLocation StepFallback = new("game", "sounds/walk/grass");
    public static readonly AssetLocation BuildFallback = new("game", "sounds/player/build");
    public static readonly AssetLocation DestructFallback = new("game", "sounds/player/destruct");

    /// <summary>A positional sound everyone in range hears, the victim included (no player is left out).</summary>
    public static void At(IWorldAccessor world, AssetLocation sound, Vec3d pos, float pitch, float range, float volume = 1) =>
        world.PlaySoundAt(sound, pos.X, pos.Y, pos.Z, null, pitch, range, volume);

    /// <summary>
    /// A sound for the victim alone, with no position (the client plays it inside the head), scaled by
    /// their scareVolume. A stand-in has no ears; nothing is sent.
    /// </summary>
    public static void For(IServerPlayer? player, AssetLocation sound, float pitch, float volume)
    {
        if (player == null)
        {
            return;
        }
        float scale = player.GetWalkersPrefsVolume();
        if (scale <= 0)
        {
            return;
        }
        player.Entity.World.PlaySoundFor(sound, player, pitch, 32, volume * scale);
    }

    private static float GetWalkersPrefsVolume(this IServerPlayer player) => Config.WalkersPlayerPrefsExtensions.GetWalkersPrefs(player).ScareVolume;
}
