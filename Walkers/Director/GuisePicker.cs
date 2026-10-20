using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Walkers.Director;

/// <summary>
/// Picks the deer type a walker wears for a whole haunt (creature bible, section 8): the type whose
/// vanilla spawn climate fits the victim's position, so winter never swaps a whitetail for a caribou.
/// </summary>
public static class GuisePicker
{
    /// <summary>The type worn when none fits, and the type of a guise with no recorded type.</summary>
    public const string Fallback = "whitetail";

    /// <summary>The guise variants the JSON prototype builds, in its own order.</summary>
    public static readonly string[] AllTypes =
    {
        "whitetail", "redbrocket", "marsh", "caribou", "water", "pudu", "elk", "taruca", "chital", "guemal", "pampas", "fallow",
    };

    private const string AutoType = "auto";

    /// <summary>
    /// The deer type for a haunt at <paramref name="pos"/>, from forms.guiseTypes: ["auto"] means any of
    /// the twelve. Of the allowed types whose walker guise exists, a type whose vanilla doe would spawn
    /// here wins; then one whose temperature and rain fit (the victim may stand in a clearing); then the
    /// first allowed type, whitetail when allowed.
    /// </summary>
    public static string Pick(ICoreServerAPI sapi, BlockPos pos, string[]? guiseTypes)
    {
        var allowed = Allowed(sapi, guiseTypes);
        if (allowed.Count == 0)
        {
            return Fallback;
        }
        if (allowed.Count == 1)
        {
            return allowed[0];
        }

        var accessor = sapi.World.BlockAccessor;
        // Vanilla reads the climate at 9% above sea level, so the spot's height does not change the temperature
        // (ServerSystemEntitySpawner.cs:548); the height test uses the real height.
        var climatePos = new BlockPos(pos.X, (int)(sapi.World.SeaLevel * 1.09), pos.Z, pos.dimension);
        var climate = accessor.GetClimateAt(climatePos, EnumGetClimateMode.WorldGenValues);
        if (climate == null)
        {
            return allowed.Contains(Fallback) ? Fallback : allowed[0];
        }
        double relY = RelativeHeight(sapi, pos.Y);

        var full = new List<string>();
        var climateOnly = new List<string>();
        foreach (var type in allowed)
        {
            var conds = SpawnClimate(sapi, type);
            if (conds == null || !conds.MatchesClimate(climate))
            {
                continue;
            }
            if (conds.MatchesForestation(climate) && relY >= conds.MinY && relY <= conds.MaxY)
            {
                full.Add(type);
            }
            else
            {
                climateOnly.Add(type);
            }
        }
        var pool = full.Count > 0 ? full : climateOnly;
        if (pool.Count > 0)
        {
            return pool[sapi.World.Rand.Next(pool.Count)];
        }
        return allowed.Contains(Fallback) ? Fallback : allowed[0];
    }

    /// <summary>The allowed types that have a walker guise entity, in config order.</summary>
    private static List<string> Allowed(ICoreServerAPI sapi, string[]? guiseTypes)
    {
        IEnumerable<string> wanted = guiseTypes == null || guiseTypes.Length == 0 || guiseTypes.Any(t => string.Equals(t, AutoType, StringComparison.OrdinalIgnoreCase))
            ? AllTypes
            : guiseTypes.Select(t => t.Trim().ToLowerInvariant());
        return wanted
            .Distinct()
            .Where(t => sapi.World.GetEntityType(new AssetLocation(Core.WalkersModSystem.ModId, DirectorSystem.GuiseCodePrefix + t)) != null)
            .ToList();
    }

    /// <summary>The spawn climate of the vanilla doe of this type (game:deer-{type}-adult-female), or null.</summary>
    private static ClimateSpawnCondition? SpawnClimate(ICoreServerAPI sapi, string type)
    {
        var doe = sapi.World.GetEntityType(new AssetLocation("game", "deer-" + type + "-adult-female"));
        var conds = doe?.Server?.SpawnConditions;
        // Clone() drops Climate, but the loaded type copies it into Runtime and Worldgen (SpawnConditions.cs:224).
        return conds?.Runtime ?? conds?.Worldgen ?? (ClimateSpawnCondition?)conds?.Climate;
    }

    /// <summary>Height as vanilla spawn rules measure it: y over sea level below it, 1 plus the share of the sky above.</summary>
    private static double RelativeHeight(ICoreServerAPI sapi, int y)
    {
        int seaLevel = sapi.World.SeaLevel;
        int sky = Math.Max(1, sapi.World.BlockAccessor.MapSizeY - seaLevel);
        return y > seaLevel ? 1 + (double)(y - seaLevel) / sky : (double)y / Math.Max(1, seaLevel);
    }
}
