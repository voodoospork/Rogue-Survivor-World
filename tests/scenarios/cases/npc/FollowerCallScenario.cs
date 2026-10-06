using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;

static class FollowerCallScenario
{
    static ScenarioWorld Arena(int seed)
    {
        ScenarioWorld world = TownScenarioFactory.Arena(seed,
            "................................", "................................", "................................", "................................", "................................");
        Session.Get.GamePreset = GamePreset.World(); Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
        RogueGame.KeyBindings.ResetToDefaults(); return world;
    }
    static Actor Guard(ScenarioWorld world, Actor player, string name, int x, int y)
    {
        Actor follower = NpcIntentSupport.Actor(world, name, x, y); player.AddFollower(follower);
        ((OrderableAI)follower.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, follower.Location)); return follower;
    }
    static void Call(ScenarioWorld world)
    {
        Check.Call(world.Game, "UpdatePlayerFOV", world.Game.Player); world.Game.ComputeViewRect(world.Game.Player.Location.Position);
        world.Game.Player.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, Check.Call(world.Game, "HandlePlayerShout", new[] { typeof(Actor), typeof(string) }, world.Game.Player, null), "actual S input calls followers");
        Check.Equal(0, world.Game.Player.ActionPoints, "player shout spends exactly one action");
    }
    static string Display(ScenarioWorld world)
    {
        Check.Call(world.Game, "PresentEncounterSpeech");
        ScenarioUI ui = (ScenarioUI)world.Game.UI; ui.DrawnStrings.Clear(); Check.Call(world.Game, "DrawMessages");
        return String.Join(" ", ui.DrawnStrings);
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/follower-call-hidden", () => Arena(4943), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor follower = Guard(world, player, "Sam", 7, 1);
            Actor second = Guard(world, player, "Lee", 1, 3);
            for (int i = 0; i < 3; i++) second.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
            second.IsSleeping = true;
            world.SetTile(4, 1, '#'); Call(world);
            Check.Equal(false, second.IsSleeping, "player shout invokes the normal noise wake-up roll");
            Actor sleeper = NpcIntentSupport.Actor(world, "Sleeper", 7, 2);
            for (int i = 0; i < 3; i++) sleeper.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
            sleeper.IsSleeping = true; Point before = follower.Location.Position;
            NpcIntentSupport.Turn(world, follower);
            NpcIntentSupport.Turn(world, second);
            string heard = Display(world);
            Check.Equal(true, heard.Contains("Sam shouts from the east, not far away: Here!"), "known voice gives auditory direction beyond line of sight");
            Check.Equal(true, heard.Contains("Lee shouts from the south, nearby: Here!"), "follower awakened by the shout answers that same call");
            Check.Equal(false, sleeper.IsSleeping, "reply invokes actual loud-noise waking");
            Check.Equal(before, follower.Location.Position, "reply does not move the guard");
            Check.Equal(ActorTasks.GUARD, ((OrderableAI)follower.Controller).Order.Task, "reply retains guard order");
            Check.Equal(false, follower.Controller.GetAction(world.Game) is ActionFollowerReply, "one player call gets one reply");
        });
        ScenarioRunner.Add("npc/follower-call-boundaries", () => Arena(4944), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1);
            Actor far = Guard(world, player, "Far", 27, 1);
            Actor sleeping = Guard(world, player, "Sleeping", 7, 1); sleeping.IsSleeping = true;
            Check.Equal(10, world.Game.Rules.ActorLoudNoiseWakeupChance(sleeping, 5), "unskilled sleeper has a chance at the five-tile noise boundary");
            Check.Equal(50, world.Game.Rules.ActorLoudNoiseWakeupChance(sleeping, 1), "an adjacent unskilled sleeper has a higher wake-up chance");
            Actor deaf = Guard(world, player, "Deaf", 6, 2); deaf.AudioRangeMod = -deaf.Sheet.BaseAudioRange;
            Actor outsider = NpcIntentSupport.Actor(world, "Outsider", 3, 2);
            Call(world);
            Check.Equal(false, far.Controller.GetAction(world.Game) is ActionFollowerReply, "distant follower did not hear the call");
            Check.Equal(true, sleeping.IsSleeping, "sleeping follower beyond noise radius stays asleep");
            sleeping.IsSleeping = false;
            Check.Equal(false, sleeping.Controller.GetAction(world.Game) is ActionFollowerReply, "follower who slept through the shout did not understand the call");
            Check.Equal(false, deaf.Controller.GetAction(world.Game) is ActionFollowerReply, "follower hearing range is required");
            Check.Equal(false, outsider.Controller.GetAction(world.Game) is ActionFollowerReply, "outsiders do not answer a follower call");
            Actor near = Guard(world, player, "Near", 5, 3); Call(world);
            player.AudioRangeMod = -player.Sheet.BaseAudioRange;
            Check.Equal(false, near.Controller.GetAction(world.Game) is ActionFollowerReply, "reply must also be audible to the player");
            player.AudioRangeMod = 0;
            Session.Get.WorldTime.TurnCounter = 6;
            Check.Equal(false, near.Controller.GetAction(world.Game) is ActionFollowerReply, "an expired request cannot produce a delayed location report");
        });
        ScenarioRunner.Add("npc/follower-call-survival", () => Arena(4945), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor follower = Guard(world, player, "Sam", 5, 1);
            // Remove the standing order so ordinary urgent eating can run before the reply.
            ((OrderableAI)follower.Controller).SetOrder(null);
            follower.FoodPoints = 0; ItemFood food = NpcIntentSupport.Food(world, follower, 1); Call(world);
            NpcIntentSupport.Turn(world, follower);
            Check.Equal(false, follower.Inventory.Contains(food), "urgent eating precedes answering");
            NpcIntentSupport.Turn(world, follower);
            Check.Equal(true, Display(world).Contains("Sam shouts"), "follower answers after urgent eating");
        });
        ScenarioRunner.Add("npc/follower-call-combat", () => Arena(4946), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor follower = Guard(world, player, "Sam", 5, 1); Call(world);
            Actor enemy = world.Game.GameActors.Zombie.CreateAnonymous(world.Game.GameFactions.TheUndeads, 0);
            world.Map.PlaceActorAt(enemy, new Point(6, 1));
            Check.Equal(false, follower.Controller.GetAction(world.Game) is ActionFollowerReply, "visible combat threat takes priority");
            world.Map.RemoveActor(enemy);
            ItemGrenadePrimed grenade = new ItemGrenadePrimed(world.Game.GameItems.GRENADE_PRIMED) { FuseTimeLeft = 1 };
            world.Map.DropItemAt(grenade, follower.Location.Position);
            Check.Equal(false, follower.Controller.GetAction(world.Game) is ActionFollowerReply, "primed explosive takes priority over reply");
        });
        ScenarioRunner.Add("npc/follower-call-persistence", () => Arena(4947), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor follower = Guard(world, player, "Sam", 5, 1); Call(world);
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "follower-call-format-5.dat");
            BinarySaveStore.Save(path, Session.Get); ScenarioWorld saved = NpcIntentSupport.Restore(world, BinarySaveStore.Load<Session>(path));
            Actor restored = NpcIntentSupport.Find(saved.Map, follower.PersonalityIdentity);
            NpcIntentSupport.Turn(saved, restored);
            Check.Equal(true, Display(saved).Contains("Sam shouts"), "pending reply survives format-5 reload");
            Check.Equal(false, restored.Controller.GetAction(saved.Game) is ActionFollowerReply, "saved reply is consumed once");
            Call(saved); saved.Game.Player.RemoveFollower(restored);
            Check.Equal(false, restored.Controller.GetAction(saved.Game) is ActionFollowerReply, "a departed follower cannot answer a stale call");
        });
        ScenarioRunner.Add("npc/follower-call-without-personalities", () => Arena(4948), world =>
        {
            Session.Get.GamePreset.NpcPersonalitiesEnabled = false;
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor follower = Guard(world, player, "Sam", 5, 1); Call(world);
            NpcIntentSupport.Turn(world, follower);
            Check.Equal(true, Display(world).Contains("Sam shouts"), "ordinary followers can answer with personalities disabled");
        });
        ScenarioRunner.Add("npc/follower-call-other-map", () => Arena(4949), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor follower = Guard(world, player, "Sam", 5, 1);
            Map other = new Map(4949, "another floor", 8, 3);
            for (int x = 0; x < other.Width; x++) for (int y = 0; y < other.Height; y++)
                other.SetTileModelAt(x, y, world.Game.GameTiles[GameTiles.IDs.FLOOR_ASPHALT]);
            world.Map.RemoveActor(follower); other.PlaceActorAt(follower, new Point(5, 1)); Call(world);
            Check.Equal(false, ((BaseAI)follower.Controller).CanReplyFollowerCall(world.Game), "coordinate proximity on another floor is not hearing contact");
        });
    }
}
