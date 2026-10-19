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
using djack.RogueSurvivor.Gameplay.Personality;

// Headless play driver: generated resources, production input/actions and full local NPC/metabolism rounds.
// Path planning knows the map; journal knowledge still comes exclusively from production observations.
static class CommunityMembershipWorldScenario
{
    static int rounds, sleepingRounds, watchedRounds;
    static int TrialSeed { get { int seed; return int.TryParse(Environment.GetEnvironmentVariable("ROGUE_MEMBERSHIP_WORLD_SEED"),out seed) ? seed : 4933; } }
    static readonly List<double> roundTimes = new List<double>();
    static void Round(ScenarioWorld world)
    {
        Check.Equal(false, world.Game.Player.IsDead, "fresh survivor remains alive without special protection");
        long started = Stopwatch.GetTimestamp();
        CommunityClarityScenario.AmbientTurn(world); rounds++;
        roundTimes.Add((Stopwatch.GetTimestamp() - started) * 1000.0 / Stopwatch.Frequency);
        Actor player = world.Game.Player;
        if (player.IsSleeping) sleepingRounds++;
        if (player.CommunityMembership != null && player.SocialGroup.Home.Watcher != Guid.Empty) watchedRounds++;
        if (rounds % 60 == 0) Console.WriteLine("World trial turn " + world.Map.LocalTime.TurnCounter +
            ": HP=" + player.HitPoints + ", food=" + player.FoodPoints + ", sleep=" + player.SleepPoints +
            ", sleeping=" + player.IsSleeping + ", membership=" + (player.CommunityMembership != null));
    }
    static bool Eat(ScenarioWorld world)
    {
        Actor player = world.Game.Player;
        ItemFood food = player.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f =>
            !world.Game.Rules.IsFoodSpoiled(f, world.Map.LocalTime.TurnCounter));
        if (!world.Game.Rules.IsActorHungry(player) || food == null) return false;
        Check.Equal(true, world.Try(new ActionUseItem(player, world.Game, food)), "survivor eats only generated carried food");
        Round(world); return true;
    }
    static bool Safety(ScenarioWorld world)
    {
        var defense=CommunityDiplomacyWorldScenario.ThreatResponse(world);
        if(defense==null) return false;
        Check.Equal(true,world.Try(defense),"overnight survivor uses normal movement or defense against visible danger"); Round(world); return true;
    }
    static void StepToward(ScenarioWorld world, Point goal, int range)
    {
        Actor player = world.Game.Player;
        if (player.IsSleeping) { Round(world); return; }
        if (Safety(world)) return;
        if (Eat(world)) return;
        var path = CommunityClarityScenario.TryWalkingPath(world, goal, range);
        if (path == null || path.Count == 0) { world.Try(new ActionWait(player, world.Game)); Round(world); return; }
        Point next = path[0];
        Check.Equal(true, world.Bump(player, Direction.FromVector(next.X - player.Location.Position.X,
            next.Y - player.Location.Position.Y)), "fresh travel uses ordinary movement and doors");
        Round(world);
    }
    static void Walk(ScenarioWorld world, Point destination, int range)
    {
        for (int attempts = 0; attempts < 250 && world.Game.Rules.GridDistance(world.Game.Player.Location.Position, destination) > range; attempts++)
            StepToward(world, destination, range);
        Check.Equal(true, world.Game.Rules.GridDistance(world.Game.Player.Location.Position, destination) <= range, "real travel reaches destination");
    }
    static void Conversation(ScenarioWorld world, Actor leader, Keys menu, Keys? reply = null)
    {
        Actor player = world.Game.Player; var ui = (ScenarioUI)world.Game.UI;
        int neighbors = world.Map.Actors.Count(a => a != player && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent &&
            !world.Game.Rules.AreEnemies(player, a) && world.Game.Rules.GridDistance(player.Location.Position, a.Location.Position) <= 1);
        if (neighbors > 1) ui.QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position, leader.Location.Position));
        ui.QueueWaitKey(menu); if (reply.HasValue) ui.QueueWaitKey(reply.Value);
        Check.Equal(true, Check.Call(world.Game, "HandlePlayerTalk", player), "generated encounter uses actual V input");
        Round(world);
    }
    public static void Register()
    {
        ScenarioRunner.Add("world/community-membership-overnight", () => TownScenarioFactory.Create(TrialSeed, false), initial =>
        {
            Actor leader = CommunityNewWorldScenario.Generate(initial);
            var world = new ScenarioWorld(TrialSeed, initial.Game.Player.Location.Map, initial.Game);
            Actor player = world.Game.Player; Guid communityId = leader.SocialGroup.Identity;
            string name = leader.SocialGroup.CommunityName; Point departure = player.Location.Position;
            rounds = sleepingRounds = watchedRounds = 0;
            roundTimes.Clear();
            Console.WriteLine("World membership trial starts at " + world.Map.LocalTime.TurnCounter + "; " + name +
                "; leader food=" + leader.FoodPoints + "; existing player food units=" + NpcIntentSupport.FoodUnits(player));
            // Gather existing unclaimed provisions for both survival and aid; no spawned gifts or boosted stats.
            for (int trip = 0; trip < 6 && NpcIntentSupport.FoodUnits(player) < 6; trip++)
            {
                Point source = Point.Empty; ItemFood supplies = null; int nearest = Int32.MaxValue;
                for (int x = 0; x < world.Map.Width; x++) for (int y = 0; y < world.Map.Height; y++)
                {
                    Point p = new Point(x, y); Inventory stock = world.Map.GetItemsAt(p);
                    if (stock == null || world.Map.XpdBaseAt(p) != null) continue;
                    foreach (ItemFood food in stock.Items.OfType<ItemFood>())
                        if (!world.Game.Rules.IsFoodSpoiled(food, world.Map.LocalTime.TurnCounter) && world.Game.Rules.GridDistance(player.Location.Position, p) < nearest &&
                            CommunityClarityScenario.TryWalkingPath(world, p, 1) != null)
                        { source = p; supplies = food; nearest = world.Game.Rules.GridDistance(player.Location.Position, p); }
                }
                Check.Equal(true, supplies != null, "generated world has ordinary unclaimed provisions");
                Walk(world, source, 1);
                Inventory remaining = world.Map.GetItemsAt(source);
                if (remaining == null || !remaining.Contains(supplies)) continue;
                Check.Equal(true, world.Try(new ActionTakeItem(player, world.Game, source, supplies)), "collect existing food"); Round(world);
            }
            // Wait for a genuine need, following the moving leader with ordinary travel/survival turns.
            string reason;
            for (int attempts = 0; attempts < 1400 && !CommunityMembershipSystem.CanJoin(world.Game, player, leader, out reason); attempts++)
            {
                if (leader.IsDead || leader.SocialGroup == null || leader.HasLeader)
                {
                    Console.WriteLine("World trial lost leader " + leader.UnmodifiedName + " at turn " + world.Map.LocalTime.TurnCounter + "; seeking a surviving independent home.");
                    leader = world.Map.Actors.Where(a => !a.IsDead && CommunityMembershipSystem.IsLeader(world.Game,a) &&
                        a.SocialGroup.Home != null && world.Game.ResolveCommunityHome(a) != null)
                        .OrderByDescending(a => PersonalitySystem.Bias(a,DecisionKind.Group) + PersonalitySystem.Bias(a,DecisionKind.Compassion)/2).FirstOrDefault();
                    Check.Equal(true,leader!=null,"a naturally surviving claimed community remains for the overnight encounter");
                }
                Check.Same(world.Map, leader.Location.Map, "trial community remains on encounter map");
                if (player.IsSleeping) { Round(world); continue; }
                if (Safety(world)) continue;
                Actor beneficiary = leader.PartyTree().FirstOrDefault(a => !a.IsDead && !a.IsSleeping && world.Game.Rules.IsActorHungry(a) &&
                    NpcKnowledgeSystem.Visible(world.Game, leader, a.Location) && CommunityClarityScenario.TryWalkingPath(world, a.Location.Position, 1) != null) ?? leader;
                if (world.Game.Rules.GridDistance(player.Location.Position, beneficiary.Location.Position) > 1)
                { StepToward(world, beneficiary.Location.Position, 1); continue; }
                ItemFood gift = player.Inventory.Items.OfType<ItemFood>().FirstOrDefault(f => !world.Game.Rules.IsFoodSpoiled(f, world.Map.LocalTime.TurnCounter));
                if (!beneficiary.IsSleeping && world.Game.Rules.IsActorHungry(beneficiary) && gift != null)
                {
                    int slot = 0; while (player.Inventory[slot] != gift) slot++;
                    ((ScenarioUI)world.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position, beneficiary.Location.Position));
                    Check.Equal(true, Check.Call(world.Game, "HandlePlayerGiveItem", player, CommunityClarityScenario.InventorySlot(world, slot)), "G gives real useful aid in generated world");
                    Console.WriteLine("World trial useful aid at turn " + world.Map.LocalTime.TurnCounter + ": " + beneficiary.UnmodifiedName + " food=" + beneficiary.FoodPoints);
                    Round(world); continue;
                }
                if (Eat(world)) continue;
                if (world.Game.Rules.IsActorSleepy(player) && world.Game.Rules.CanActorSleep(player))
                { world.Try(new ActionSleep(player, world.Game)); Round(world); continue; }
                world.Try(new ActionWait(player, world.Game)); Round(world);
            }
            Check.Equal(true, CommunityMembershipSystem.CanJoin(world.Game, player, leader, out reason), "real aid earns generated-world invitation: " + reason);
            communityId=leader.SocialGroup.Identity; name=leader.SocialGroup.CommunityName;
            Walk(world, leader.Location.Position, 1);
            string homeRule=CommunityCultureSystem.SpokenRules(leader);
            Check.Equal(true, player.Personality.HeardJournal.Any(h => h.Text.Contains("Leave our supplies alone")||homeRule.Length>0&&h.Text.Contains(homeRule)), "full-scheduler encounter communicates its current home rule");
            Conversation(world, leader, Keys.D2, Keys.Y);
            Check.Equal(communityId, player.CommunityMembership.CommunityId, "actual V/2/Y joins generated community");
            ResolvedCommunityHome home = world.Game.ResolveCommunityHome(player);
            Point homePoint = home.Claim.Cells.Where(p => world.Map.IsWalkable(p.X, p.Y) && world.Map.GetActorAt(p) == null)
                .OrderByDescending(p => Direction.COMPASS.Count(d => home.Claim.Contains(p + d))).First();
            Console.WriteLine("World trial departure from " + player.Location.Position + " to " + departure + "; return home target " + homePoint);
            Walk(world, departure, 1); Walk(world, homePoint, 1);
            Check.Equal(true, home.Claim.Contains(player.Location.Position), "returning member reaches the actual claimed shelter");
            int dawn = (world.Map.LocalTime.TurnCounter / WorldTime.TURNS_PER_DAY + 1) * WorldTime.TURNS_PER_DAY + 6 * WorldTime.TURNS_PER_HOUR;
            while (world.Map.LocalTime.TurnCounter < dawn)
            {
                if (player.IsSleeping) { Round(world); continue; }
                if (Safety(world)) continue;
                if (Eat(world)) continue;
                if (world.Game.Rules.CanActorSleep(player)) world.Try(new ActionSleep(player, world.Game));
                else world.Try(new ActionWait(player, world.Game));
                Round(world);
            }
            Check.Equal(false, player.IsDead, "natural overnight stay does not add protection");
            Check.Equal(true, sleepingRounds > 0, "overnight uses real sleeping and recovery");
            Check.Equal(true, watchedRounds > 0, "real community scheduling maintains watch when available");
            Check.Equal(true, player.Personality.FindCommunity(communityId).Notes.Any(n => n.Kind == "shared_food"), "dated real contribution remains known");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "membership-overnight-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            Session saved = BinarySaveStore.Load<Session>(path);
            Check.Equal(communityId, saved.CurrentMap.Actors.First(a => a.IsPlayer).CommunityMembership.CommunityId, "generated overnight membership saves and reloads");
            roundTimes.Sort();
            Console.WriteLine("Full active-map scheduler/survival rounds: median " + roundTimes[roundTimes.Count / 2].ToString("F2") +
                " ms; p95 " + roundTimes[(roundTimes.Count - 1) * 95 / 100].ToString("F2") + " ms; max " + roundTimes.Last().ToString("F2") + " ms (diagnostic, no new budget).");
            Console.WriteLine("Fresh World membership overnight: " + rounds + " full local scheduler/survival turns, " + sleepingRounds +
                " sleeping rounds and " + watchedRounds + " rounds with an assigned watch; joined " + name +
                ", travelled away and returned, stayed through dawn at turn " + world.Map.LocalTime.TurnCounter +
                ". No resources, protection or need overrides added. This is automated evidence, not human visual review.");
        });
    }
}
