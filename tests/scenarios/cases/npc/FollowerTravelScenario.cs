using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Gameplay.AI;

static class FollowerTravelScenario
{
    internal static ScenarioWorld Arena(int seed)
    {
        var w = TownScenarioFactory.Arena(seed, "........................", "........................", "........................", "........................", "........................", "........................", "........................", "........................");
        Session.Get.GamePreset = GamePreset.World(); Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
        RogueGame.KeyBindings.ResetToDefaults(); w.Map.LocalTime.TurnCounter = 360; Session.Get.WorldTime.TurnCounter = 360; return w;
    }
    internal static Actor Follower(ScenarioWorld w, Actor leader, string name, int x, int y)
    {
        Actor actor = NpcIntentSupport.Actor(w, name, x, y); leader.AddFollower(actor);
        actor.TrustInLeader = Rules.TRUST_TRUSTING_THRESHOLD; return actor;
    }
    internal static void Tick(ScenarioWorld w, Actor actor)
    {
        actor.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, w.NpcTurn(actor), "real follower decision performs a legal action");
        Check.Equal(true, actor.ActionPoints < Rules.BASE_ACTION_COST, "follower action is charged");
        CommunityMembershipScenario.Time(w, Session.Get.WorldTime.TurnCounter + 1);
    }
    internal static Map Floor(ScenarioWorld w)
    {
        var floor = new ScenarioWorld(w.Seed + 100, "........................", "........................", "........................", "........................", "........................", "........................", "........................", "........................").Map;
        w.Map.District.AddUniqueMap(floor); floor.LocalTime.TurnCounter = w.Map.LocalTime.TurnCounter; return floor;
    }
    internal static string Display(ScenarioWorld w)
    {
        Check.Call(w.Game, "PresentEncounterSpeech"); var ui = (ScenarioUI)w.Game.UI;
        ui.DrawnStrings.Clear(); Check.Call(w.Game, "DrawMessages"); return String.Join(" ", ui.DrawnStrings);
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/follower-travel-priority", () => Arena(5101), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 8, 3), f = Follower(w, p, "Sam", 3, 3);
            var food = new ItemFood(w.Game.GameItems.CANNED_FOOD); w.Map.DropItemAt(food, new Point(3, 4));
            Tick(w, f);
            Check.Equal(true, f.Location.Position.X > 3, "catching up precedes nearby optional loot");
            Check.Equal(false, f.Inventory.Contains(food), "travel creates no pickup");
            Check.Equal("catching up", ((BaseAI)f.Controller).Travel.Status, "catching-up state");
        });
        ScenarioRunner.Add("npc/follower-travel-contact-memory", () => Arena(5102), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 5, 3), f = Follower(w, p, "Sam", 3, 3);
            Tick(w, f); var ai = (BaseAI)f.Controller; Point remembered = ai.Travel.Contact.Anchor;
            w.Map.PlaceActorAt(p, new Point(20, 3)); for (int y = 0; y < w.Map.Height; y++) w.SetTile(12, y, '#');
            Tick(w, f); Check.Equal(remembered, ai.Travel.Contact.Anchor, "unseen relocation does not refresh hidden coordinates");
            Check.Equal(true, f.Location.Position.X < 12, "search stays around actual last contact");
            for (int n = 0; n < 45; n++) Tick(w, f);
            Check.Equal("waiting near last contact", ai.Travel.Status, "bounded search eventually waits");
            Check.Equal(true, w.Game.Rules.GridDistance(f.Location.Position, remembered) <= 1, "wait returns near the meeting point");
        });
        ScenarioRunner.Add("npc/follower-travel-recall-input", () => Arena(5103), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = Follower(w, p, "Sam", 8, 3);
            w.SetTile(5, 3, '#'); CommunityMembershipScenario.Fov(w);
            Check.Equal(PlayerCommand.RECALL_FOLLOWERS, RogueGame.KeyBindings.Get(Keys.S | Keys.Shift), "Shift+S is a separate rebindable command");
            RogueGame.KeyBindings.Set(PlayerCommand.RECALL_FOLLOWERS, Keys.R | Keys.Control);
            Check.Equal(PlayerCommand.RECALL_FOLLOWERS, RogueGame.KeyBindings.Get(Keys.R | Keys.Control), "recall rebinds normally");
            p.ActionPoints = 100; Check.Equal(true, Check.Call(w.Game, "HandlePlayerRecall", p), "production recall input");
            Check.Equal(0, p.ActionPoints, "recall costs one player action");
            Tick(w, f); Check.Equal(true, Display(w).Contains("Coming back!"), "actual reply acknowledges recall");
            for (int n = 0; n < 15 && w.Game.Rules.GridDistance(f.Location.Position, p.Location.Position) > 2; n++) Tick(w, f);
            Check.Equal(true, w.Game.Rules.GridDistance(f.Location.Position, p.Location.Position) <= 2, "heard recall reunites through legal detour");
        });
        ScenarioRunner.Add("npc/follower-travel-recall-boundaries", () => Arena(5104), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 1, 2), guard = Follower(w, p, "Guard", 3, 2), far = Follower(w, p, "Far", 23, 2);
            Actor distrust = Follower(w, p, "Distrust", 4, 2); distrust.TrustInLeader = Rules.TRUST_NEUTRAL;
            ((BaseAI)guard.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, guard.Location));
            Check.Call(w.Game, "HandlePlayerRecall", p);
            Check.Equal(ActorTasks.GUARD, ((BaseAI)guard.Controller).Order.Task, "recall preserves guard assignment");
            Check.Equal(null, ((BaseAI)far.Controller).Travel, "distant follower gains no recall knowledge");
            Check.Equal(null, ((BaseAI)distrust.Controller).Travel, "distrust prevents recall obedience");
            Map floor = Floor(w); w.Map.RemoveActor(far); floor.PlaceActorAt(far, new Point(1, 2));
            Check.Call(w.Game, "HandlePlayerRecall", p); Check.Equal(null, ((BaseAI)far.Controller).Travel, "same coordinates on another floor are not hearing");
        });
        ScenarioRunner.Add("npc/follower-travel-nested-recall", () => Arena(5105), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = Follower(w, p, "Sam", 7, 3), child = Follower(w, f, "Lee", 8, 4);
            Check.Call(w.Game, "HandlePlayerRecall", p); Tick(w, child);
            Check.Same(f, child.Leader, "recall preserves immediate follower edge");
            Check.Equal(true, Display(w).Contains("Coming back!"), "nested travelling listener answers");
            ((BaseAI)f.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, f.Location));
            Check.Call(w.Game, "HandlePlayerRecall", p); child.Controller.GetAction(w.Game);
            Check.Equal(null, ((BaseAI)child.Controller).Travel, "assigned branch remains anchored rather than recalled");
        });
        ScenarioRunner.Add("npc/follower-travel-needs-priority", () => Arena(5106), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 7, 3), f = Follower(w, p, "Sam", 2, 3);
            f.FoodPoints = 0; var food = NpcIntentSupport.Food(w, f, 1); Point before = f.Location.Position;
            Tick(w, f); Check.Equal(false, f.Inventory.Contains(food), "urgent eating precedes travel"); Check.Equal(before, f.Location.Position, "eating does not move");
            f.StaminaPoints = 0; Tick(w, f); Check.Equal(before, f.Location.Position, "exhaustion precedes catch-up");
            Check.Equal(true, Display(w).Contains("catch my breath"), "rest explains interruption");
        });
        ScenarioRunner.Add("npc/follower-travel-blocked-and-reopened", () => Arena(5107), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 6, 3), f = Follower(w, p, "Sam", 2, 3); f.Controller.GetAction(w.Game);
            for (int y = 0; y < w.Map.Height; y++) w.SetTile(3, y, '#'); Tick(w, f);
            Check.Equal("waiting: route blocked", ((BaseAI)f.Controller).Travel.Status, "blocked route pauses");
            Check.Equal(true, Display(w).Contains("cannot get through"), "quiet obstacle warning");
            w.SetTile(3, 3, '.'); Tick(w, f); Check.Equal(new Point(3, 3), f.Location.Position, "observed adjacent obstacle change retries before ten turns");
            Check.Same(p, f.Leader, "blocked route does not dismiss follower");
        });
        ScenarioRunner.Add("npc/follower-travel-crossing-confirmation", () => Arena(5108), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = Follower(w, p, "Sam", 7, 3); Map floor = Floor(w);
            w.Map.SetExitAt(p.Location.Position, new Exit(floor, new Point(2, 3)) { IsAnAIExit = true }); CommunityMembershipScenario.Fov(w);
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Escape); p.ActionPoints = 100;
            Check.Equal(false, w.Game.DoUseExit(p, p.Location.Position), "cancel departure with unready follower");
            Check.Same(w.Map, p.Location.Map, "cancel leaves player in source map"); Check.Equal(100, p.ActionPoints, "cancel is free");
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Y); Check.Equal(true, w.Game.DoUseExit(p, p.Location.Position), "explicit departure allowed");
            Check.Same(p, f.Leader, "separated follower remains yours"); Check.Same(w.Map, f.Location.Map, "distant follower is not teleported");
            for (int n = 0; n < 12 && f.Location.Map != floor; n++)
            {
                Tick(w, f);
                Console.WriteLine("Exit recovery " + n + ": map=" + f.Location.Map.Seed + ", point=" + f.Location.Position +
                    ", status=" + ((BaseAI)f.Controller).Travel.Status + ", target=" + ((BaseAI)f.Controller).Travel.Contact.MapSeed + ", exitLegal=" + w.Game.Rules.CanActorUseExit(f, new Point(2, 3)));
            }
            Check.Same(floor, f.Location.Map, "heard departure retraced through real exit");
        });
        ScenarioRunner.Add("npc/follower-travel-nested-transfer", () => Arena(5109), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = Follower(w, p, "Sam", 3, 3), child = Follower(w, f, "Lee", 4, 3);
            Map floor = Floor(w); w.Map.SetExitAt(p.Location.Position, new Exit(floor, new Point(2, 3)) { IsAnAIExit = true });
            Check.Equal(true, w.Game.DoUseExit(p, p.Location.Position), "ready nested party crosses without warning");
            Check.Same(floor, f.Location.Map, "direct companion follows"); Check.Same(floor, child.Location.Map, "nested companion follows its immediate leader");
            Check.Equal(3, floor.CountActors, "all actors conserved exactly once"); Check.Equal(3, floor.Actors.Select(a => a.Location.Position).Distinct().Count(), "landing tiles distinct");
            Check.Same(f, child.Leader, "nested relationship preserved");
        });
        ScenarioRunner.Add("npc/follower-travel-assigned-stays", () => Arena(5110), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = Follower(w, p, "Guard", 3, 3); ((BaseAI)f.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, f.Location));
            Map floor = Floor(w); w.Map.SetExitAt(p.Location.Position, new Exit(floor, new Point(2, 3)) { IsAnAIExit = true });
            Check.Equal(true, w.Game.DoUseExit(p, p.Location.Position), "intentionally assigned guard does not prompt");
            Check.Same(w.Map, f.Location.Map, "guard retains its real post"); Check.Same(p, f.Leader, "assignment keeps personal affiliation");
        });
        ScenarioRunner.Add("npc/follower-travel-stale-orders-view", () => Arena(5111), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = Follower(w, p, "Sam", 4, 3); CommunityMembershipScenario.Fov(w);
            FollowerReport report = p.FollowerReports.Single(); int date = report.Turn; string status = report.Status, location = report.Location;
            Map floor = Floor(w); w.Map.RemoveActor(f); floor.PlaceActorAt(f, new Point(2, 3)); f.IsSleeping = true;
            ((BaseAI)f.Controller).SetOrder(new ActorOrder(ActorTasks.PATROL, f.Location)); CommunityMembershipScenario.Time(w, 380);
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Escape); Check.Equal(false, Check.Call(w.Game, "HandlePlayerOrderMode", p), "real O cancels freely");
            var again = p.FollowerReports.Single(); Check.Equal(date, again.Turn, "O does not refresh hidden date"); Check.Equal(status, again.Status, "hidden changed order unknown"); Check.Equal(location, again.Location, "hidden location unchanged");
        });
        ScenarioRunner.Add("npc/follower-travel-saved-bounds", () => Arena(5112), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 3), f = Follower(w, p, "Sam", 4, 3); Tick(w, f); var ai = (BaseAI)f.Controller;
            Map floor = Floor(w);
            for (int n = 0; n < 25; n++) ai.Travel.Transition(new Location(w.Map, new Point(2, 3)), new Location(floor, new Point(2, 3)), n, "heard");
            for (int n = 0; n < 80; n++) p.RecordFollower(new FollowerReport { Identity = Guid.NewGuid(), Name = "old", Source = "heard", Turn = n });
            Check.Equal(16, ai.Travel.Transitions.Count, "exit history bounded"); Check.Equal(64, p.FollowerReports.Count, "player reports bounded");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "follower-travel-format-5.dat"); BinarySaveStore.Save(path, Session.Get);
            var restored = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(path)); var saved = NpcIntentSupport.Find(restored.Map, f.PersonalityIdentity);
            Check.Equal(16, ((BaseAI)saved.Controller).Travel.Transitions.Count, "remembered exits survive save/reload"); Check.Equal(64, restored.Game.Player.FollowerReports.Count, "dated knowledge survives reload");
            restored.Game.Player.RemoveFollower(saved); Check.Equal(null, ((BaseAI)saved.Controller).Travel, "dismissal clears obsolete route and contact");
        });
        ScenarioRunner.Add("npc/follower-travel-legacy", () => Arena(5113), w =>
        {
            string path = Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_LEGACY_FILE"); var loaded = BinarySaveStore.Load<Session>(path);
            var restored = NpcIntentSupport.Restore(w, loaded); Check.Equal(0, restored.Game.Player.FollowerReports.Count, "genuine older save starts without reports");
            foreach (var actor in restored.Map.Actors) if (actor.Controller is BaseAI) Check.Equal(null, ((BaseAI)actor.Controller).Travel, "load creates no travel history");
        });
    }
}
