using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Walkers.Stalker.Tasks;

/// <summary>
/// walkers-stare: keeps the body facing its victim, turning the whole body in small steps (the server
/// cannot turn an animal's head on its own). It runs in its own slot beside the task that walks, and
/// turns only while the body stands still. A frozen body does not turn, except a stage 1 doe, which
/// stands and looks back like a deer would (creature bible, section 5). In the defence it faces
/// whoever hit it.
///
/// JSON: <c>stepDegrees</c> (12) per step, <c>stepIntervalMs</c> (600) between steps.
/// </summary>
public class AiTaskWalkerStare : WalkerTaskBase
{
    public const string Code = "walkers-stare";

    [JsonProperty]
    public float StepDegrees = 12;

    [JsonProperty]
    public int StepIntervalMs = 600;

    private long nextStepMs;

    public AiTaskWalkerStare(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig) : base(entity, taskConfig, aiConfig)
    {
    }

    public override bool ShouldExecute()
    {
        var brain = Brain;
        return brain != null && Target(brain) != null && !entity.Swimming && !IsOnCooldown();
    }

    public override void StartExecute()
    {
        base.StartExecute();
        nextStepMs = world.ElapsedMilliseconds + StepIntervalMs;
    }

    public override bool ContinueExecute(float dt)
    {
        var brain = Brain;
        var target = brain == null ? null : Target(brain);
        if (target == null || entity.Swimming)
        {
            return false;
        }
        long now = world.ElapsedMilliseconds;
        if (now < nextStepMs || pathTraverser.Active || (brain!.Frozen && brain.Stage != 1))
        {
            return true;
        }
        nextStepMs = now + StepIntervalMs;
        float step = StepDegrees * GameMath.DEG2RAD;
        float want = YawTo(entity.Pos, target.Pos.XYZ);
        if (System.Math.Abs(GameMath.AngleRadDistance(entity.Pos.Yaw, want)) > step / 2)
        {
            TurnTowards(target.Pos.XYZ, step);
        }
        return true;
    }

    /// <summary>Who it faces: its victim while it stalks or hunts, whoever hit it last while it defends.</summary>
    private static Vintagestory.API.Common.Entities.Entity? Target(WalkerBrain brain)
    {
        switch (brain.Mode)
        {
            case StalkerMode.Stalk:
            case StalkerMode.Silhouette:
            case StalkerMode.Hunt:
                return brain.Victim;
            case StalkerMode.Defend:
                return brain.LastAttacker ?? brain.Victim;
            default:
                return null;
        }
    }
}
