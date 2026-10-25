using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;

static class CommunityActivityScenario
{
    static ActorAction Step(ScenarioWorld w, Actor actor)
    {
        actor.ActionPoints = Rules.BASE_ACTION_COST;
        ActorAction action = actor.Controller.GetAction(w.Game);
        Check.Equal(true, w.Try(action), "ordinary NPC action remains legal");
        return action;
    }
    static ActorAction Home(ScenarioWorld w, Actor actor, bool sleepReturn = false)
    {
        return (ActorAction)typeof(OrderableAI).GetMethod("BehaviorCommunityHome", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(actor.Controller, new object[] { w.Game, sleepReturn });
    }
    static Actor Residents(ScenarioWorld w, out Actor chief)
    {
        Actor resident; var group = CommunityAdaptationScenario.Found(w, out chief, out resident);
        chief.Personality.IntentList.Clear();
        Check.Equal(true, w.Game.AvailableForCommunityWatch(chief), "fixture chief is available for its watch");
        group.Home.Watcher = chief.PersonalityIdentity; group.Home.NextRotationTurn = 2000;
        return resident;
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-activity-pickup-before-familiarity", () => CommunityAdaptationScenario.Arena(6410), w => {
            var actor = NpcIntentSupport.Actor(w, "Observer", 4, 4);
            var neighbor = NpcIntentSupport.Actor(w, "Neighbor", 5, 4);
            var bat = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT); w.Map.DropItemAt(bat, actor.Location.Position);
            Step(w, actor);
            Check.Equal(true, actor.Inventory.Contains(bat), "unaffiliated survivor gathers equipment before any social movement");
            Check.Equal(null, actor.Community, "pickup grants no automatic membership");
            Check.Equal(true, actor.ResidentCommunity.Contact(neighbor.PersonalityIdentity) != null, "familiarity still grows while gathering");
        });
        ScenarioRunner.Add("npc/community-activity-mobile-founding", () => CommunityAdaptationScenario.Arena(6411), w => {
            var a = NpcIntentSupport.Actor(w, "Mira", 4, 4); var b = NpcIntentSupport.Actor(w, "Sam", 5, 4);
            a.Sheet.SkillTable.AddOrIncreaseSkill((int)djack.RogueSurvivor.Gameplay.Skills.IDs.LEADERSHIP);
            int movement = 0, waiting = 0;
            for (int n = 0; n < 180 && a.Community == null; n++) {
                CommunityAdaptationScenario.Time(w, 360 + n);
                foreach (var actor in new[] { a, b }) {
                    Point before = actor.Location.Position; var action = Step(w, actor);
                    if (actor.Location.Position != before) movement++;
                    if (action is ActionWait) waiting++;
                }
            }
            Check.Equal(true, movement >= 40, "founders keep moving during acquaintance");
            Check.Equal(true, waiting < 10, "familiarity does not impose repeated idle turns");
            Check.Equal(true, a.Community != null, "moving strangers still propose, consent and found");
            Check.Same(a.Community, b.Community, "founders retain independent shared membership");
            Check.Equal(null, a.Leader, "founding creates no command hierarchy");
            Console.WriteLine("MOBILE FOUNDING: moves=" + movement + "; waits=" + waiting + "; turn=" + w.Map.LocalTime.TurnCounter);
        });
        ScenarioRunner.Add("npc/community-activity-unclaimable-refuge", () => CommunityAdaptationScenario.Arena(6412), w => {
            var a = NpcIntentSupport.Actor(w, "Mira", 4, 4); var b = NpcIntentSupport.Actor(w, "Sam", 5, 4);
            w.SetTile(1, 5, '.');
            int movement = 0, consecutive = 0, maximum = 0;
            for (int n = 0; n < 90; n++) {
                CommunityAdaptationScenario.Time(w, 360 + n);
                Point before = a.Location.Position; var action = Step(w, a);
                if (a.Location.Position != before) movement++;
                consecutive = action is ActionWait ? consecutive + 1 : 0; maximum = Math.Max(maximum, consecutive);
                Step(w, b);
            }
            Check.Equal(true, movement >= 30, "an unusable founding site does not suspend ordinary navigation");
            Check.Equal(true, maximum < 5, "failed founding cannot produce the old 180-turn hold");
            Console.WriteLine("INVALID REFUGE: moves=" + movement + "; longestWait=" + maximum);
        });
        ScenarioRunner.Add("npc/community-activity-resident-exploration", () => CommunityAdaptationScenario.Arena(6413), w => {
            Actor chief; var resident = Residents(w, out chief); var places = new HashSet<Point>(); int waits = 0;
            for (int n = 0; n < 90; n++) {
                CommunityAdaptationScenario.Time(w, 361 + n); places.Add(resident.Location.Position);
                bool tired = w.Game.Rules.IsActorTired(resident);
                if (Step(w, resident) is ActionWait) {
                    waits++;
                    Check.Equal(true, tired || resident.SocialGroup.Home.Watcher == resident.PersonalityIdentity,
                        "a resting resident must have a survival need or actual watch duty");
                }
            }
            Console.WriteLine("RESIDENT ACTIVITY: distinctPositions=" + places.Count + "; waits=" + waits + "; watcher=" + (resident.SocialGroup.Home.Watcher == resident.PersonalityIdentity));
            Check.Equal(true, places.Count >= 10, "idle resident explores instead of remaining fixed at home");

            Console.WriteLine("RESIDENT: distinctPositions=" + places.Count + "; waits=" + waits);
        });
        ScenarioRunner.Add("npc/community-activity-outing-return-and-save", () => CommunityAdaptationScenario.Arena(6414), w => {
            Actor chief; var resident = Residents(w, out chief);
            Check.Equal(null, Home(w, resident), "non-watcher at home yields to normal activity");
            w.Map.PlaceActorAt(resident, new Point(14, 6));
            CommunityAdaptationScenario.Time(w, 380);
            Check.Equal(null, Home(w, resident), "nearby outing remains free before its deadline");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "activity-outing.dat");
            Guid id = resident.PersonalityIdentity; BinarySaveStore.Save(path, Session.Get);
            w = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(path)); resident = NpcIntentSupport.Find(w.Map, id);
            CommunityAdaptationScenario.Time(w, 421);
            Check.Equal(true, Home(w, resident) != null, "reloading retains the expired outing deadline");
            var claim = w.Game.ResolveCommunityHome(resident).Claim;
            for (int turn = 421; turn < 460 && !claim.Contains(resident.Location.Position); turn++) {
                CommunityAdaptationScenario.Time(w, turn); resident.ActionPoints = Rules.BASE_ACTION_COST;
                var action = Home(w, resident); Check.Equal(true, action != null && w.Try(action), "ordinary navigation completes return");
            }
            Check.Equal(true, claim.Contains(resident.Location.Position), "return continues through the door all the way home");
            Check.Equal(null, Home(w, resident), "arrival permits a new outing");
        });
        ScenarioRunner.Add("npc/community-activity-idle-exit-boundary", () => CommunityAdaptationScenario.Arena(6416), w => {
            Actor chief; var resident = Residents(w, out chief);
            Home(w, resident);
            var other = new Map(16416, "other floor", 4, 4);
            for (int x = 0; x < 4; x++) for (int y = 0; y < 4; y++) other.SetTileModelAt(x, y, w.Game.GameTiles.FLOOR_ASPHALT);
            w.Map.District.AddUniqueMap(other);
            var landing = new Point(12, 6); w.Map.SetExitAt(landing, new Exit(other, new Point(1, 1)) { IsAnAIExit = true });
            for (int i = 0; i < 60; i++) {
                if (resident.Location.Position != landing) w.Map.PlaceActorAt(resident, landing);
                var action = Step(w, resident);
                Check.Equal(false, action is ActionUseExit, "idle exploration never abandons its home map");
            }
            Check.Same(w.Map, resident.Location.Map, "idle decision keeps current map");
            // Actual cross-map return remains possible after task/survival travel.
            other.SetExitAt(new Point(1, 1), new Exit(w.Map, landing) { IsAnAIExit = true });
            if (resident.Location.Position != landing) w.Map.PlaceActorAt(resident, landing);
            other.LocalTime.TurnCounter = w.Map.LocalTime.TurnCounter;
            resident.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, w.Try(new ActionUseExit(resident, landing, w.Game)), "real task travel can still cross an exit");
            for (int i = 0; i < 8 && resident.Location.Map != w.Map; i++) Step(w, resident);
            Check.Same(w.Map, resident.Location.Map, "ordinary known-exit navigation returns to the home map");
        });
        ScenarioRunner.Add("npc/community-activity-distance-and-watch", () => CommunityAdaptationScenario.Arena(6415), w => {
            Actor chief; var resident = Residents(w, out chief);
            Home(w, resident); w.Map.PlaceActorAt(resident, new Point(17, 6));
            var far = Home(w, resident); Console.WriteLine("DISTANCE: first=" + (far == null ? "null" : far.GetType().Name) + "; watcher=" + (resident.SocialGroup.Home.Watcher == resident.PersonalityIdentity) + "; retry=" + resident.SocialGroup.Home.RetryTurn(resident.PersonalityIdentity));
            Check.Equal(true, far != null, "radius starts return before the time deadline");
            resident.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(far), "first return step is real");
            for (int i = 0; i < 12 && resident.Location.Position.X > 13; i++) {
                resident.ActionPoints = Rules.BASE_ACTION_COST;
                var returning = Home(w, resident); Check.Equal(true, returning != null && w.Try(returning), "walk back inside radius");
            }
            Check.Equal(true, Home(w, resident) != null, "reentering radius does not cancel an unfinished return");
            w.Map.PlaceActorAt(chief, new Point(9, 6));
            ActorAction watch = null;
            for (int i = 0; i < 20; i++) {
                chief.ActionPoints = Rules.BASE_ACTION_COST; watch = Home(w, chief);
                Check.Equal(true, watch != null, "healthy watcher keeps an actual watch action");
                if (watch is ActionWait) break;
                Check.Equal(true, w.Try(watch), "watcher walks to its actual entrance post");
            }
            Check.Equal(true, watch is ActionWait, "watcher still holds the entrance post");
            var order = new ActorOrder(ActorTasks.GUARD, resident.Location); ((OrderableAI)resident.Controller).SetOrder(order);
            Check.Equal(null, Home(w, resident), "explicit work suppresses autonomous home activity");
            Check.Same(order, ((OrderableAI)resident.Controller).Order, "home behavior never replaces the order");
            Check.Equal(null, Home(w, chief, true), "sleep return does not trap an actor who is already home");
        });
    }
}
