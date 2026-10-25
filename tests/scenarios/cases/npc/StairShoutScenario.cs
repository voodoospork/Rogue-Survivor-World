using System;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay;

static class StairShoutScenario
{
    static readonly Point Bottom = new Point(2, 1);
    static readonly Point Top = new Point(4, 1);

    static ScenarioWorld Arena()
    {
        ScenarioWorld world = TownScenarioFactory.Arena(4532,
            "................", "................", "................", "................");
        Session.Get.GamePreset = GamePreset.World();
        Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
        return world;
    }

    static Map Floor(ScenarioWorld world, int seed)
    {
        Map map = new Map(seed, "floor " + seed, 16, 4);
        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
                map.SetTileModelAt(x, y, world.Game.GameTiles.FLOOR_ASPHALT);
        world.Map.District.AddUniqueMap(map);
        return map;
    }

    static Map Stairs(ScenarioWorld world, string decoration)
    {
        Map upper = Floor(world, 4533);
        world.Map.SetExitAt(Bottom, new Exit(upper, Top));
        upper.SetExitAt(Top, new Exit(world.Map, Bottom));
        if (decoration != null) world.Map.GetTileAt(Bottom.X, Bottom.Y).AddDecoration(decoration);
        upper.GetTileAt(Top.X, Top.Y).AddDecoration(GameImages.DECO_STAIRS_DOWN);
        return upper;
    }

    static Actor Sleeper(ScenarioWorld world, Map floor, Point position, string name)
    {
        Actor actor = NpcIntentSupport.Actor(world, name, 0, 0);
        world.Map.RemoveActor(actor);
        floor.PlaceActorAt(actor, position);
        // Guarantee waking inside the radius; the real chance formula is unchanged.
        for (int i = 0; i < 5; i++) actor.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
        actor.IsSleeping = true;
        return actor;
    }

    static void Shout(ScenarioWorld world)
    {
        Actor player = world.Game.Player;
        Check.Call(world.Game, "UpdatePlayerFOV", player);
        world.Game.ComputeViewRect(player.Location.Position);
        int ap = player.ActionPoints;
        Check.Equal(true, Check.Call(world.Game, "HandlePlayerShout",
            new[] { typeof(Actor), typeof(string) }, player, null), "S performs a shout");
        Check.Equal(ap - Rules.BASE_ACTION_COST, player.ActionPoints, "one shout spends one action");
    }

    public static void Register()
    {
        ScenarioRunner.Add("npc/stair-shout-up", Arena, world =>
        {
            Actor player = NpcIntentSupport.Player(world, Bottom.X, Bottom.Y);
            Map upper = Stairs(world, GameImages.DECO_STAIRS_UP);
            Actor blocker = Sleeper(world, upper, Top, "Blocker");
            Actor edge = Sleeper(world, upper, new Point(Top.X + Rules.LOUD_NOISE_RADIUS, Top.Y), "Edge");
            Actor far = Sleeper(world, upper, new Point(Top.X + Rules.LOUD_NOISE_RADIUS + 1, Top.Y), "Far");
            Actor local = Sleeper(world, world.Map, new Point(Bottom.X + 1, Bottom.Y), "Local");
            Map third = Floor(world, 4534);
            Point nextStair = new Point(Top.X + 1, Top.Y);
            upper.SetExitAt(nextStair, new Exit(third, Top));
            upper.GetTileAt(nextStair.X, nextStair.Y).AddDecoration(GameImages.DECO_STAIRS_UP);
            Actor beyond = Sleeper(world, third, Top, "Third floor");
            Shout(world);
            Check.Equal(false, blocker.IsSleeping, "a shout wakes the survivor blocking the upper landing");
            Check.Equal(false, edge.IsSleeping, "upper floor uses the normal five-tile waking radius");
            Check.Equal(true, far.IsSleeping, "upper floor waking radius stays bounded");
            Check.Equal(false, local.IsSleeping, "same-floor shout still works");
            Check.Equal(true, beyond.IsSleeping, "sound does not relay through another staircase");
            Check.Same(world.Map, player.Location.Map, "shouting does not climb the stairs");
        });
        Boundary("adjacent", GameImages.DECO_STAIRS_UP, true);
        Boundary("down", GameImages.DECO_STAIRS_DOWN, false);
        Boundary("other-exit", null, false);

        ScenarioRunner.Add("npc/stair-shout-speech", Arena, world =>
        {
            Actor player = NpcIntentSupport.Player(world, Bottom.X, Bottom.Y);
            Map upper = Stairs(world, GameImages.DECO_STAIRS_UP);
            Actor blocker = Sleeper(world, upper, Top, "Blocker");
            Actor listener = NpcIntentSupport.Actor(world, "Listener", Bottom.X + 1, Bottom.Y);
            Check.Call(world.Game, "UpdatePlayerFOV", player);
            world.Game.ComputeViewRect(player.Location.Position);
            Check.Call(world.Game, "OnLoudNoise", world.Map, Bottom, "Nearby fighting");
            Check.Equal(true, blocker.IsSleeping, "other loud noises do not gain stair propagation");
            world.Game.DoSay(player, listener, "Hello", RogueGame.Sayflags.NONE);
            Check.Equal(true, blocker.IsSleeping, "ordinary speech does not carry upstairs");
            world.Game.DoSay(player, listener, "Wake up!", RogueGame.Sayflags.IS_SHOUT);
            Check.Equal(false, blocker.IsSleeping, "speech flagged as a shout also carries upstairs");
        });
        HeardAbove("heard-above", 0);
        HeardAbove("deaf-above", 1);
        HeardAbove("beyond-hearing", 2);
    }

    static void Boundary(string name, string decoration, bool adjacent)
    {
        ScenarioRunner.Add("npc/stair-shout-" + name, Arena, world =>
        {
            NpcIntentSupport.Player(world, Bottom.X + (adjacent ? 1 : 0), Bottom.Y);
            Map upper = Stairs(world, decoration);
            Actor blocker = Sleeper(world, upper, Top, "Blocker");
            Shout(world);
            Check.Equal(true, blocker.IsSleeping, "only the exact bottom of an upward staircase carries shouts");
        });
    }

    static void HeardAbove(string name, int hearing)
    {
        ScenarioRunner.Add("npc/stair-shout-" + name, Arena, world =>
        {
            Actor speaker = NpcIntentSupport.Actor(world, "Hidden caller", Bottom.X, Bottom.Y);
            Actor player = NpcIntentSupport.Player(world, Bottom.X + 1, Bottom.Y);
            Map upper = Stairs(world, GameImages.DECO_STAIRS_UP);
            world.Map.RemoveActor(player);
            upper.PlaceActorAt(player, new Point(Top.X + 2, Top.Y));
            Session.Get.CurrentMap = upper;
            if (hearing != 0) player.AudioRangeMod = (hearing == 1 ? 0 : 1) - player.Sheet.BaseAudioRange;
            Check.Call(world.Game, "UpdatePlayerFOV", player);
            world.Game.ComputeViewRect(player.Location.Position);
            world.Game.DoShout(speaker, "Wake up!");
            ScenarioUI ui = (ScenarioUI)world.Game.UI;
            ui.DrawnStrings.Clear();
            Check.Call(world.Game, "DrawMessages");
            string heard = String.Join(" ", ui.DrawnStrings);
            Check.Equal(hearing == 0, heard.Contains("You hear a shout from below."), "upper-floor player hears only within hearing range");
            Check.Equal(false, heard.Contains("Hidden caller"), "unseen voice does not reveal its identity");
        });
    }
}
