using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityHomeScenario
{
    static Actor Setup(ScenarioWorld world)
    {
        NpcIntentSupport.Player(world, 22, 7);
        Actor leader = CommunityMembershipScenario.Group(world);
        foreach (Actor member in leader.PartyTree()) NpcIntentSupport.Food(world, member, 6);
        CommunityMembershipScenario.Fov(world); return leader;
    }
    static void Turn(ScenarioWorld world, Actor actor, int turn)
    {
        CommunityMembershipScenario.Time(world, turn); actor.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, world.NpcTurn(actor), "actual home controller action is legal");
        Check.Equal(true, actor.ActionPoints < Rules.BASE_ACTION_COST, "home controller charges action");
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-home-return-watch", () => CommunityMembershipScenario.Arena(4960), world =>
        {
            Actor leader = Setup(world), member = leader.Followers.First();
            world.Map.PlaceActorAt(member, new Point(15, 4));
            XpdBase home = world.Map.XpdBaseAt(leader.Location.Position);
            for (int turn = 0; turn < 50 && !home.Contains(member.Location.Position); turn++) Turn(world, member, turn);
            Check.Equal(true, home.Contains(member.Location.Position), "ordinary outing ends by returning to existing home");
            world.Game.ScheduleCommunityWatch(leader);
            Guid first = leader.SocialGroup.Home.Watcher;
            Check.Equal(false, first == Guid.Empty, "healthy home can assign one watcher");
            Actor watcher = leader.PartyTree().First(a => a.PersonalityIdentity == first);
            // Test the active watch stint; off-duty residents may now leave on an outing.
            leader.SocialGroup.Home.NextRotationTurn = 100;
            for (int turn = 51; turn < 64; turn++) Turn(world, watcher, turn);
            Check.Equal(false, world.Map.GetMapObjectAt(watcher.Location.Position) is djack.RogueSurvivor.Engine.MapObjects.DoorWindow,
                "watcher leaves doorway clear");
            Check.Equal(true, home.Contains(watcher.Location.Position), "watch position remains owned");
            Guid beforeRotation = leader.SocialGroup.Home.Watcher;
            CommunityMembershipScenario.Time(world, 100); world.Game.ScheduleCommunityWatch(leader);
            Check.Equal(false, leader.SocialGroup.Home.Watcher == beforeRotation, "hourly rotation chooses another eligible member");
            foreach (Actor actor in leader.PartyTree()) actor.IsSleeping = true;
            CommunityMembershipScenario.Time(world, 130); world.Game.ScheduleCommunityWatch(leader);
            Check.Equal(Guid.Empty, leader.SocialGroup.Home.Watcher, "no watch when all members sleep");
        });
        ScenarioRunner.Add("npc/community-home-orders-survival-greeting", () => CommunityMembershipScenario.Arena(4961), world =>
        {
            Actor leader = Setup(world), member = leader.Followers.First();
            ActorOrder order = new ActorOrder(ActorTasks.GUARD, new Location(world.Map, new Point(15, 4)));
            ((OrderableAI)member.Controller).SetOrder(order);
            world.Game.ScheduleCommunityWatch(leader);
            Check.Equal(false, leader.SocialGroup.Home.Watcher == member.PersonalityIdentity, "explicitly ordered actor is unavailable for automatic watch");
            Turn(world, member, 0); Check.Same(order, ((OrderableAI)member.Controller).Order, "home routine does not replace orders");
            ((OrderableAI)member.Controller).SetOrder(null);
            member.FoodPoints = 0; int food = NpcIntentSupport.FoodUnits(member); Turn(world, member, 1);
            Check.Equal(true, NpcIntentSupport.FoodUnits(member) < food, "urgent eating precedes home work");
            member.StaminaPoints = 0; world.Game.ScheduleCommunityWatch(member);
            Check.Equal(false, leader.SocialGroup.Home.Watcher == member.PersonalityIdentity, "exhausted member leaves watch");
            member.StaminaPoints = world.Game.Rules.ActorMaxSTA(member);
            world.Map.PlaceActorAt(world.Game.Player, new Point(2, 2)); CommunityMembershipScenario.Fov(world);
            leader.FoodPoints = world.Game.Rules.ActorMaxFood(leader);
            Turn(world, leader, 2);
            Check.Equal(true, world.Game.Player.Personality.HeardJournal.Any(h => h.Text.Contains("Mira's circle")), "home work cannot starve first greeting");
            member.SleepPoints = 0;
            for (int turn = 3; turn < 40 && !member.IsSleeping; turn++) Turn(world, member, turn);
            Check.Equal(true, member.IsSleeping, "normal sleep behavior retains priority over autonomous watch");
        });
        ScenarioRunner.Add("npc/community-home-alarm-waking", () => CommunityMembershipScenario.Arena(4962), world =>
        {
            Actor leader = Setup(world), sleeper = leader.Followers.First();
            world.Game.ScheduleCommunityWatch(leader);
            Actor watcher = leader.PartyTree().First(a => a.PersonalityIdentity == leader.SocialGroup.Home.Watcher);
            if (watcher == sleeper) sleeper = leader.Followers.Last();
            world.Map.PlaceActorAt(watcher, new Point(7, 2));
            world.Map.PlaceActorAt(sleeper, new Point(6, 3));
            world.Map.SetTileModelAt(7, 3, world.Game.GameTiles.WALL_BRICK);
            for (int i = 0; i < 3; i++) sleeper.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
            sleeper.IsSleeping = true;
            Actor threat = NpcIntentSupport.Actor(world, "Thief", 10, 2);
            world.Game.DoMakeAggression(threat, watcher);
            var alarm = new ActionCommunityAlarm(watcher, world.Game, threat);
            Check.Equal(true, watcher.Controller.GetAction(world.Game) is ActionCommunityAlarm, "actual controller selects distant threat alarm");
            watcher.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, world.Try(alarm), "witness raises real noisy alarm");
            Check.Equal(0, watcher.ActionPoints, "alarm charges once");
            Check.Equal(false, sleeper.IsSleeping, "real noise can wake nearby listeners");
            NpcKnownPerson reported = sleeper.Personality.Knowledge.Person(threat.PersonalityIdentity);
            Check.Equal(NpcKnowledgeSource.Told, reported.Source, "hearing alarm teaches only witness report");
            Check.Equal(threat.Location.Position, reported.Place.Position, "reported threat location is the witness's sighting");
            Check.Equal(70, reported.Danger, "existing safety goals receive a reported threat");
            Check.Equal(false, watcher.Controller.GetAction(world.Game) is ActionCommunityAlarm, "alarm cooldown suppresses repeated chatter");
            world.Map.SetTileModelAt(8, 2, world.Game.GameTiles.WALL_BRICK);
            world.Map.SetTileModelAt(8, 1, world.Game.GameTiles.WALL_BRICK);
            world.Map.SetTileModelAt(8, 3, world.Game.GameTiles.WALL_BRICK);
            CommunityMembershipScenario.Time(world, 40);
            Check.Equal(false, new ActionCommunityAlarm(watcher, world.Game, threat).IsLegal(), "cannot raise a new sighting for an unseen enemy");
            Check.Equal(false, world.Game.Rules.CanActorFireAt(sleeper, threat), "report alone never permits shooting unseen enemy");
            world.Map.SetTileModelAt(8, 2, world.Game.GameTiles.FLOOR_ASPHALT);
            watcher.HitPoints = 4;
            Check.Equal(false, watcher.Controller.GetAction(world.Game) is ActionCommunityAlarm, "serious injury retains priority over watch alarm");
        });
        ScenarioRunner.Add("npc/community-home-blocked-loss", () => CommunityMembershipScenario.Arena(4963), world =>
        {
            Actor leader = Setup(world), member = leader.Followers.First();
            world.Map.PlaceActorAt(member, new Point(15, 4));
            for (int y = 0; y < world.Map.Height; y++) world.Map.SetTileModelAt(12, y, world.Game.GameTiles.WALL_BRICK);
            Turn(world, member, 0);
            Check.Equal(30, leader.SocialGroup.Home.RetryTurn(member.PersonalityIdentity), "blocked home route waits thirty turns before retry");
            Check.Equal(true, member.Location.Position.X > 12, "no home teleportation");
            world.Map.SetTileModelAt(12, 4, world.Game.GameTiles.FLOOR_ASPHALT);
            for (int turn = 30; turn < 65 && member.Location.Position.X > 8; turn++) Turn(world, member, turn);
            Check.Equal(true, member.Location.Position.X <= 8, "ordinary navigation retries an opened path");
            XpdBase claim = world.Map.XpdBaseAt(leader.Location.Position); world.Map.RemoveXpdBase(claim);
            Check.Equal(null, world.Game.ResolveCommunityHome(member), "actual claim loss stops home work");
            Check.Equal(Guid.Empty, leader.SocialGroup.Home.Watcher, "claim loss clears automatic watch");
        });
        ScenarioRunner.Add("npc/community-home-witnessed-theft-alarm", () => CommunityMembershipScenario.Arena(4965), world =>
        {
            Actor leader = Setup(world); world.Game.ScheduleCommunityWatch(leader);
            Actor watcher = leader.PartyTree().First(a => a.PersonalityIdentity == leader.SocialGroup.Home.Watcher);
            Actor player = world.Game.Player;
            int index = 0;
            foreach (Actor member in leader.PartyTree()) world.Map.PlaceActorAt(member, new Point(4 + index++, 5));
            watcher.Personality.AddTrait(new TraitInstance("brave"));
            CommunityCultureSystem.Ensure(world.Game,leader);leader.SocialGroup.Culture.Enforcement=80;
            // Teach the defender the true policy privately; public rules may be a cover.
            CommunityCultureSystem.SayPolicy(world.Game,leader,watcher,true);
            Item bat = new ItemMeleeWeapon(world.Game.GameItems.BASEBALLBAT); watcher.Inventory.AddAll(bat); world.Game.DoEquipItem(watcher, bat);
            world.Map.PlaceActorAt(watcher, new Point(5, 2)); world.Map.PlaceActorAt(player, new Point(9, 2));
            index = 0;
            foreach (Actor member in leader.PartyTree())
                if (member != watcher) { world.Map.PlaceActorAt(member, new Point(6, 3 + index++)); member.IsSleeping = true; }
            CommunityMembershipScenario.Fov(world);
            ItemFood stock = new ItemFood(world.Game.GameItems.CANNED_FOOD); world.Map.DropItemAt(stock, new Point(8, 2));
            player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Try(new ActionTakeItem(player, world.Game, new Point(8, 2), stock)), "outsider takes actual claimed stock beside home");
            Check.Equal(true, world.Game.Rules.AreEnemies(watcher, player), "awake watcher identifies witnessed theft");
            bool alarmed = false; int hp = player.HitPoints;
            for (int turn = 0; turn < 20 && player.HitPoints == hp; turn++)
            {
                CommunityMembershipScenario.Time(world, turn); watcher.ActionPoints = Rules.BASE_ACTION_COST;
                ActorAction action = watcher.Controller.GetAction(world.Game);
                if (action is ActionCommunityAlarm) alarmed = true;
                Check.Equal(true, world.Try(action), "watcher performs real alarm or ordinary defensive action");
            }
            Check.Equal(true, alarmed, "witnessed theft can trigger a noisy watcher alarm");
            Check.Equal(true, player.HitPoints < hp, "ordinary controller actually defends against the witnessed thief");
        });
        ScenarioRunner.Add("npc/community-home-turn-budget", () => CommunityMembershipScenario.Arena(4964), world =>
        {
            Actor leader = Setup(world);
            for (int i = 0; i < 20; i++) leader.Controller.GetAction(world.Game);
            var clock = Stopwatch.StartNew();
            for (int i = 0; i < 1000; i++) { world.Map.LocalTime.TurnCounter = i; leader.Controller.GetAction(world.Game); }
            clock.Stop();
            Console.WriteLine("Active home: " + clock.Elapsed.TotalMilliseconds.ToString("F2") + " ms / 1,000 controller selections");
            Check.Equal(true, world.Game.ResolveCommunityHome(leader) != null, "active-home benchmark retains a valid cached home");
        });
    }
}
