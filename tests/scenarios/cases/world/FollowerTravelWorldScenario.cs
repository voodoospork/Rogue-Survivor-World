using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

// Automated player driving over generated geometry. NPCs and survival use the production scheduler;
// no supplies, actor relocation, forced needs, stat protection or human visual review.
static class FollowerTravelWorldScenario
{
    static int rounds, doors;
    static Point? lastSeen;
    static readonly List<double> times = new List<double>();
    static ScenarioWorld Current(ScenarioWorld w) { return new ScenarioWorld(w.Seed, w.Game.Player.Location.Map, w.Game); }
    static void Round(ScenarioWorld w)
    {
        w = Current(w); var clock = Stopwatch.StartNew(); CommunityClarityScenario.AmbientTurn(w); clock.Stop(); times.Add(clock.Elapsed.TotalMilliseconds); rounds++;
        foreach (Actor member in w.Game.PersonalFollowers()) if (w.Game.FollowerVisible(member)) lastSeen = member.Location.Position;
        Check.Equal(false, w.Game.Player.IsDead, "journey survives without protection");
        if (rounds % 60 == 0) Console.WriteLine("Follower journey round " + rounds + ": " + w.Map.Name + ", HP=" + w.Game.Player.HitPoints + ", food=" + w.Game.Player.FoodPoints + ", sleep=" + w.Game.Player.SleepPoints);
    }
    static bool Survival(ScenarioWorld w)
    {
        var p = w.Game.Player;
        if (p.IsSleeping) { Round(w); return true; }
        ActorAction danger = CommunityDiplomacyWorldScenario.ThreatResponse(w);
        if (danger != null) { Check.Equal(true, w.Try(danger), "ordinary visible-threat response"); Round(w); return true; }
        var food = p.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter));
        if (w.Game.Rules.IsActorHungry(p) && food != null) { w.Try(new ActionUseItem(p, w.Game, food)); Round(w); return true; }
        if (w.Game.Rules.IsActorSleepy(p) && w.Game.Rules.CanActorSleep(p)) { w.Try(new ActionSleep(p, w.Game)); Round(w); return true; }
        return false;
    }
    static void Step(ScenarioWorld w, Point goal, int range, bool approachingCompanion = false)
    {
        if (Survival(w)) return;
        Actor observedLag = w.Game.PersonalFollowers().FirstOrDefault(a => BaseAI.Travelling(a) && w.Game.FollowerVisible(a) && !a.IsSleeping && w.Game.Rules.GridDistance(a.Location.Position, w.Game.Player.Location.Position) > 2);
        if (observedLag != null && !approachingCompanion)
        {
            if (w.Game.Rules.IsActorTrustingLeader(observedLag) && rounds % 10 == 0) Check.Call(w.Game, "HandlePlayerRecall", w.Game.Player);
            else w.Try(new ActionWait(w.Game.Player, w.Game));
            Round(w); return;
        }
        var path = CommunityClarityScenario.TryWalkingPath(w, goal, range);
        if (path == null || path.Count == 0) { w.Try(new ActionWait(w.Game.Player, w.Game)); Round(w); return; }
        Point next = path[0]; var door = w.Map.GetMapObjectAt(next) as DoorWindow;
        if (door != null) doors++;
        Check.Equal(true, w.Bump(w.Game.Player, Direction.FromVector(next.X - w.Game.Player.Location.Position.X, next.Y - w.Game.Player.Location.Position.Y)), "ordinary movement/door action"); Round(w);
    }
    static void Walk(ScenarioWorld w, Point goal, int range)
    {
        for (int n = 0; n < 350 && w.Game.Rules.GridDistance(w.Game.Player.Location.Position, goal) > range; n++) Step(w, goal, range);
        Check.Equal(true, w.Game.Rules.GridDistance(w.Game.Player.Location.Position, goal) <= range, "generated route reaches destination");
    }
    static bool Gather(ScenarioWorld w)
    {
        var candidates = w.Map.GroundInventories.Where(i => w.Map.XpdBaseAt(w.Map.GetGroundInventoryPosition(i).Value) == null && i.Items.OfType<ItemFood>().Any(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter)))
            .Select(i => w.Map.GetGroundInventoryPosition(i).Value).OrderBy(p => w.Game.Rules.GridDistance(w.Game.Player.Location.Position, p))
            .Where(p => CommunityClarityScenario.TryWalkingPath(w, p, 1) != null).ToArray();
        if (candidates.Length == 0) return false;
        var point = candidates[0]; Walk(w, point, 1); var stock = w.Map.GetItemsAt(point);
        var food = stock == null ? null : stock.Items.OfType<ItemFood>().FirstOrDefault(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter));
        if (food == null) return false;
        Check.Equal(true, w.Try(new ActionTakeItem(w.Game.Player, w.Game, point, food)), "only existing unclaimed food collected"); Round(w); return true;
    }
    static void Regroup(ScenarioWorld w, Actor f, int range)
    {
        Console.WriteLine("Regroup: player=" + w.Map.Name + "/" + w.Map.Seed + " " + w.Game.Player.Location.Position + "; companion=" + f.Location.Map.Name + "/" + f.Location.Map.Seed + " " + f.Location.Position + "; food=" + f.FoodPoints + "; sleep=" + f.SleepPoints + "; status=" + (((BaseAI)f.Controller).Travel == null ? "unknown" : ((BaseAI)f.Controller).Travel.Status));
        for (int n = 0; n < 180 && (f.Location.Map != w.Map || f.IsSleeping || w.Game.Rules.GridDistance(f.Location.Position, w.Game.Player.Location.Position) > range); n++)
        {
            Check.Equal(false, f.IsDead, "companion survives normal survival and danger decisions");
            if (!Survival(w))
            {
                // A lost follower only knows its last contact. The driver must regain contact too.
                if (w.Game.FollowerVisible(f) && w.Game.Rules.GridDistance(f.Location.Position, w.Game.Player.Location.Position) > range)
                    Step(w, f.Location.Position, range, true);
                else if (!w.Game.FollowerVisible(f) && lastSeen.HasValue &&
                    w.Game.Rules.GridDistance(lastSeen.Value, w.Game.Player.Location.Position) > 1)
                    Step(w, lastSeen.Value, 1, true);
                else { w.Try(new ActionWait(w.Game.Player, w.Game)); Round(w); }
            }
        }
        Check.Same(w.Map, f.Location.Map, "regrouped on actual map"); Check.Equal(false, f.IsSleeping, "companion awake before crossing");
        Check.Equal(true, w.Game.Rules.GridDistance(f.Location.Position, w.Game.Player.Location.Position) <= range, "ordinary NPC decisions reunite party");
    }
    static KeyValuePair<Point, Exit> Cross(ScenarioWorld w, KeyValuePair<Point, Exit> exit, bool deliberate)
    {
        for(int attempt=0;attempt<6;attempt++) {
            if (deliberate || !w.Map.IsInBounds(exit.Key)) ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Y);
            if (w.Map.IsInBounds(exit.Key)) Check.Equal(true, w.Game.DoUseExit(w.Game.Player, exit.Key), "production exit action");
            else Check.Equal(true, w.Game.DoPlayerBump(w.Game.Player, Direction.FromVector(exit.Key.X - w.Game.Player.Location.Position.X, exit.Key.Y - w.Game.Player.Location.Position.Y)), "real boundary crossing input");
            ((ScenarioUI)w.Game.UI).ClearQueuedInput();
            if(w.Game.Player.Location.Map==exit.Value.ToMap) { Round(Current(w));return exit; }
            Console.WriteLine("The attempted crossing remained blocked; automated driver uses ordinary waiting or another existing exit.");
            w.Try(new ActionWait(w.Game.Player,w.Game));Round(w);
            var alternatives=w.Map.ExitEntries.Where(e=>e.Value.ToMap==exit.Value.ToMap&&e.Key!=exit.Key&&
                e.Value.ToMap.GetActorAt(e.Value.ToPosition)==null&&CommunityClarityScenario.TryWalkingPath(w,e.Key,w.Map.IsInBounds(e.Key)?0:1)!=null)
                .OrderBy(e=>w.Game.Rules.GridDistance(w.Game.Player.Location.Position,e.Key)).ToArray();
            if(alternatives.Length>0) { exit=alternatives[0];Walk(w,exit.Key,w.Map.IsInBounds(exit.Key)?0:1); }
        }
        Check.Same(exit.Value.ToMap, w.Game.Player.Location.Map, "crossing remains unavailable after ordinary retries; no destination actor is moved");return exit;
    }
    public static void Register()
    {
        int seed; if (!Int32.TryParse(Environment.GetEnvironmentVariable("ROGUE_FOLLOWER_WORLD_SEED"), out seed)) seed = 5015;
        ScenarioRunner.Add("world/follower-travel-journey", () => TownScenarioFactory.Create(seed, false), initial =>
        {
            try
            {
                CommunityNewWorldScenario.Generate(initial, true, true, Skills.IDs.LEADERSHIP);
                var options = RogueGame.Options; options.SimThread = false;
                typeof(RogueGame).GetField("s_Options", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic).SetValue(null, options);
                var w = Current(initial); Actor p = w.Game.Player; rounds = doors = 0; lastSeen = null; times.Clear();
                Console.WriteLine("Generated journey uses the normal Leadership starting choice; no later skill grants.");
                for (int n = 0; n < 10 && NpcIntentSupport.FoodUnits(p) < 6; n++) Gather(w);
                Actor follower = null;
                for (int n = 0; n < 250 && follower == null; n++)
                {
                    Actor target = w.Map.Actors.Where(a => !a.IsDead && !a.IsSleeping && !a.HasLeader && a.CountFollowers == 0 && a.Faction == w.Game.GameFactions.TheCivilians && w.Game.Rules.CanActorTakeLead(p, a))
                        .Where(a => CommunityClarityScenario.TryWalkingPath(w, a.Location.Position, 1) != null)
                        .OrderByDescending(a => PersonalitySystem.Bias(a, DecisionKind.Group)).ThenBy(a => w.Game.Rules.GridDistance(p.Location.Position, a.Location.Position)).FirstOrDefault();
                    Check.Equal(true, target != null, "existing generated survivor eligible for recruitment");
                    if (w.Game.Rules.GridDistance(p.Location.Position, target.Location.Position) > 1) { Step(w, target.Location.Position, 1); continue; }
                    Check.Equal(true, w.Try(new ActionTakeLead(p, w.Game, target)), "real recruitment action"); follower = target; Round(w);
                }
                Check.Equal(true, follower != null, "recruited ordinary survivor"); Guid id = follower.PersonalityIdentity;
                Console.WriteLine("Recruited " + follower.UnmodifiedName + " at round " + rounds + "; initial trust=" + follower.TrustInLeader);
                // Let actual time together and ordinary giving earn recall trust.
                for (int n = 0; n < 900 && !w.Game.Rules.IsActorTrustingLeader(follower); n++)
                {
                    Check.Equal(false, follower.IsDead, "recruited survivor remains alive"); Check.Same(p, follower.Leader, "ordinary relationship remains intact");
                    if (Survival(w)) continue;
                    if (w.Game.Rules.GridDistance(p.Location.Position, follower.Location.Position) > 1) { Step(w, follower.Location.Position, 1); continue; }
                    var gift = p.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter));
                    if (!follower.IsSleeping && w.Game.Rules.IsActorHungry(follower) && gift != null)
                    {
                        int slot = p.Inventory.Items.ToList().IndexOf(gift); ((ScenarioUI)w.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, follower.Location.Position));
                        Check.Equal(true, Check.Call(w.Game, "HandlePlayerGiveItem", p, CommunityClarityScenario.InventorySlot(w, slot)), "real aid contributes to trust"); Round(w);
                    }
                    else { w.Try(new ActionWait(p, w.Game)); Round(w); }
                    if (NpcIntentSupport.FoodUnits(p) < 2 && rounds % 30 == 0) Gather(w);
                }
                Check.Equal(true, w.Game.Rules.IsActorTrustingLeader(follower), "ordinary travel/aid earns recall trust");
                Console.WriteLine("Trust earned at round " + rounds + "; companion map=" + follower.Location.Map.Name + "; sleep=" + follower.SleepPoints);
                // Use the existing O directives to favor retreat while testing travel, with no stat protection.
                Regroup(w, follower, 2);
                for (int n = 0; n < 100 && !w.Game.FollowerOrderLink(follower); n++)
                    Step(w, w.Game.FollowerVisible(follower) ? follower.Location.Position : lastSeen.Value, 1, true);
                Check.Equal(true, w.Game.FollowerOrderLink(follower), "move into actual mutual order contact before O");
                var menu = (ScenarioUI)w.Game.UI; menu.QueueWaitKey(Keys.D1);
                int changes = 0;
                for (ActorCourage c = ((BaseAI)follower.Controller).Directives.Courage; c != ActorCourage.COWARD; c = c == ActorCourage.CAUTIOUS ? ActorCourage.COURAGEOUS : ActorCourage.COWARD) changes++;
                for (int n = 0; n < changes; n++) menu.QueueWaitKey(Keys.D6);
                menu.QueueWaitKey(Keys.Escape); menu.QueueWaitKey(Keys.Escape); Check.Call(w.Game, "HandlePlayerOrderMode", p);
                Check.Equal(ActorCourage.COWARD, ((BaseAI)follower.Controller).Directives.Courage, "actual O directs retreat during travel");
                w.Try(new ActionWait(p, w.Game)); Round(w);
                var stairs = w.Map.ExitEntries.Where(e => e.Value.ToMap == w.Map.District.SewersMap && w.Map.IsInBounds(e.Key) && e.Value.ToMap.GetActorAt(e.Value.ToPosition) == null && CommunityClarityScenario.TryWalkingPath(w, e.Key, 0) != null)
                    .OrderBy(e => w.Game.Rules.GridDistance(p.Location.Position, e.Key)).First();
                Map source = w.Map; Walk(w, stairs.Key, 0); Regroup(w, follower, 2);
                // Natural following stops at radius two; leave when not adjacent, without moving the NPC.
                for (int n = 0; n < 40 && w.Game.Rules.GridDistance(p.Location.Position, follower.Location.Position) <= 1; n++) { w.Try(new ActionWait(p, w.Game)); Round(w); }
                Check.Equal(true, w.Game.Rules.GridDistance(p.Location.Position, follower.Location.Position) > 1, "deliberate separation starts with an unready real follower");
                stairs=Cross(w, stairs, true); w = Current(w); Check.Same(source, follower.Location.Map, "unready companion stays in its area"); Check.Same(p, follower.Leader, "separation does not dismiss");
                Console.WriteLine("Deliberate departure to " + w.Map.Name + "; prior map advances only with existing simulation.");
                var back = w.Map.ExitEntries.First(e => e.Value.ToMap == source && e.Value.ToPosition == stairs.Key && w.Map.IsInBounds(e.Key)); Walk(w, back.Key, 0); Cross(w, back, true); w = Current(w);
                bool answered = false;
                for (int call = 0; call < 6 && !answered; call++)
                {
                    if (Survival(w)) continue;
                    Console.WriteLine("Recall: companion=" + follower.Location.Position + ", sleeping=" + follower.IsSleeping + ", stamina=" + follower.StaminaPoints + ", HP=" + follower.HitPoints + ", trust=" + follower.TrustInLeader);
                    ((ScenarioUI)w.Game.UI).QueueKey(Keys.R | Keys.Control); p.ActionPoints = 100; Check.Call(w.Game, "HandlePlayerActor", p); Round(w);
                    answered = FollowerTravelScenario.Display(w).Contains("Coming back!");
                    for (int wait = 0; wait < 4 && !answered; wait++)
                    {
                        if (!Survival(w)) { w.Try(new ActionWait(p, w.Game)); Round(w); }
                        answered = FollowerTravelScenario.Display(w).Contains("Coming back!");
                    }
                }
                Check.Equal(true, answered, "fresh journey hears an actual recall acknowledgment after higher-priority actions");
                Regroup(w, follower, 2); Console.WriteLine("Actual recall and ordinary turns reunite with " + follower.UnmodifiedName + ".");
                string save = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "follower-world-reunited-format-5.dat"); BinarySaveStore.Save(save, Session.Get);
                Check.Equal(true, Check.Call(w.Game, "LoadGame", save), "production reload during generated journey"); w = Current(w); p = w.Game.Player;
                follower = w.Map.Actors.Single(a => a.PersonalityIdentity == id); Check.Same(p, follower.Leader, "reload preserves recruitment and travel");
                source = w.Map;
                // Cross a real district boundary together after ordinary movement and regrouping.
                var districtExit = w.Map.ExitEntries.Where(e => e.Value.ToMap.District != w.Map.District && e.Value.ToMap.GetActorAt(e.Value.ToPosition) == null && CommunityClarityScenario.TryWalkingPath(w, e.Key, 1) != null)
                    .OrderBy(e => w.Game.Rules.GridDistance(p.Location.Position, e.Key)).First();
                Walk(w, districtExit.Key, 1); Point boundary = new Point(Math.Max(0, Math.Min(w.Map.Width - 1, districtExit.Key.X)), Math.Max(0, Math.Min(w.Map.Height - 1, districtExit.Key.Y))); Point meeting = lastSeen.Value;
                districtExit=Cross(w, districtExit, true);boundary=new Point(Math.Max(0,Math.Min(source.Width-1,districtExit.Key.X)),Math.Max(0,Math.Min(source.Height-1,districtExit.Key.Y)));w = Current(w);
                Check.Same(p, follower.Leader, "district crossing cannot automatically dismiss personal follower");
                Console.WriteLine("Boundary departure leaves dated last contact at " + meeting + "; return uses that observation.");
                var districtBack = w.Map.ExitEntries.First(e => e.Value.ToMap == source && e.Value.ToPosition == boundary);
                Walk(w, districtBack.Key, w.Map.IsInBounds(districtBack.Key) ? 0 : 1); Cross(w, districtBack, true); w = Current(w);
                Walk(w, meeting, 1); ((ScenarioUI)w.Game.UI).QueueKey(Keys.R | Keys.Control); p.ActionPoints = 100; Check.Call(w.Game, "HandlePlayerActor", p); Round(w);
                Regroup(w, follower, 2); Check.Equal(false, follower.IsDead, "final real reunion retains a living companion");
                BinarySaveStore.Save(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "follower-world-complete-format-5.dat"), Session.Get);
                var ordered = times.OrderBy(t => t).ToArray();
                Console.WriteLine("Fresh generated follower journey: " + rounds + " ordinary scheduler/survival rounds, " + doors + " doorway steps; recruitment, aid/trust, O retreat directive, area departure, recall/reunion, real reload and district departure/return/reunion. Median " + ordered[ordered.Length / 2].ToString("F2") + " ms, p95 " + ordered[(int)(ordered.Length * .95)].ToString("F2") + " ms. Automated driving; no human visual review or protection.");
            }
            catch
            {
                string failed = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "follower-world-failure-" + seed + "-" + DateTime.UtcNow.Ticks + ".dat");
                try { BinarySaveStore.Save(failed, Session.Get); Console.WriteLine("Preserved failed journey: " + failed); } catch (Exception e) { Console.WriteLine(e.Message); } throw;
            }
        });
    }
}
