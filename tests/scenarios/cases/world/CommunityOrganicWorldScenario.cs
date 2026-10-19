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
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Personality;

// Ordinary scheduler/survival, automated map-aware walking. No added stock, protection or scripted crisis.
static class CommunityOrganicWorldScenario
{
    static bool Move(ScenarioWorld w, Point destination, int range)
    {
        var path = CommunityClarityScenario.TryWalkingPath(w, destination, range);
        if (path == null || path.Count == 0) return false;
        Point next = path[0]; return w.Bump(w.Game.Player, Direction.FromVector(next.X - w.Game.Player.Location.Position.X, next.Y - w.Game.Player.Location.Position.Y));
    }
    static bool Survive(ScenarioWorld w)
    {
        var p = w.Game.Player;
        if (p.IsSleeping) return true;
        var danger = CommunityDiplomacyWorldScenario.ThreatResponse(w); if (danger != null && danger.IsLegal()) { danger.Perform(); return true; }
        var food = p.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter));
        if (w.Game.Rules.IsActorHungry(p) && food != null) return w.Try(new ActionUseItem(p, w.Game, food));
        if (w.Game.Rules.IsActorSleepy(p) && w.Game.Rules.CanActorSleep(p)) return w.Try(new ActionSleep(p, w.Game));
        // The automated player gathers a small surplus for voluntary aid from real,
        // unclaimed stock, while urgent eating, danger and sleep remain first.
        if (food == null || NpcIntentSupport.FoodUnits(p) < 2 && !w.Game.Rules.IsActorHungry(p))
        {
            var supply = w.Map.GroundInventories.Where(i => i.Items.OfType<ItemFood>().Any(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter)))
                .Select(i => w.Map.GetGroundInventoryPosition(i).Value).Where(point => w.Map.XpdBaseAt(point) == null || w.Map.XpdBaseAt(point).Owns(p))
                .OrderBy(point => w.Game.Rules.GridDistance(p.Location.Position, point)).FirstOrDefault(point => CommunityClarityScenario.TryWalkingPath(w, point, 1) != null);
            var stock = w.Map.GetItemsAt(supply);
            if (stock != null)
            {
                if (w.Game.Rules.GridDistance(p.Location.Position, supply) <= 1)
                { var f = stock.Items.OfType<ItemFood>().FirstOrDefault(item => !w.Game.Rules.IsFoodSpoiled(item, w.Map.LocalTime.TurnCounter));
                    if (f != null) return w.Try(new ActionTakeItem(p, w.Game, supply, f)); }
                if (Move(w, supply, 1)) return true;
            }
        }
        return false;
    }
    static bool Talk(ScenarioWorld w, Actor chief)
    {
        Actor p = w.Game.Player; var ui = (ScenarioUI)w.Game.UI;
        int nearby = w.Map.Actors.Count(a => a != p && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent &&
            w.Game.Rules.GridDistance(a.Location.Position, p.Location.Position) <= 1 && !w.Game.Rules.AreEnemies(p, a));
        if (nearby > 1) ui.QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, chief.Location.Position));
        ui.QueueWaitKey(NpcConversation.Pending(p, chief) == null ? Keys.D1 : Keys.Y);
        return (bool)Check.Call(w.Game, "HandlePlayerTalk", p);
    }
    public static void Register()
    {
        foreach (int seed in new[] { 5210, 5211, 5212 })
        {
            int trialSeed = seed;
            ScenarioRunner.Add("world/community-organic-trial-" + seed, () => TownScenarioFactory.Create(trialSeed, false), initial =>
            {
                CommunityNewWorldScenario.Generate(initial, false, true, Skills.IDs.CARPENTRY, true);
                var w = new ScenarioWorld(trialSeed, initial.Game.Player.Location.Map, initial.Game);
                Check.Equal(false, Session.Get.GamePreset.InitialCommunities, "fresh World has no seeded civilian settlements");
                Check.Equal(true, Session.Get.GamePreset.OrganicCommunities, "fresh World enables organic decisions");
                Check.Equal(0, w.Map.Actors.Count(a => a.Community != null), "generation itself creates no civilian tribe");
                Check.Equal(true, w.Map.Actors.Any(a => a.Faction == w.Game.GameFactions.ThePolice && a.SocialGroup != null &&
                    a.SocialGroup.CommunityName.Contains("police patrol")), "organic World retains named local police organization");
                var groups = new Dictionary<Guid, SocialGroup>(); var peaks = new Dictionary<Guid, int>();
                var cultures=new Dictionary<Guid,SocialGroup>();var warned=new HashSet<long>();var repaid=new HashSet<long>();var feuds=new HashSet<string>();
                var lines = new List<string> { "seed=" + trialSeed + "; automated driving; ordinary survival; no crisis provocation; no human visual review" };
                int conversations = 0, usefulAid = 0, leaveTurn = -1; Guid met = Guid.Empty;
                bool left = false, returned = false; Point helpedAt = Point.Empty; int aidDate = -1;
                Point departure = w.Game.Player.Location.Position; var times = new List<double>();
                for (int n = 0; n < 1080 && !w.Game.Player.IsDead; n++)
                {
                    Actor p = w.Game.Player; p.ActionPoints = Rules.BASE_ACTION_COST;
                    foreach (Actor actor in w.Map.Actors)
                        if (actor.Community != null) { var group = actor.Community; groups[group.Identity] = group;
                            peaks[group.Identity] = Math.Max(peaks.ContainsKey(group.Identity) ? peaks[group.Identity] : 0, group.Tribe.Residents.Count(a => !a.IsDead)); }
                    foreach(Actor actor in w.Map.Actors)if(actor.SocialGroup!=null&&actor.SocialGroup.Culture!=null) {
                        var group=actor.SocialGroup;cultures[group.Identity]=group;
                        foreach(var c in group.Culture.Cases) { if(c.WarnedClock>=0||c.DemandClock>=0)warned.Add(c.Episode);
                            if(c.Losses.Any(l=>l.Repaid>0||l.Repaired))repaid.Add(c.Episode); }
                        if(group.Diplomacy!=null)foreach(var r in group.Diplomacy.Relations)if(r.Feud)feuds.Add(group.Identity+"/"+r.Target.Group);
                    }
                    if (!Survive(w))
                    {
                        if (leaveTurn >= 0 && w.Game.Rules.GridDistance(p.Location.Position, helpedAt) >= 8) left = true;
                        Actor chief = w.Map.Actors.Where(a => a.CommunityChief == a && !a.IsDead && (usefulAid == 0 || a.Community.Identity == met) &&
                            !w.Game.Rules.AreEnemies(p, a) && CommunityClarityScenario.TryWalkingPath(w, a.Location.Position, 1) != null)
                            .OrderBy(a => w.Game.Rules.GridDistance(p.Location.Position, a.Location.Position)).FirstOrDefault();
                        bool acted = false;
                        if (leaveTurn >= 0 && n - leaveTurn < 40) acted = Move(w, departure, 0);
                        else if (chief != null)
                        {
                            Actor needy = null; Item gift = null;
                            var known = p.Personality.FindCommunity(chief.Community.Identity);
                            if (known != null && usefulAid == 0)
                            {
                                foreach (Actor member in w.Map.Actors.Where(a => !a.IsDead && !a.IsSleeping && known.Members.Any(m => m.Id == a.PersonalityIdentity) &&
                                    NpcKnowledgeSystem.Visible(w.Game, p, a.Location)).OrderBy(a => w.Game.Rules.GridDistance(p.Location.Position, a.Location.Position)))
                                { if (w.Game.Rules.IsActorHungry(member)) gift = p.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f => !w.Game.Rules.IsFoodSpoiled(f, w.Map.LocalTime.TurnCounter));
                                    else if (member.HitPoints < w.Game.Rules.ActorMaxHPs(member) / 2) gift = p.Inventory.Items.OfType<ItemMedicine>().FirstOrDefault(m => m.Healing > 0);
                                    if (gift != null) { needy = member; break; } }
                            }
                            if (needy != null && gift != null)
                            {
                                if (w.Game.Rules.GridDistance(p.Location.Position, needy.Location.Position) > 1) acted = Move(w, needy.Location.Position, 1);
                                else {
                                    int slot = 0; while (p.Inventory[slot] != gift) slot++;
                                    var ui = (ScenarioUI)w.Game.UI; ui.QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, needy.Location.Position));
                                    acted = (bool)Check.Call(w.Game, "HandlePlayerGiveItem", p, CommunityClarityScenario.InventorySlot(w, slot));
                                    if (acted) { usefulAid++; leaveTurn = n; helpedAt = needy.Location.Position; met = chief.Community.Identity;
                                        aidDate = Session.Get.WorldTime.TurnCounter; lines.Add("Useful aid at round " + n + " to " + needy.UnmodifiedName); }
                                }
                            }
                            else if (w.Game.Rules.GridDistance(p.Location.Position, chief.Location.Position) > 1) acted = Move(w, chief.Location.Position, 1);
                            else if (chief.IsSleeping && left && !returned) acted = (bool)Check.Call(w.Game, "HandlePlayerShout", p, "Hello, is anybody awake?");
                            else if (!chief.IsSleeping && (conversations == 0 || left && !returned || n % 90 == 0))
                            { acted = Talk(w, chief); if (acted) { conversations++; met = chief.Community.Identity;
                                    if (left && n - leaveTurn >= 40) { returned = true; lines.Add("Returned and used V at round " + n + " after ordinary travel and survival."); } } }
                        }
                        else if (left && usefulAid > 0)
                        {
                            KnownCommunity known = p.Personality.FindCommunity(met);
                            if (known != null && known.ShelterLocation != null && known.ShelterLocation.Matches(w.Map, known.ShelterLocation.Anchor))
                            { acted = Move(w, known.ShelterLocation.Anchor, 1); if (known.ShelterLocation.Matches(w.Map, p.Location.Position)) returned = true; }
                        }
                        if (!acted) w.Try(new ActionWait(p, w.Game));
                    }
                    var clock = Stopwatch.StartNew();
                    ((ScenarioUI)w.Game.UI).ContinueImportantMessages = true;
                    try { CommunityClarityScenario.AmbientTurn(w); }
                    catch (Exception e) when (p.IsDead)
                    { lines.Add("Player died during an ordinary scheduler turn; headless death-screen limitation: " + e.GetBaseException().Message); }
                    finally { ((ScenarioUI)w.Game.UI).ContinueImportantMessages = false; }
                    clock.Stop(); times.Add(clock.Elapsed.TotalMilliseconds);
                    if (n % 180 == 0) Console.WriteLine("Organic trial " + trialSeed + " round " + n + ": tribes=" + groups.Count + ", HP=" + p.HitPoints + ", food=" + p.FoodPoints);
                }
                foreach (SocialGroup group in groups.Values)
                {
                    lines.Add(group.CommunityName + " | founded=" + group.Tribe.FoundedTurn + " | peak=" + peaks[group.Identity] +
                        " | committed=" + group.Tribe.Residents.Count(a => !a.IsDead) + " | shortage reviews=" + group.Tribe.ShortageReviews +
                        " | dissolved=" + group.Tribe.Dissolved);
                    foreach (CommunityNote note in group.Tribe.History) lines.Add("  turn " + note.EventTurn + " " + note.Kind + ": " + note.Text);
                }
                KnownCommunity returnedKnowledge = met == Guid.Empty ? null : w.Game.Player.Personality.FindCommunity(met);
                var consequences = returnedKnowledge == null ? new CommunityNote[0] : returnedKnowledge.Notes.Where(note => note.EventTurn > aidDate &&
                    (note.Kind.StartsWith("community_") || note.Kind == "home_apparently_empty" || note.Kind == "death")).ToArray();
                if (returned) foreach (CommunityNote consequence in consequences)
                    lines.Add("Dated consequence learned: " + consequence.Kind + " at " + consequence.EventTurn + " from " + consequence.Source + ": " + consequence.Text);
                lines.Add("rounds=" + times.Count + "; tribes=" + groups.Count + "; conversations=" + conversations + "; aid=" + usefulAid + "; left=" + left + "; returned=" + returned + "; learnedChange=" + (returned && consequences.Length > 0) +
                    "; met=" + met + "; playerDied=" + w.Game.Player.IsDead + "; medianRoundMs=" + (times.Count == 0 ? 0 : times.OrderBy(t => t).ElementAt(times.Count / 2)));
                foreach(var group in cultures.Values)lines.Add("Culture diagnostic: "+group.CommunityName+" | T="+group.Culture.Territoriality+
                    " | E="+group.Culture.Enforcement+" | G="+group.Culture.Grudge+" | revision="+group.Culture.Revision);
                lines.Add("Character observations: warnings/demands="+warned.Count+"; repayments="+repaid.Count+"; declared feuds="+feuds.Count+
                    "; dissolved tribes="+groups.Values.Count(g=>g.Tribe.Dissolved)+"; cultures="+cultures.Count+". Diagnostic scores are not player knowledge.");
                File.WriteAllLines(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "character-organic-world-" + trialSeed + ".txt"), lines);
                BinarySaveStore.Save(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "character-organic-world-" + trialSeed + "-format-5.dat"), Session.Get);
                Console.WriteLine(lines.Last());
                // Measurements include zero formation or player loss; they are not guaranteed-success fixtures.
                Check.Equal(true, times.Count > 0, "generated trial executes real scheduler and survival rounds");
            });
        }
    }
}
