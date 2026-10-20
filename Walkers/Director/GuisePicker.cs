using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Walkers.Config;

namespace Walkers.Director;

/// <summary>
/// Deer country (creature bible, section 8): the deer type a walker wears for a whole haunt is one that
/// lives where its victim is, because a doe where only gazelles roam would give it away. A type is a
/// candidate only when the yearly climate at the victim's column lies inside its vanilla doe's spawn
/// range with forms.guisePickMarginC and forms.guisePickMarginRain to spare. Of the candidates, the one
/// most numerous among real wild deer within forms.guiseCensusRadius wins, then the one that also fits
/// the forest and height ranges at most of 9 samples around the victim, then any. With no candidate the
/// victim is not in deer country, unless forms.guiseNoFit names a type to wear anyway.
/// </summary>
public static class GuisePicker
{
    /// <summary>The type of a guise body with no recorded type.</summary>
    public const string Fallback = "whitetail";

    /// <summary>The guise variants the JSON prototype builds, in its own order.</summary>
    public static readonly string[] AllTypes =
    {
        "whitetail", "redbrocket", "marsh", "caribou", "water", "pudu", "elk", "taruca", "chital", "guemal", "pampas", "fallow",
    };

    private const string AutoType = "auto";
    private const string PauseNoFit = "pause";

    /// <summary>Distance between the 3 by 3 samples of the forest and height test.</summary>
    private const int SampleSpacing = 24;

    /// <summary>
    /// The deer type for a haunt starting at <paramref name="pos"/>, or null when no type lives there
    /// and forms.guiseNoFit is "pause".
    /// </summary>
    public static string? Pick(ICoreServerAPI sapi, BlockPos pos, FormsConfig forms)
    {
        var candidates = Candidates(sapi, pos, forms, out bool skipClimate);
        if (candidates.Count == 0)
        {
            return NoFitType(sapi, forms);
        }
        if (candidates.Count == 1 || skipClimate)
        {
            return candidates[0];
        }

        if (forms.GuiseCensusRadius > 0)
        {
            var census = Census(sapi, pos, forms.GuiseCensusRadius);
            var counted = candidates.Where(t => census.GetValueOrDefault(t) > 0).ToList();
            if (counted.Count > 0)
            {
                int most = counted.Max(t => census[t]);
                var top = counted.Where(t => census[t] == most).ToList();
                return top[sapi.World.Rand.Next(top.Count)];
            }
        }

        var fits = candidates.ToDictionary(t => t, t => ForestAndHeightFits(sapi, pos, t));
        int best = fits.Values.Max();
        var pool = best > 0 ? candidates.Where(t => fits[t] == best).ToList() : candidates;
        return pool[sapi.World.Rand.Next(pool.Count)];
    }

    /// <summary>
    /// Whether some deer type lives at <paramref name="pos"/> (or forms.guiseNoFit names one): the
    /// director rolls no haunt for a player outside deer country.
    /// </summary>
    public static bool InDeerCountry(ICoreServerAPI sapi, BlockPos pos, FormsConfig forms) =>
        Candidates(sapi, pos, forms, out _).Count > 0 || NoFitType(sapi, forms) != null;

    /// <summary>
    /// Whether a travelling victim has gone too far out of their walker's type's country: more than
    /// forms.guiseKeepMarginC or forms.guiseKeepMarginRain past its range. A type worn by forms.guiseNoFit
    /// or picked from a single-type list never leaves its country.
    /// </summary>
    public static bool OutOfCountry(ICoreServerAPI sapi, BlockPos pos, string type, FormsConfig forms)
    {
        if (forms.GuiseKeepMarginC < 0 || IsForced(sapi, type, forms))
        {
            return false;
        }
        var climate = Climate(sapi, pos);
        var conds = SpawnClimate(sapi, type);
        if (climate == null || conds == null)
        {
            return false;
        }
        return !Within(conds, climate, -forms.GuiseKeepMarginC, -forms.GuiseKeepMarginRain);
    }

    /// <summary>
    /// The types that may be worn here. A single configured type skips the climate test
    /// (<paramref name="skipClimate"/>); otherwise only types whose climate fits with the pick margins.
    /// </summary>
    private static List<string> Candidates(ICoreServerAPI sapi, BlockPos pos, FormsConfig forms, out bool skipClimate)
    {
        var allowed = Allowed(sapi, forms.GuiseTypes);
        skipClimate = allowed.Count == 1 && !IsAuto(forms.GuiseTypes);
        if (skipClimate || allowed.Count == 0)
        {
            return allowed;
        }
        var climate = Climate(sapi, pos);
        if (climate == null)
        {
            return new List<string>();
        }
        return allowed.Where(t => SpawnClimate(sapi, t) is { } conds && Within(conds, climate, forms.GuisePickMarginC, forms.GuisePickMarginRain)).ToList();
    }

    /// <summary>
    /// The climate lies inside the range shrunk by the margins (a negative margin widens it). Vanilla's
    /// own test, MatchesClimate, reads WorldGenTemperature and Rainfall (SpawnConditions.cs:123).
    /// </summary>
    private static bool Within(ClimateSpawnCondition conds, ClimateCondition climate, float marginC, float marginRain) =>
        climate.WorldGenTemperature >= conds.MinTemp + marginC && climate.WorldGenTemperature <= conds.MaxTemp - marginC &&
        climate.Rainfall >= conds.MinRain + marginRain && climate.Rainfall <= conds.MaxRain - marginRain;

    /// <summary>The type forms.guiseNoFit wears where none fits, or null for "pause" or a type with no guise.</summary>
    private static string? NoFitType(ICoreServerAPI sapi, FormsConfig forms)
    {
        var type = forms.GuiseNoFit?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(type) || type == PauseNoFit || !HasGuise(sapi, type) ? null : type;
    }

    /// <summary>The type is worn whatever the climate: the forms.guiseNoFit type, or the one type of a single-type list.</summary>
    private static bool IsForced(ICoreServerAPI sapi, string type, FormsConfig forms) =>
        type == NoFitType(sapi, forms) || (!IsAuto(forms.GuiseTypes) && Allowed(sapi, forms.GuiseTypes) is { Count: 1 } only && only[0] == type);

    /// <summary>Living wild deer within the radius, counted by type (game:deer-{type}-*).</summary>
    private static Dictionary<string, int> Census(ICoreServerAPI sapi, BlockPos pos, int radius)
    {
        var counts = new Dictionary<string, int>();
        var center = pos.ToVec3d().Add(0.5, 0, 0.5);
        foreach (var entity in sapi.World.GetEntitiesAround(center, radius, radius, e => e.Alive && e.Code?.Domain == "game" && e.Code.Path.StartsWith("deer-", StringComparison.Ordinal)))
        {
            var parts = entity.Code.Path.Split('-');
            if (parts.Length >= 2)
            {
                counts[parts[1]] = counts.GetValueOrDefault(parts[1]) + 1;
            }
        }
        return counts;
    }

    /// <summary>How many of 9 samples around the victim fit the type's forest and height ranges.</summary>
    private static int ForestAndHeightFits(ICoreServerAPI sapi, BlockPos pos, string type)
    {
        var conds = SpawnClimate(sapi, type);
        if (conds == null)
        {
            return 0;
        }
        var accessor = sapi.World.BlockAccessor;
        int fits = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                var sample = new BlockPos(pos.X + dx * SampleSpacing, pos.Y, pos.Z + dz * SampleSpacing, pos.dimension);
                if (accessor.GetMapChunkAtBlockPos(sample) == null)
                {
                    continue;
                }
                var climate = Climate(sapi, sample);
                double relY = RelativeHeight(sapi, accessor.GetTerrainMapheightAt(sample));
                if (climate != null && conds.MatchesForestation(climate) && relY >= conds.MinY && relY <= conds.MaxY)
                {
                    fits++;
                }
            }
        }
        return fits;
    }

    /// <summary>
    /// The yearly climate of the column, read as vanilla's spawn test reads it: in WorldGenValues mode at
    /// 9% above sea level, so the spot's own height does not change it (ServerSystemEntitySpawner.cs:548).
    /// </summary>
    private static ClimateCondition? Climate(ICoreServerAPI sapi, BlockPos pos)
    {
        var climatePos = new BlockPos(pos.X, (int)(sapi.World.SeaLevel * 1.09), pos.Z, pos.dimension);
        return sapi.World.BlockAccessor.GetClimateAt(climatePos, EnumGetClimateMode.WorldGenValues);
    }

    private static bool IsAuto(string[]? guiseTypes) =>
        guiseTypes == null || guiseTypes.Length == 0 || guiseTypes.Any(t => string.Equals(t, AutoType, StringComparison.OrdinalIgnoreCase));

    private static bool HasGuise(ICoreServerAPI sapi, string type) =>
        sapi.World.GetEntityType(new AssetLocation(Core.WalkersModSystem.ModId, DirectorSystem.GuiseCodePrefix + type)) != null;

    /// <summary>The allowed types that have a walker guise entity, in config order.</summary>
    private static List<string> Allowed(ICoreServerAPI sapi, string[]? guiseTypes)
    {
        IEnumerable<string> wanted = IsAuto(guiseTypes) ? AllTypes : guiseTypes!.Select(t => t.Trim().ToLowerInvariant());
        return wanted.Distinct().Where(t => HasGuise(sapi, t)).ToList();
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
