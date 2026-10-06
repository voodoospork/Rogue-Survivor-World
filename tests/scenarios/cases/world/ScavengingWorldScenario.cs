using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.Personality;

// The driver can route through generated geometry. Supplies, needs, transfers and notebook knowledge
// are production state; every travel/wait action advances real local NPC and survival turns.
static class ScavengingWorldScenario
{
    static int rounds;
    static readonly System.Collections.Generic.List<double> times = new System.Collections.Generic.List<double>();
    static void Round(ScenarioWorld w)
    {
        var watch = Stopwatch.StartNew(); CommunityClarityScenario.AmbientTurn(w); watch.Stop(); times.Add(watch.Elapsed.TotalMilliseconds); rounds++;
        Check.Equal(false, w.Game.Player.IsDead, "expedition survivor remains alive without added resources/protection");
        if (rounds % 60 == 0) Console.WriteLine("Scavenging travel turn " + w.Map.LocalTime.TurnCounter + "; HP=" + w.Game.Player.HitPoints + "; food=" + w.Game.Player.FoodPoints);
    }
    static bool Survival(ScenarioWorld w)
    {
        var p = w.Game.Player;
        if (p.IsSleeping) { Round(w); return true; }
        var danger = CommunityDiplomacyWorldScenario.ThreatResponse(w);
        if (danger != null) { Check.Equal(true, w.Try(danger), "normal visible-threat response"); Round(w); return true; }
        var food = p.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter));
        if (w.Game.Rules.IsActorHungry(p) && food != null) { w.Try(new ActionUseItem(p, w.Game, food)); Round(w); return true; }
        if (w.Game.Rules.IsActorSleepy(p) && w.Game.Rules.CanActorSleep(p)) { w.Try(new ActionSleep(p, w.Game)); Round(w); return true; }
        return false;
    }
    static void Step(ScenarioWorld w, Point goal, int range)
    {
        if (Survival(w)) return;
        var path = CommunityClarityScenario.TryWalkingPath(w, goal, range);
        if (path == null || path.Count == 0) { w.Try(new ActionWait(w.Game.Player, w.Game)); Round(w); return; }
        var next = path[0]; var current = w.Game.Player.Location.Position;
        Check.Equal(true, w.Bump(w.Game.Player, Direction.FromVector(next.X - current.X, next.Y - current.Y)), "expedition uses real walking/door actions"); Round(w);
    }
    static void Walk(ScenarioWorld w, Point goal, int range)
    {
        for (int n = 0; n < 400 && w.Game.Rules.GridDistance(w.Game.Player.Location.Position, goal) > range; n++) Step(w, goal, range);
        if (w.Game.Rules.GridDistance(w.Game.Player.Location.Position, goal) > range)
        {
            Console.WriteLine("Blocked trial route: player=" + w.Game.Player.Location.Position + ", goal=" + goal + ", range=" + range +
                ", target object=" + (w.Map.GetMapObjectAt(goal) == null ? "none" : w.Map.GetMapObjectAt(goal).ImageID) + ", path=" +
                (CommunityClarityScenario.TryWalkingPath(w, goal, range) == null ? "unreachable" : "reachable"));
            BinarySaveStore.Save(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "scavenging-world-blocked-" + w.Seed + "-" + DateTime.UtcNow.Ticks + ".dat"), Session.Get);
        }
        Check.Equal(true, w.Game.Rules.GridDistance(w.Game.Player.Location.Position, goal) <= range, "ordinary expedition reaches chosen point");
    }
    static bool Gather(ScenarioWorld w, string resource)
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            var candidates = w.Map.GroundInventories.Where(i => w.Map.XpdBaseAt(w.Map.GetGroundInventoryPosition(i).Value) == null &&
                ScavengingSystem.Count(w.Game, i, resource, w.Map.LocalTime.TurnCounter) > 0)
                .Select(i => w.Map.GetGroundInventoryPosition(i).Value)
                .OrderBy(p => w.Game.Rules.GridDistance(w.Game.Player.Location.Position, p))
                .Where(p => CommunityClarityScenario.TryWalkingPath(w, p, 1) != null).ToArray();
            if (candidates.Length == 0) return false;
            var point = candidates[0]; Walk(w, point, 1);
            var inventory = w.Map.GetItemsAt(point);
            if (inventory == null) continue;
            var item = inventory.Items.FirstOrDefault(i => resource == "food" ? i is ItemFood && !w.Game.Rules.IsFoodSpoiled((ItemFood)i, w.Map.LocalTime.TurnCounter) :
                i is ItemMedicine && ((ItemMedicine)i).Healing > 0);
            if (item == null) continue;
            Check.Equal(true, w.Game.Player.Personality.Scavenging.Leads.Any(l => l.Resource == resource && !l.SiteOnly && l.Place.Anchor == point && l.Latest.Source == "seen by you"), "stock enters notebook through actual visibility before pickup");
            Check.Equal(true, w.Try(new ActionTakeItem(w.Game.Player, w.Game, point, item)), "collect only preexisting unclaimed generated stock"); Round(w); return true;
        }
        return false;
    }
    static void Talk(ScenarioWorld w, Actor leader, Keys key)
    {
        var p = w.Game.Player; var ui = (ScenarioUI)w.Game.UI;
        int adjacent = w.Map.Actors.Count(a => a != p && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent && !w.Game.Rules.AreEnemies(p, a) && w.Game.Rules.GridDistance(p.Location.Position, a.Location.Position) <= 1);
        if (adjacent > 1) ui.QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, leader.Location.Position));
        ui.QueueWaitKey(key); Check.Equal(true, Check.Call(w.Game, "HandlePlayerTalk", p), "generated expedition uses actual V input"); Round(w);
    }
    public static void Register()
    {
        int seed; if (!Int32.TryParse(Environment.GetEnvironmentVariable("ROGUE_SCAVENGING_WORLD_SEED"), out seed)) seed = 5015;
        ScenarioRunner.Add("world/scavenging-expeditions", () => TownScenarioFactory.Create(seed, false), initial =>
        {
            try
            {
            Actor leader = CommunityNewWorldScenario.Generate(initial);
            var w = new ScenarioWorld(seed, initial.Game.Player.Location.Map, initial.Game); var p = w.Game.Player;
            rounds = 0; times.Clear(); Point departure = p.Location.Position;
            ScavengingScenario.Plan(w);
            var personal = p.Personality.Scavenging.Active;
            Console.WriteLine("Personal scavenging departure " + departure + "; carried food=" + personal.DepartureFood);
            Check.Equal(true, Gather(w, "food"), "first personal search finds existing supplies");
            for (int n = 0; n < 3 && ScavengingSystem.Count(w.Game, p.Inventory, "food", w.Map.LocalTime.TurnCounter) <= personal.DepartureFood; n++) Gather(w, "food");
            Console.WriteLine("Personal gathered, turn " + w.Map.LocalTime.TurnCounter + "; returning to " + departure);
            Walk(w, departure, 1); ScavengingScenario.Journal(w, Keys.E, Keys.Y);
            Check.Equal(true, personal.Returned && personal.FinalFood > personal.DepartureFood, "fresh personal trip brings supplies back");
            Console.WriteLine("Personal expedition returned at turn " + w.Map.LocalTime.TurnCounter + "; net food " + (personal.FinalFood - personal.DepartureFood));
            int lastNews = -1000;
            for (int attempt = 0; attempt < 1000 && !p.Personality.Commitments.Any(c => c.Promisor == p.PersonalityIdentity && c.Status == NpcCommitmentStatus.Active); attempt++)
            {
                if (leader.IsDead || leader.Location.Map != w.Map || !CommunityMembershipSystem.IsLeader(w.Game, leader) || w.Game.Rules.AreEnemies(p, leader))
                    leader = w.Map.Actors.Where(a => CommunityMembershipSystem.IsLeader(w.Game, a) && !a.IsDead && !w.Game.Rules.AreEnemies(p, a))
                        .OrderBy(a => w.Game.Rules.GridDistance(p.Location.Position, a.Location.Position)).FirstOrDefault();
                Check.Equal(true, leader != null, "an independently surviving community can receive expedition help");
                if (Survival(w)) continue;
                if (w.Game.Rules.GridDistance(p.Location.Position, leader.Location.Position) > 1) { Step(w, leader.Location.Position, 1); continue; }
                if (leader.IsSleeping) { w.Try(new ActionWait(p, w.Game)); Round(w); continue; }
                if (NpcConversation.Pending(p, leader) != null) { Talk(w, leader, Keys.Y); continue; }
                if (w.Map.LocalTime.TurnCounter - lastNews >= 60) { lastNews = w.Map.LocalTime.TurnCounter; Talk(w, leader, Keys.D1); }
                else { w.Try(new ActionWait(p, w.Game)); Round(w); }
            }
            var promise = p.Personality.Commitments.FirstOrDefault(c => c.Promisor == p.PersonalityIdentity && c.Status == NpcCommitmentStatus.Active);
            Check.Equal(true, promise != null, "fresh community offers a real unmet resource need");
            ScavengingScenario.Plan(w, true); var job = p.Personality.Scavenging.Active;
            Check.Equal(promise.Id, job.CommitmentId, "accepted request is linked by saved commitment ID");
            string save = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "scavenging-world-active-format-5.dat");
            BinarySaveStore.Save(save, Session.Get); Check.Equal(true, Check.Call(w.Game, "LoadGame", save), "real reload retains active generated expedition");
            w = new ScenarioWorld(seed, w.Game.Player.Location.Map, w.Game); p = w.Game.Player; job = p.Personality.Scavenging.Active;
            promise = p.Personality.Commitments.Single(c => c.Id == job.CommitmentId);
            Check.Equal(true, Gather(w, job.Resource), "request expedition scavenges actual generated matching stock");
            Actor beneficiary = w.Map.Actors.FirstOrDefault(a => a.PersonalityIdentity == promise.Beneficiary && !a.IsDead);
            Check.Equal(true, beneficiary != null, "real request beneficiary survives until delivery");
            for (int attempt = 0; attempt < 180 && (w.Game.Rules.GridDistance(p.Location.Position, beneficiary.Location.Position) > 1 || beneficiary.IsSleeping); attempt++)
            {
                Check.Equal(false, beneficiary.IsDead, "beneficiary remains alive during return");
                if (!p.IsSleeping && beneficiary.IsSleeping && w.Game.Rules.GridDistance(p.Location.Position, beneficiary.Location.Position) <= 1 && !Survival(w))
                { Check.Equal(true, w.Try(new ActionShout(p, w.Game, "Wake up! I brought your supplies!")), "courier uses ordinary noisy wake-up chance"); Round(w); }
                else Step(w, beneficiary.Location.Position, 1);
            }
            if (w.Map.LocalTime.TurnCounter > promise.DueTurn)
            {
                Console.WriteLine("Expired generated trip: promised=" + job.DepartedTurn + ", due=" + promise.DueTurn + ", now=" + w.Map.LocalTime.TurnCounter +
                    ", beneficiary=" + beneficiary.UnmodifiedName + ", sleeping=" + beneficiary.IsSleeping + ", distance=" + w.Game.Rules.GridDistance(p.Location.Position, beneficiary.Location.Position));
                BinarySaveStore.Save(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "scavenging-world-expired-" + seed + "-" + DateTime.UtcNow.Ticks + ".dat"), Session.Get);
            }
            Check.Equal(true, w.Map.LocalTime.TurnCounter <= promise.DueTurn, "expedition meets unchanged deadline");
            var gift = p.Inventory.Items.First(i => job.Resource == "food" ? i is ItemFood && !w.Game.Rules.IsFoodSpoiled((ItemFood)i, w.Map.LocalTime.TurnCounter) : i is ItemMedicine && ((ItemMedicine)i).Healing > 0);
            int slot = p.Inventory.Items.ToList().IndexOf(gift); ((ScenarioUI)w.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, beneficiary.Location.Position));
            Check.Equal(true, Check.Call(w.Game, "HandlePlayerGiveItem", p, CommunityClarityScenario.InventorySlot(w, slot)), "generated job delivers with actual G input"); Round(w);
            Check.Equal(NpcCommitmentStatus.Kept, promise.Status, "actual giving fulfills real generated request");
            Walk(w, job.Return.Anchor, 1); ScavengingScenario.Journal(w, Keys.E, Keys.Y);
            Check.Equal(true, job.Returned, "request expedition returns to recorded departure point");
            BinarySaveStore.Save(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "scavenging-world-complete-format-5.dat"), Session.Get);
            var ordered = times.OrderBy(t => t).ToArray();
            Console.WriteLine("Fresh generated scavenging trial: " + rounds + " real scheduler/survival rounds; personal food expedition and " + job.Resource + " request for " + promise.BeneficiaryName + " completed. Median round " + ordered[ordered.Length / 2].ToString("F2") + "ms, p95 " + ordered[(int)(ordered.Length * .95)].ToString("F2") + "ms. No spawned supplies, forced needs, stat protection or human visual review.");
            }
            catch
            {
                string failed = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "scavenging-world-failure-" + seed + "-" + DateTime.UtcNow.Ticks + ".dat");
                try { BinarySaveStore.Save(failed, Session.Get); Console.WriteLine("Preserved failed generated expedition: " + failed); }
                catch (Exception saveError) { Console.WriteLine("Failed trial snapshot could not be written: " + saveError.Message); }
                throw;
            }
        });
    }
}
