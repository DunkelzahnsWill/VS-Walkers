using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Walkers.Commands;
using Walkers.Config;
using Walkers.Core;
using Walkers.Director;

namespace Walkers.Forms;

/// <summary>
/// Forms and the kill ritual: registers the walkers-form and walkers-husk entity behaviors (both
/// sides, since the husk answers the knife on the client too), and on the server notes the weapon of
/// every attack on a walker, adds <c>/walkers forms</c> and a <c>/walkers status</c> line. The behaviors
/// do the work; see <see cref="EntityBehaviorWalkerForm"/>.
/// </summary>
public class FormsModSystem : ModSystem
{
    /// <summary>The gaze cone's half angle, read from gaze.coneDegrees when our own sight check stands in for the stalker AI's.</summary>
    private double coneCos = Math.Cos(55 * GameMath.DEG2RAD);

    public ICoreServerAPI Sapi { get; private set; } = null!;

    public WalkersConfig Config => Sapi.GetWalkersConfig();

    /// <summary>The director, while one is loaded; null otherwise, and the outcomes are only logged.</summary>
    public IWalkerDirector? Director => Sapi.GetWalkersDirector();

    private FormsSelfTest? selfTest;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorWalkerForm.Code, typeof(EntityBehaviorWalkerForm));
        api.RegisterEntityBehaviorClass(EntityBehaviorWalkerHusk.Code, typeof(EntityBehaviorWalkerHusk));
    }

    /// <summary>
    /// Adds our behaviors to both walker types. A JSON patch cannot do it: the entity files open with a
    /// comment, which the patch loader takes for the whole document (JsonPatchLoader.cs:445). The server
    /// sends entity types to clients after this, client behaviors included, as vanilla relies on for
    /// harvestable drops (BehaviorHarvestable.cs:23).
    /// </summary>
    public override void AssetsFinalize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server)
        {
            return;
        }
        foreach (var type in api.World.EntityTypes)
        {
            if (type.Code?.Domain != WalkersModSystem.ModId || !type.Code.Path.StartsWith(WalkersCommandsSystem.CreaturePrefix, StringComparison.Ordinal))
            {
                continue;
            }
            Append(type.Server, EntityBehaviorWalkerForm.Code);
            if (type.Code.Path == FormMemory.TrueFormCode)
            {
                Append(type.Server, EntityBehaviorWalkerHusk.Code);
                Append(type.Client, EntityBehaviorWalkerHusk.Code);
            }
        }
    }

    private static void Append(EntitySidedProperties? side, string code)
    {
        if (side == null || side.BehaviorsAsJsonObj.Any(b => b["code"].AsString() == code))
        {
            return;
        }
        side.BehaviorsAsJsonObj = side.BehaviorsAsJsonObj.Append(new JsonObject(new JObject { ["code"] = code })).ToArray();
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Sapi = api;
        selfTest = new FormsSelfTest(this);
        api.Event.OnPlayerInteractEntity += OnPlayerInteractEntity;

        var parsers = api.ChatCommands.Parsers;
        api.ChatCommands.GetOrCreate(WalkersModSystem.ModId)
            .BeginSubCommand("forms")
                .WithDescription(Lang.Get("walkers:forms-commands-desc"))
                .BeginSubCommand("shift")
                    .WithDescription(Lang.Get("walkers:forms-shift-desc"))
                    .WithExamples("/walkers forms shift", "/walkers forms shift 1234")
                    .WithArgs(parsers.OptionalLong("entityId"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(OnShift))
                .EndSubCommand()
                .BeginSubCommand("test")
                    .WithDescription(Lang.Get("walkers:forms-test-desc"))
                    .HandleWith(WalkersCommandsSystem.SafeReply(args => selfTest.Run(args)))
                .EndSubCommand()
            .EndSubCommand();

        api.ModLoader.GetModSystem<WalkersCommandsSystem>().Status += lines =>
        {
            int lying = api.World.LoadedEntities.Values.Count(e => e.GetBehavior<EntityBehaviorWalkerForm>()?.AwaitsRitual == true);
            lines.Add(Lang.Get("walkers:forms-status", lying));
        };
    }

    /// <summary>The weapon in hand when an attack begins: a blow that breaks it empties the hand before the damage lands.</summary>
    private void OnPlayerInteractEntity(Entity entity, IPlayer byPlayer, ItemSlot slot, Vec3d hitPosition, int mode, ref EnumHandling handling)
    {
        if (mode == (int)EnumInteractMode.Attack && entity.GetBehavior<EntityBehaviorWalkerForm>() is { } form)
        {
            form.NoteAttack(byPlayer, slot?.Itemstack);
        }
    }

    private TextCommandResult OnShift(TextCommandCallingArgs args)
    {
        long? id = args.Parsers[0].IsMissing ? null : (long)args[0];
        var bodies = Sapi.World.LoadedEntities.Values
            .Where(e => e.Alive && (id == null || e.EntityId == id) && e.GetBehavior<EntityBehaviorWalkerForm>() != null)
            .ToList();
        if (bodies.Count == 0)
        {
            return TextCommandResult.Error(Lang.GetL(args.LanguageCode, "walkers:forms-shift-none"));
        }
        var lines = new List<string>();
        foreach (var body in bodies)
        {
            var shifted = WalkerShift.Shift(this, body, "command", out var failure);
            lines.Add(shifted == null
                ? Lang.GetL(args.LanguageCode, "walkers:forms-shift-failed", body.Code.Path, body.EntityId, failure)
                : Lang.GetL(args.LanguageCode, "walkers:forms-shifted", body.Code.Path, body.EntityId, shifted.Code.Path, shifted.EntityId));
        }
        Mod.Logger.Audit("{0} shifted {1} walker(s) by command", args.Caller.GetName(), bodies.Count);
        return TextCommandResult.Success(string.Join("\n", lines));
    }

    // ---- Shared checks for the behaviors ----

    /// <summary>
    /// Players who count (creature bible, section 5) within <paramref name="range"/> of the body: the
    /// director's list when it runs, else alive, connected players in survival or guest mode.
    /// </summary>
    public IEnumerable<IServerPlayer> CountingPlayersNear(Entity body, double range)
    {
        var director = Director;
        if (director != null)
        {
            return director.CountingEntitiesNear(body.Pos.XYZ, body.Pos.Dimension, range)
                .OfType<EntityPlayer>().Select(e => e.Player).OfType<IServerPlayer>();
        }
        return Sapi.World.AllOnlinePlayers.OfType<IServerPlayer>().Where(p =>
            p.ConnectionState == EnumClientState.Playing && p.Entity is { Alive: true } pe &&
            p.WorldData.CurrentGameMode is EnumGameMode.Survival or EnumGameMode.Guest &&
            pe.Pos.Dimension == body.Pos.Dimension && pe.Pos.DistanceTo(body.Pos) <= range);
    }

    /// <summary>
    /// Our stand-in for the stalker AI's gaze: the target is inside the viewer's view cone and nothing
    /// solid stands between the viewer's eyes and the target's chest.
    /// </summary>
    public bool Sees(Entity? viewer, Entity target)
    {
        if (viewer == null)
        {
            return false;
        }
        coneCos = Math.Cos(Config.Gaze.ConeDegrees / 2.0 * GameMath.DEG2RAD);
        var eye = viewer.Pos.XYZ.Add(viewer.LocalEyePos);
        var chest = target.Pos.XYZ.Add(0, target.SelectionBox.Y2 * 0.6, 0);
        var to = chest - eye;
        double length = to.Length();
        if (length < 0.5)
        {
            return true;
        }
        var view = viewer.Pos.GetViewVector();
        if ((view.X * to.X + view.Y * to.Y + view.Z * to.Z) / length < coneCos)
        {
            return false;
        }
        var accessor = Sapi.World.BlockAccessor;
        var pos = new BlockPos(viewer.Pos.Dimension);
        for (double step = 0.5; step < length - 0.5; step += 0.5)
        {
            double f = step / length;
            pos.Set((int)Math.Floor(eye.X + to.X * f), (int)Math.Floor(eye.Y + to.Y * f), (int)Math.Floor(eye.Z + to.Z * f));
            var block = accessor.GetBlock(pos);
            if (block.Id != 0 && !block.IsLiquid() && block.CollisionBoxes is { Length: > 0 })
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Night where the body stands: the sun is below the horizon.</summary>
    public bool IsNight(Entity body) =>
        Sapi.World.Calendar.GetSunPosition(body.Pos.XYZ, Sapi.World.Calendar.TotalDays).Y < 0;

    /// <summary>
    /// Stage 4 shows the true form only as a night's silhouette, and only while the stalker AI allows
    /// another one tonight (stalk.silhouettes, stalk.silhouettesPerNight). Read by path, so forms runs
    /// without the stalker AI's settings too.
    /// </summary>
    public bool SilhouetteAllowed(Entity body)
    {
        var config = Config;
        if (WalkersConfigJson.Find(config, "stalk.silhouettes")?.Value is false)
        {
            return false;
        }
        if (WalkersConfigJson.Find(config, "stalk.silhouettesPerNight")?.Value is int perNight)
        {
            return (FormMemory.Ai(body)?.GetInt(FormMemory.AiSilhouettesTonight) ?? 0) < perNight;
        }
        return true;
    }
}
