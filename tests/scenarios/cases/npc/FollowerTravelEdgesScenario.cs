using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;

static class FollowerTravelEdgesScenario
{
    static object Field(RogueGame game, string name) { return typeof(RogueGame).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(game); }
    public static void Register()
    {
        ScenarioRunner.Add("npc/follower-travel-real-key", () => FollowerTravelScenario.Arena(5120), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3);
            for (int n = 0; n < 3; n++) f.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
            f.IsSleeping = true; p.ActionPoints = 100;
            ((ScenarioUI)w.Game.UI).QueueKey(Keys.S | Keys.Shift);
            Check.Call(w.Game, "HandlePlayerActor", p);
            Check.Equal(0, p.ActionPoints, "real Shift+S dispatch charges once");
            Check.Equal(false, f.IsSleeping, "recall uses actual shout waking");
            Check.Equal(true, ((BaseAI)f.Controller).Travel.Recalling, "awakened listener understands recall");
            FollowerTravelScenario.Tick(w, f);
            Check.Equal(true, FollowerTravelScenario.Display(w).Contains("Coming back!"), "awakened travelling follower acknowledges");
            Check.Equal(PlayerCommand.SAVE_GAME, RogueGame.KeyBindings.Get(Keys.S | Keys.Shift | Keys.Control), "save has a distinct default binding");
            Check.Equal(PlayerCommand.SWITCH_PLACE, RogueGame.KeyBindings.Get(Keys.S | Keys.Control), "existing swap is preserved");
        });
        ScenarioRunner.Add("npc/follower-travel-old-bindings", () => FollowerTravelScenario.Arena(5121), w =>
        {
            var kb = new Keybindings(); kb.ResetToDefaults(); kb.Set(PlayerCommand.RECALL_FOLLOWERS, Keys.None); kb.Set(PlayerCommand.SAVE_GAME, Keys.S | Keys.Shift);
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "follower-old-keys.dat"); Keybindings.Save(kb, path);
            var loaded = Keybindings.Load(path);
            Check.Equal(PlayerCommand.RECALL_FOLLOWERS, loaded.Get(Keys.S | Keys.Shift), "older default gains recall");
            Check.Equal(PlayerCommand.SAVE_GAME, loaded.Get(Keys.S | Keys.Control | Keys.Shift), "older save default migrates");
            kb.Set(PlayerCommand.SAVE_GAME, Keys.F9); kb.Set(PlayerCommand.SHOUT, Keys.S | Keys.Shift); Keybindings.Save(kb, path); loaded = Keybindings.Load(path);
            Check.Equal(PlayerCommand.SHOUT, loaded.Get(Keys.S | Keys.Shift), "custom existing binding stays intact");
            Check.Equal(Keys.F9, loaded.Get(PlayerCommand.SAVE_GAME), "custom save binding stays intact");
        });
        ScenarioRunner.Add("npc/follower-travel-door-and-yield", () => FollowerTravelScenario.Arena(5122), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 7, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 2, 3);
            for (int y = 0; y < w.Map.Height; y++) if (y != 3) w.SetTile(4, y, '#');
            var door = new DoorWindow("door", "closed", "open", "broken", 40); w.Map.PlaceMapObjectAt(door, new Point(4, 3));
            ((BaseAI)f.Controller).ContactFollower(w.Game, p, "heard leader");
            for (int n = 0; n < 7 && w.Game.Rules.GridDistance(f.Location.Position, p.Location.Position) > 2; n++) FollowerTravelScenario.Tick(w, f);
            Check.Equal(true, door.IsOpen, "following opens a legal closed door"); Check.Equal(true, door.HitPoints > 0, "following does not bash the door");
            w.Map.PlaceActorAt(f, new Point(4, 3)); w.Map.PlaceActorAt(p, new Point(5, 3)); FollowerTravelScenario.Tick(w, f);
            Check.Equal(false, w.Map.GetMapObjectAt(f.Location.Position) is DoorWindow, "nearby follower yields out of doorway");
            Check.Equal(true, w.Game.Rules.GridDistance(f.Location.Position, p.Location.Position) <= 2, "yield remains beside leader");
        });
        ScenarioRunner.Add("npc/follower-travel-stationary-stops", () => FollowerTravelScenario.Arena(5123), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 5, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3);
            ((BaseAI)f.Controller).ContactFollower(w.Game, p, "seen");
            Check.Equal(true, f.Controller.GetAction(w.Game) is ActionWait, "optional routine waits during first stationary turn");
            var take = new ActionTakeItem(f, w.Game, f.Location.Position, new ItemFood(w.Game.GameItems.CANNED_FOOD));
            CommunityMembershipScenario.Time(w, 364); f.Controller.GetAction(w.Game);
            Check.Same(take, ((BaseAI)f.Controller).ConstrainFollowerRoutine(w.Game, take), "close stationary routine can collect unclaimed supplies");
            w.Map.AddXpdBase(new XpdBase(NpcIntentSupport.Actor(w, "Owner", 9, 3), new[] { f.Location.Position }));
            Check.Equal(true, ((BaseAI)f.Controller).ConstrainFollowerRoutine(w.Game, take) is ActionWait, "following does not authorize foreign-stock theft");
            var move = new ActionMoveStep(f, w.Game, new Point(2, 3));
            Check.Equal(true, ((BaseAI)f.Controller).ConstrainFollowerRoutine(w.Game, move) is ActionWait, "routine cannot wander beyond two tiles");
        });
        ScenarioRunner.Add("npc/follower-travel-warning-pause", () => FollowerTravelScenario.Arena(5124), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 6, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 2, 3), sleeper = NpcIntentSupport.Actor(w, "Sleeper", 2, 4);
            sleeper.IsSleeping = true; ((BaseAI)f.Controller).ContactFollower(w.Game, p, "heard");
            for (int y = 0; y < w.Map.Height; y++) w.SetTile(3, y, '#');
            Check.Call(w.Game, "StartMouseMove", p, new List<Point> { new Point(7, 3) }); FollowerTravelScenario.Tick(w, f);
            Check.Equal(null, Field(w.Game, "m_MouseMoveSteps"), "actually heard blocked remark pauses automatic travel");
            Check.Equal(true, sleeper.IsSleeping, "quiet remark invokes no loud-noise waking");
            var ai = (BaseAI)f.Controller; int warned = ai.Travel.WarningTurn;
            for (int n = 0; n < 15; n++) FollowerTravelScenario.Tick(w, f);
            Check.Equal(warned, ai.Travel.WarningTurn, "one interruption does not repeat every retry");
            p.AudioRangeMod = -p.Sheet.BaseAudioRange; ai.Travel.Warning = null; CommunityMembershipScenario.Time(w, 400);
            Check.Call(w.Game, "StartMouseMove", p, new List<Point> { new Point(7, 3) }); FollowerTravelScenario.Tick(w, f);
            Check.Equal(true, Field(w.Game, "m_MouseMoveSteps") != null, "unheard remark does not interrupt automatic travel");
        });
        ScenarioRunner.Add("npc/follower-travel-sleep-and-danger", () => FollowerTravelScenario.Arena(5125), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 8, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 2, 3); f.SleepPoints = 0;
            for (int x = 0; x < w.Map.Width; x++) for (int y = 0; y < w.Map.Height; y++) w.Map.GetTileAt(x, y).IsInside = true;
            for (int n = 0; n < 16 && !f.IsSleeping; n++) FollowerTravelScenario.Tick(w, f);
            Check.Equal(true, f.IsSleeping, "necessary sleep retains the existing safe-turn check before sleeping");
            Check.Equal(true, FollowerTravelScenario.Display(w).Contains("need sleep"), "sleep gets a quiet explanation");
            f.IsSleeping = false; f.SleepPoints = w.Game.Rules.ActorMaxSleep(f);
            var enemy = w.Game.GameActors.Zombie.CreateAnonymous(w.Game.GameFactions.TheUndeads, 0); w.Map.PlaceActorAt(enemy, new Point(3, 3));
            var action = f.Controller.GetAction(w.Game); Check.Equal(false, action is ActionWait, "immediate combat/danger can interrupt travel");
            w.Map.RemoveActor(enemy); var grenade = new ItemGrenadePrimed(w.Game.GameItems.GRENADE_PRIMED) { FuseTimeLeft = 1 }; w.Map.DropItemAt(grenade, f.Location.Position);
            Point before = f.Location.Position; FollowerTravelScenario.Tick(w, f); Check.Equal(false, before == f.Location.Position, "explosive escape precedes travel");
        });
        ScenarioRunner.Add("npc/follower-travel-unheard-exit", () => FollowerTravelScenario.Arena(5126), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 23, 3); Map floor = FollowerTravelScenario.Floor(w);
            w.Map.SetExitAt(p.Location.Position, new Exit(floor, new Point(2, 3)) { IsAnAIExit = true });
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Y); Check.Equal(true, w.Game.DoUseExit(p, p.Location.Position), "deliberate unheard departure");
            f.Controller.GetAction(w.Game); Check.Equal(null, ((BaseAI)f.Controller).Travel, "unseen/unheard transition creates no route"); Check.Same(p, f.Leader, "unheard separation retains relationship");
        });
        ScenarioRunner.Add("npc/follower-travel-assignments", () => FollowerTravelScenario.Arena(5127), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3);
            foreach (var task in new[] { ActorTasks.GUARD, ActorTasks.PATROL, ActorTasks.SLEEP_NOW, ActorTasks.BUILD_SMALL_FORTIFICATION, ActorTasks.BARRICADE_ONE, ActorTasks.SCAVENGE_SUPPLIES })
            {
                ((BaseAI)f.Controller).SetOrder(new ActorOrder(task, f.Location)); Check.Call(w.Game, "HandlePlayerRecall", p);
                Check.Equal(task, ((BaseAI)f.Controller).Order.Task, "recall keeps assignment " + task); Check.Equal(null, ((BaseAI)f.Controller).Travel, "assigned branch gains no travel recall");
            }
        });
        ScenarioRunner.Add("npc/follower-travel-unseen-death", () => FollowerTravelScenario.Arena(5128), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3); CommunityMembershipScenario.Fov(w); var report = p.FollowerReports.Single();
            Map floor = FollowerTravelScenario.Floor(w); w.Map.RemoveActor(f); floor.PlaceActorAt(f, new Point(2, 3)); f.IsDead = true; p.RemoveFollower(f);
            CommunityMembershipScenario.Time(w, 400); ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Escape); Check.Call(w.Game, "HandlePlayerOrderMode", p);
            Check.Same(report, p.FollowerReports.Single(), "unwitnessed death cannot silently alter status record");
        });
        ScenarioRunner.Add("npc/follower-travel-phone-battery", () => FollowerTravelScenario.Arena(5129), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3); Map floor = FollowerTravelScenario.Floor(w);
            var a = new ItemTracker(w.Game.GameItems.CELL_PHONE); var b = new ItemTracker(w.Game.GameItems.CELL_PHONE); p.Inventory.AddAll(a); f.Inventory.AddAll(b);
            Check.Equal(true, w.Try(new ActionEquipItem(p, w.Game, a)), "player equips real phone"); Check.Equal(true, w.Try(new ActionEquipItem(f, w.Game, b)), "follower equips real phone");
            w.Map.RemoveActor(p); floor.PlaceActorAt(p, new Point(2, 3)); f.Controller.GetAction(w.Game);
            var ai = (BaseAI)f.Controller; Check.Equal(floor.Seed, ai.Travel.Contact.MapSeed, "working phone learns correct map identity"); Check.Equal(0, ai.Travel.Transitions.Count, "phone position cannot invent unknown transitions");
            a.Batteries = 0; Point contact = ai.Travel.Contact.Anchor; floor.PlaceActorAt(p, new Point(9, 3)); f.Controller.GetAction(w.Game);
            Check.Equal(contact, ai.Travel.Contact.Anchor, "empty battery does not learn hidden new position");
        });
        ScenarioRunner.Add("npc/follower-travel-running", () => FollowerTravelScenario.Arena(5130), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 7, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 2, 3); p.IsRunning = true; int before = f.StaminaPoints;
            FollowerTravelScenario.Tick(w, f); Check.Equal(true, f.IsRunning, "seen running leader permits ordinary running catch-up"); Check.Equal(true, f.StaminaPoints < before, "running pays real stamina");
            f.StaminaPoints = 0; Point point = f.Location.Position; FollowerTravelScenario.Tick(w, f); Check.Equal(point, f.Location.Position, "exhausted runner rests instead of free running");
        });
        ScenarioRunner.Add("npc/follower-travel-district-delayed", () => FollowerTravelScenario.Arena(5131), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 23, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 20, 3); Map target = FollowerTravelScenario.Floor(w);
            var district = new District(new Point(1, 0), DistrictKind.RESIDENTIAL); district.EntryMap = target;
            var options = RogueGame.Options; options.SimulateDistricts = GameOptions.SimRatio.OFF; options.SimThread = false;
            typeof(RogueGame).GetField("s_Options", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, options);
            Point exit = new Point(24, 3); w.Map.SetExitAt(exit, new Exit(target, new Point(0, 3)));
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.N); p.ActionPoints = 100; Check.Equal(false, w.Game.DoPlayerBump(p, Direction.E), "district warning can cancel"); Check.Equal(100, p.ActionPoints, "district cancellation is free");
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Y); Check.Equal(true, w.Game.DoPlayerBump(p, Direction.E), "deliberate district departure");
            Check.Same(p, f.Leader, "district separation cannot dismiss"); Point old = f.Location.Position;
            CommunityMembershipScenario.Time(w, 400); Check.Equal(old, f.Location.Position, "inactive simulation cannot imply catch-up");
            for (int n = 0; n < 15 && f.Location.Map != target; n++) FollowerTravelScenario.Tick(w, f);
            Check.Same(target, f.Location.Map, "when local turns run, remembered outside-map exit is usable"); Check.Same(p, f.Leader, "reunion preserves leadership");
            Check.Equal(0, w.Map.CountActors, "old map no duplicated actors"); Check.Equal(2, target.CountActors, "district crossing conserves actors exactly once");
        });
        ScenarioRunner.Add("npc/follower-travel-safe-entry", () => FollowerTravelScenario.Arena(5132), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 7, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 2, 3);
            for (int y = 0; y < w.Map.Height; y++) if (y != 3) w.SetTile(4, y, '#');
            var door = new DoorWindow("door", "closed", "open", "broken", 40); door.BarricadePoints = 20; w.Map.PlaceMapObjectAt(door, new Point(4, 3));
            ((BaseAI)f.Controller).ContactFollower(w.Game, p, "heard leader");
            for (int n = 0; n < 12; n++) FollowerTravelScenario.Tick(w, f);
            Check.Equal(20, door.BarricadePoints, "following cannot tear down barricades"); Check.Equal(40, door.HitPoints, "following cannot destroy entry");
            Check.Same(p, f.Leader, "blocked shelter preserves relationship");
        });
        ScenarioRunner.Add("npc/follower-travel-branch-invalidation", () => FollowerTravelScenario.Arena(5133), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 5, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3), child = FollowerTravelScenario.Follower(w, f, "Lee", 3, 3);
            f.Controller.GetAction(w.Game); child.Controller.GetAction(w.Game); Check.Equal(true, ((BaseAI)child.Controller).Travel != null, "nested companion has learned route/contact");
            p.RemoveFollower(f); Check.Equal(null, ((BaseAI)child.Controller).Travel, "branch dismissal invalidates old personal routes"); Check.Same(f, child.Leader, "dismissal preserves nested immediate edge");
            p.AddFollower(f); Check.Equal(null, ((BaseAI)child.Controller).Travel, "recruitment starts without stale route knowledge");
        });
        ScenarioRunner.Add("npc/follower-travel-heard-status-boundary", () => FollowerTravelScenario.Arena(5134), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 8, 3);
            ((BaseAI)f.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, f.Location)); w.SetTile(5, 3, '#'); CommunityMembershipScenario.Fov(w);
            Check.Call(w.Game, "HandlePlayerShout", new[] { typeof(Actor), typeof(string) }, p, null); FollowerTravelScenario.Tick(w, f);
            Check.Equal("answered location check", p.FollowerReports.Single().Status, "unseen reply learns spoken acknowledgment without hidden assignment");
            Check.Equal(true, p.FollowerReports.Single().Location.Contains("east"), "unseen reply reports auditory direction rather than hidden exact coordinates");
        });
        ScenarioRunner.Add("npc/follower-travel-long-detour", () => TownScenarioFactory.Arena(5135, Enumerable.Repeat(new string('.', 50), 30).ToArray()), w =>
        {
            Session.Get.GamePreset = GamePreset.World(); Session.Get.UniqueActors.TheSewersThing = new UniqueActor(); w.Map.LocalTime.TurnCounter = 360; Session.Get.WorldTime.TurnCounter = 360;
            Actor p = NpcIntentSupport.Player(w, 20, 15), f = FollowerTravelScenario.Follower(w, p, "Sam", 24, 15);
            for (int y = 1; y < 29; y++) w.SetTile(22, y, '#');
            w.Game.DoSay(p, null, "I'm here.", RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF);
            for (int n = 0; n < 55 && w.Game.Rules.GridDistance(p.Location.Position, f.Location.Position) > 2; n++) FollowerTravelScenario.Tick(w, f);
            Check.Equal(true, w.Game.Rules.GridDistance(p.Location.Position, f.Location.Position) <= 2, "existing pathfinding takes legal detour beyond the first local window");
        });
        ScenarioRunner.Add("npc/follower-travel-linked-status-view", () => FollowerTravelScenario.Arena(5136), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3); FollowerTravelScenario.Tick(w, f); CommunityMembershipScenario.Fov(w);
            var ui = (ScenarioUI)w.Game.UI; ui.DrawnStrings.Clear(); ui.QueueWaitKey(Keys.Escape); Check.Call(w.Game, "HandlePlayerOrderMode", p);
            string drawn = String.Join(" ", ui.DrawnStrings);
            Check.Equal(true, drawn.Contains("Sam: following"), "single connected follower shows status in real O");
            Check.Equal(true, drawn.Contains("Last contact: scenario"), "real O keeps location visible");
            Check.Equal(true, drawn.Contains("Turn 361; seen by you"), "real O keeps date and source visible within seven lines");
        });
        ScenarioRunner.Add("npc/follower-travel-known-chain", () => FollowerTravelScenario.Arena(5137), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3); Map middle = FollowerTravelScenario.Floor(w);
            Map last = new Map(w.Seed + 200, "last floor", 24, 8); for (int x = 0; x < 24; x++) for (int y = 0; y < 8; y++) last.SetTileModelAt(x, y, w.Game.GameTiles.FLOOR_ASPHALT); w.Map.District.AddUniqueMap(last);
            w.Map.SetExitAt(new Point(2, 3), new Exit(middle, new Point(2, 3))); middle.SetExitAt(new Point(3, 3), new Exit(last, new Point(2, 3)));
            var a = new ItemTracker(w.Game.GameItems.CELL_PHONE); var b = new ItemTracker(w.Game.GameItems.CELL_PHONE); p.Inventory.AddAll(a); f.Inventory.AddAll(b); w.Try(new ActionEquipItem(p, w.Game, a)); w.Try(new ActionEquipItem(f, w.Game, b));
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Y); w.Game.DoUseExit(p, p.Location.Position);
            w.Try(new ActionMoveStep(p, w.Game, new Point(3, 3)));
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Y); w.Game.DoUseExit(p, new Point(3, 3));
            Check.Equal(2, ((BaseAI)f.Controller).Travel.Transitions.Count, "actual phone-linked departures teach two real transitions");
            for (int n = 0; n < 15 && f.Location.Map != last; n++) FollowerTravelScenario.Tick(w, f);
            Check.Same(last, f.Location.Map, "normal NPC actions retrace the complete remembered multi-map chain");
        });
        ScenarioRunner.Add("npc/follower-travel-partial-chain", () => FollowerTravelScenario.Arena(5138), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = FollowerTravelScenario.Follower(w, p, "Sam", 4, 3); Map middle = FollowerTravelScenario.Floor(w);
            Map unknown = new Map(w.Seed + 200, "unreported floor", 24, 8); for (int x = 0; x < 24; x++) for (int y = 0; y < 8; y++) unknown.SetTileModelAt(x, y, w.Game.GameTiles.FLOOR_ASPHALT); w.Map.District.AddUniqueMap(unknown);
            var ai = (BaseAI)f.Controller; ai.ContactFollower(w.Game, p, "seen");
            ai.Travel.Transition(new Location(w.Map, p.Location.Position), new Location(middle, new Point(2, 3)), 359, "old heard departure");
            ai.Travel.Transition(new Location(middle, new Point(2, 3)), new Location(w.Map, p.Location.Position), 360, "old heard return");
            w.Map.RemoveActor(p); unknown.PlaceActorAt(p, new Point(2, 3));
            var a = new ItemTracker(w.Game.GameItems.CELL_PHONE); var b = new ItemTracker(w.Game.GameItems.CELL_PHONE); p.Inventory.AddAll(a); f.Inventory.AddAll(b); w.Try(new ActionEquipItem(p, w.Game, a)); w.Try(new ActionEquipItem(f, w.Game, b));
            var action = f.Controller.GetAction(w.Game); Check.Equal(true, action is ActionWait, "partial knowledge waits instead of following an old cycle"); Check.Equal("waiting for contact", ai.Travel.Status, "unknown connecting exit remains unknown");
        });
    }
}
