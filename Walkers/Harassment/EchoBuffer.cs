using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Walkers.Harassment;

/// <summary>What a victim did to a block, remembered so the echo act can play it back.</summary>
public enum EchoKind
{
    Used,
    Broke,
    Placed,
}

/// <summary>One remembered block action: the block, the sound it made and when.</summary>
public readonly record struct EchoEntry(EchoKind Kind, int BlockId, BlockPos Pos, AssetLocation? Sound, double TotalHours);

/// <summary>
/// A ring buffer of a player's last harass.echoes.buffer block actions (creature bible, section 9,
/// performance budget), fed by DidUseBlock, DidBreakBlock and DidPlaceBlock. Kept for every online
/// player, because a haunt may begin at any time; a few hundred bytes each.
/// </summary>
public sealed class EchoBuffer
{
    private readonly LinkedList<EchoEntry> entries = new();

    public int Count => entries.Count;

    public IEnumerable<EchoEntry> Entries => entries;

    public void Add(EchoEntry entry, int capacity)
    {
        entries.AddLast(entry);
        while (entries.Count > capacity)
        {
            entries.RemoveFirst();
        }
    }

    /// <summary>A random entry with a sound from the last <paramref name="withinHours"/> in-game hours; null if none.</summary>
    public EchoEntry? Pick(System.Random rand, double nowHours, double withinHours)
    {
        var candidates = new List<EchoEntry>();
        foreach (var entry in entries)
        {
            if (entry.Sound != null && nowHours - entry.TotalHours <= withinHours)
            {
                candidates.Add(entry);
            }
        }
        return candidates.Count == 0 ? null : candidates[rand.Next(candidates.Count)];
    }

    public void Clear() => entries.Clear();
}
