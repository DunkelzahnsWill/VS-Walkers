using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Walkers.Director;

namespace Walkers.Forms;

/// <summary>
/// <c>/walkers forms test</c>: runs the shift, the guise's death, the silver and fire kills and the
/// burned body on bodies it spawns at the world spawn, with no player needed, and removes them again.
/// tests/smoke/forms.commands runs it; forms.expect checks the log lines.
/// </summary>
public class FormsSelfTest
{
    private readonly FormsModSystem forms;
    private readonly List<Entity> spawned = new();
    private readonly List<string> lines = new();
    private int passed;
    private int failed;

    public FormsSelfTest(FormsModSystem forms)
    {
        this.forms = forms;
    }

    private IWorldAccessor World => forms.Sapi.World;

    public TextCommandResult Run(TextCommandCallingArgs args)
    {
        lines.Clear();
        spawned.Clear();
        passed = failed = 0;
        var origin = World.DefaultSpawnPosition.AsBlockPos;
        if (World.BlockAccessor.GetChunkAtBlockPos(origin) == null)
        {
            return TextCommandResult.Error("Forms self-test: the world spawn is not loaded");
        }
        var spots = FindSpots(origin, 5);
        if (spots.Count < 5)
        {
            return TextCommandResult.Error("Forms self-test: found only " + spots.Count + " open spots near the world spawn");
        }
        try
        {
            TestShift(spots[0]);
            TestGuiseDeath(spots[1]);
            TestSilver(spots[2]);
            TestFire(spots[3]);
            TestBurnedBody(spots[4]);
            TestSilverItems();
        }
        catch (Exception e)
        {
            Fail("exception", e.ToString());
        }
        finally
        {
            CleanUp(origin);
        }
        Log($"Forms self-test: {passed} passed, {failed} failed");
        return failed == 0 ? TextCommandResult.Success(string.Join("\n", lines)) : TextCommandResult.Error(string.Join("\n", lines));
    }

    private void TestShift(Vec3d at)
    {
        var guise = Spawn(FormMemory.GuiseCode("elk"), at);
        var tree = FormMemory.Tree(guise);
        tree.SetInt(WalkerMemory.Stage, 3);
        var body = WalkerShift.Shift(forms, guise, "self-test", out var failure);
        if (body == null || !FormMemory.IsTrueForm(body) || guise.Alive)
        {
            Fail("shift guise to true form", failure ?? "wrong body");
            return;
        }
        spawned.Add(body);
        body.GetBehavior<EntityBehaviorWalkerForm>()!.CheckNow();
        var health = body.GetBehavior<EntityBehaviorHealth>();
        if (health == null || Math.Abs(health.MaxHealth - forms.Config.Forms.TrueHealth) > 0.01)
        {
            Fail("true form health", $"max health {health?.MaxHealth}");
            return;
        }
        var back = WalkerShift.Shift(forms, body, "self-test", out failure);
        if (back == null || back.Code.Path != FormMemory.GuisePrefix + "elk" || FormMemory.Stage(back) != 3)
        {
            Fail("shift back to the guise", failure ?? back?.Code.Path ?? "no body");
            return;
        }
        spawned.Add(back);
        Pass("shift elk guise -> true form -> elk guise, memory carried");
    }

    private void TestGuiseDeath(Vec3d at)
    {
        var guise = Spawn(FormMemory.GuiseCode("whitetail"), at);
        guise.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Fall, Type = EnumDamageType.Gravity }, 1000);
        var body = World.GetEntitiesAround(at, 2, 3, e => FormMemory.IsTrueForm(e) && !e.Alive).FirstOrDefault();
        if (guise.Alive || body == null || body.GetBehavior<EntityBehaviorWalkerForm>()?.AwaitsRitual != true)
        {
            Fail("guise death", body == null ? "no true body in its place" : "the body does not await the ritual");
            return;
        }
        spawned.Add(body);
        Pass("a guise that fell to its death left the true body, which awaits the ritual");
    }

    private void TestSilver(Vec3d at)
    {
        var body = Spawn(FormMemory.TrueCode, at);
        var arrow = World.GetItem(new AssetLocation("game", "arrow-silver"));
        body.ReceiveDamage(new WalkersTestDamageSource { Source = EnumDamageSource.Entity, Type = EnumDamageType.PiercingAttack, Weapon = arrow == null ? null : new ItemStack(arrow) }, 1000);
        Expect(body, false, "silver kill banishes");
    }

    private void TestFire(Vec3d at)
    {
        var body = Spawn(FormMemory.TrueCode, at);
        body.Ignite();
        body.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Unknown, Type = EnumDamageType.BluntAttack }, 1000);
        Expect(body, false, "a kill while it burns banishes");
    }

    private void TestBurnedBody(Vec3d at)
    {
        var body = Spawn(FormMemory.TrueCode, at);
        body.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Unknown, Type = EnumDamageType.BluntAttack }, 1000);
        if (!Expect(body, true, "an ordinary kill leaves the body for the burn window"))
        {
            return;
        }
        body.Ignite();
        body.GetBehavior<EntityBehaviorWalkerForm>()!.CheckNow();
        Expect(body, false, "burning the body in time banishes");
    }

    private void TestSilverItems()
    {
        var ritual = forms.Config.Ritual;
        var missing = new List<string>();
        foreach (var code in ritual.SilverItems.Append("walkers:spear-silver"))
        {
            var item = World.GetItem(new AssetLocation(code));
            if (item == null || !WalkerRitual.IsSilver(new ItemStack(item), ritual))
            {
                missing.Add(code);
            }
        }
        var copper = World.GetItem(new AssetLocation("game", "arrow-copper"));
        if (copper != null && WalkerRitual.IsSilver(new ItemStack(copper), ritual))
        {
            missing.Add("game:arrow-copper counts as silver");
        }
        if (missing.Count > 0)
        {
            Fail("silver items", string.Join(", ", missing));
        }
        else
        {
            Pass("every silver item counts as silver, a copper arrow does not");
        }
    }

    private bool Expect(Entity body, bool awaits, string name)
    {
        var form = body.GetBehavior<EntityBehaviorWalkerForm>();
        if (body.Alive || form == null || form.AwaitsRitual != awaits)
        {
            Fail(name, body.Alive ? "still alive" : "the body " + (awaits ? "does not await" : "still awaits") + " the ritual");
            return false;
        }
        Pass(name);
        return true;
    }

    private Entity Spawn(AssetLocation code, Vec3d at)
    {
        var type = World.GetEntityType(code) ?? throw new InvalidOperationException("no entity type " + code);
        var entity = World.ClassRegistry.CreateEntity(type);
        entity.Pos.SetPos(at);
        World.SpawnEntity(entity);
        spawned.Add(entity);
        return entity;
    }

    /// <summary>Spots where the true form fits, standing on the ground, a few blocks apart near <paramref name="origin"/>.</summary>
    private List<Vec3d> FindSpots(BlockPos origin, int count)
    {
        var box = World.GetEntityType(FormMemory.TrueCode)?.SpawnCollisionBox;
        var accessor = World.BlockAccessor;
        var spots = new List<Vec3d>();
        for (int dx = -24; dx <= 24 && spots.Count < count; dx += 4)
        {
            for (int dz = -24; dz <= 24 && spots.Count < count; dz += 4)
            {
                var column = new BlockPos(origin.X + dx, 0, origin.Z + dz, origin.dimension);
                if (accessor.GetChunkAtBlockPos(column) == null)
                {
                    continue;
                }
                column.Y = accessor.GetTerrainMapheightAt(column) + 1;
                var at = new Vec3d(column.X + 0.5, column.Y, column.Z + 0.5);
                if (box != null && !World.CollisionTester.IsColliding(accessor, box, at, false) &&
                    !accessor.GetBlock(column).IsLiquid() && !accessor.GetBlock(column.DownCopy()).IsLiquid())
                {
                    spots.Add(at);
                }
            }
        }
        return spots;
    }

    private void CleanUp(BlockPos origin)
    {
        foreach (var entity in spawned)
        {
            if (entity.Alive)
            {
                entity.Die(EnumDespawnReason.Removed);
            }
            else if (entity is EntityAgent agent)
            {
                agent.AllowDespawn = true;
            }
        }
        // The eyes and pages the banishings dropped.
        foreach (var item in World.GetEntitiesAround(origin.ToVec3d(), 40, 40, e => e is EntityItem item && item.Itemstack?.Collectible?.Code is { } c &&
                     (c.Domain == Core.WalkersModSystem.ModId || c.Path == "lore-paper")))
        {
            item.Die(EnumDespawnReason.Removed);
        }
    }

    private void Pass(string name)
    {
        passed++;
        Log("Forms self-test: " + name + ": ok");
    }

    private void Fail(string name, string why)
    {
        failed++;
        Log("Forms self-test: " + name + ": FAILED (" + why + ")");
    }

    private void Log(string line)
    {
        lines.Add(line);
        forms.Mod.Logger.Notification(line);
    }
}
