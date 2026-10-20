using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Walkers.Director;

namespace Walkers.Forms;

/// <summary>
/// The parts of a walker's memory (entity.Attributes "walkers") that forms reads and writes. The
/// walkers/form subtree is ours; the stage is top level and the walkers/ai subtree is the stalker AI's,
/// read only here. Bodies the director placed share their tree with the director's record, so every
/// write lands in the record at once.
/// </summary>
public static class FormMemory
{
    // walkers/form, written by forms only.
    public const string TrueShare = "trueShare";
    public const string TrueShareDay = "trueShareDay";
    public const string Revealed = "revealed";
    public const string GuiseType = "guiseType";
    public const string LastShiftMs = "lastShiftMs";
    public const string IgniterUid = "igniterUid";
    public const string NightRollDay = "nightRollDay";
    public const string NightRollShifts = "nightRollShifts";

    /// <summary>Top level, written by the director: the escalation level, 1 (Interloping) to 5 (Wrath).</summary>
    public const string EscalationKey = "escalation";

    // walkers/ai, written by the stalker AI.
    public const string AiWatched = "watched";
    public const string AiVictimWatching = "victimWatching";
    public const string AiBreakOff = "breakOff";
    public const string AiSilhouettesTonight = "silhouettesTonight";

    /// <summary>Entity code path (walkers domain) of the true form; every guise is <see cref="GuisePrefix"/> plus a deer type.</summary>
    public const string TrueFormCode = "skinwalker-true";
    public const string GuisePrefix = "skinwalker-animal-";
    public const string DefaultGuise = "whitetail";

    public static bool IsTrueForm(Entity entity) => entity.Code?.Path == TrueFormCode;

    public static bool IsGuise(Entity entity) => entity.Code?.Path.StartsWith(GuisePrefix, StringComparison.Ordinal) == true;

    /// <summary>The walker's memory on this body, created empty when the body has none (a command spawn without a director).</summary>
    public static ITreeAttribute Tree(Entity entity)
    {
        if (entity.Attributes[WalkerMemory.TreeKey] is ITreeAttribute tree)
        {
            return tree;
        }
        tree = new TreeAttribute();
        entity.Attributes[WalkerMemory.TreeKey] = tree;
        return tree;
    }

    public static ITreeAttribute Form(Entity entity) => Tree(entity).GetOrAddTreeAttribute(WalkerMemory.Form);

    /// <summary>The stalker AI's subtree, or null while no stalker AI has written one.</summary>
    public static ITreeAttribute? Ai(Entity entity) => Tree(entity).GetTreeAttribute(WalkerMemory.Ai);

    public static int Stage(Entity entity) => Tree(entity).GetInt(WalkerMemory.Stage);

    /// <summary>The escalation level, 1 to 5; 1 while the director has written none.</summary>
    public static int Escalation(Entity entity) => Math.Max(1, Tree(entity).GetInt(EscalationKey, 1));

    /// <summary>
    /// The deer type the guise wears: the director's choice, else the one this walker last wore, else
    /// the whitetail.
    /// </summary>
    public static string Guise(Entity entity)
    {
        var tree = Tree(entity);
        var type = tree.GetString(WalkerMemory.Guise);
        if (string.IsNullOrEmpty(type))
        {
            type = Form(entity).GetString(GuiseType);
        }
        return string.IsNullOrEmpty(type) ? DefaultGuise : type!;
    }

    public static AssetLocation GuiseCode(string type) => new(Core.WalkersModSystem.ModId, GuisePrefix + type);

    public static AssetLocation TrueCode => new(Core.WalkersModSystem.ModId, TrueFormCode);
}
