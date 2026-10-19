using System;
using System.Drawing;
using System.IO;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

// Controlled feud and dated shelter reports isolate raids; feud creation has its own combat scenarios.
static class CommunityRaidScenario
{
    static Actor chief, target;
    static ScenarioWorld Setup(int seed)
    {
        var w = CommunityAdaptationScenario.Arena(seed); Actor second;
        var group = CommunityAdaptationScenario.Found(w, out chief, out second);
        var defender = NpcIntentSupport.Actor(w, "Home resident", 4, 5, "brave", "loyal");
        defender.SetCommunityHousehold(group); defender.ResidentCommunity.KnownHome = group.Home.Place.Copy();
        foreach (var actor in group.Tribe.Residents)
        {
            NpcIntentSupport.Food(w, actor, 3); var weapon = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT);
            actor.Inventory.AddAll(weapon); w.Game.DoEquipItem(actor, weapon);
            actor.FoodPoints = w.Game.Rules.ActorMaxFood(actor);
            actor.Personality.Opinion(chief.PersonalityIdentity, chief.UnmodifiedName).Trust = 40;
        }
        target = NpcIntentSupport.Actor(w, "Rival chief", 21, 4, "brave");
        target.AddFollower(NpcIntentSupport.Actor(w, "Rival resident", 22, 4));
        target.SocialGroup.CommunityName = "East refuge";
        var known = chief.Personality.LearnCommunity(target.SocialGroup.Identity, target.SocialGroup.CommunityName, 360, "controlled witness report");
        known.Shelter = "East refuge"; known.ShelterLocation = CommunityShelterLocation.At(target.Location);
        known.ShelterTurn = 360; known.ShelterSource = "reported by a returning resident";
        foreach (var actor in target.PartyTree()) known.Member(new CommunityMember { Id = actor.PersonalityIdentity,
            Name = actor.UnmodifiedName, Status = "seen alive", Turn = 360, Source = "controlled witness report" });
        group.Diplomacy = new CommunityDiplomacyState();
        var feud = group.Diplomacy.Get(PoliticalIdentity.Of(target.SocialGroup)); feud.Feud = true; feud.Grievance = 80; feud.Revision = 1; feud.DeclaredTurn = 360;
        chief.Personality.LearnDiplomacy(CommunityDiplomacySystem.Message(group, feud, 360), 360, "our declaration", 100);
        CommunityMembershipScenario.Fov(w); return w;
    }
    static Actor[] Party() { return chief.Community.Tribe.Residents.Where(a => a.CommunityRaid != null).ToArray(); }
    static void Assemble(ScenarioWorld w)
    {
        chief.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, w.Try(new ActionCommunityRaid(chief, w.Game)), "production raid assembly legal");
        Check.Equal(0, chief.ActionPoints, "briefing costs one normal action");
        Check.Equal(2, Party().Length, "three residents send two volunteers and retain one defender");
    }
    static ActorAction Turn(ScenarioWorld w, Actor actor)
    {
        actor.ActionPoints = Rules.BASE_ACTION_COST; var action = actor.Controller.GetAction(w.Game);
        Check.Equal(true, action != null && w.Try(action), "actual controller performs a legal raid turn"); return action;
    }
    static void Time(ScenarioWorld w, int turn) { CommunityAdaptationScenario.Time(w, turn); }
    static string Save(ScenarioWorld w, string name)
    { string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), name + ".dat"); BinarySaveStore.Save(path, Session.Get); return path; }
    static Map DistantShelter(ScenarioWorld w)
    {
        var away = new Map(w.Seed + 100, "Distant refuge", 30, 12); away.LocalTime.TurnCounter = 9000;
        for (int y = 0; y < 12; y++) for (int x = 0; x < 30; x++) away.SetTileModelAt(x, y, w.Game.GameTiles.FLOOR_ASPHALT);
        w.Map.District.AddUniqueMap(away);
        foreach (var a in target.PartyTree().ToArray()) { w.Map.RemoveActor(a); away.PlaceActorAt(a, new Point(21 + (a == target ? 0 : 1), 4)); }
        w.Map.SetExitAt(new Point(13, 6), new Exit(away, new Point(4, 6)) { IsAnAIExit = true });
        away.SetExitAt(new Point(4, 6), new Exit(w.Map, new Point(13, 6)) { IsAnAIExit = true });
        var known = chief.Personality.FindCommunity(target.SocialGroup.Identity); known.ShelterLocation = CommunityShelterLocation.At(target.Location);
        return away;
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-raid-assembly", () => Setup(5300), w =>
        {
            var people = w.Map.Actors.ToArray(); int food = people.Sum(NpcIntentSupport.FoodUnits);
            ActorAction action = null;
            for (int n = 0; n < 8 && chief.CommunityRaid == null; n++) action = Turn(w, chief);
            Check.Equal(true, chief.CommunityRaid != null, "ordinary AI autonomously assembles the feud raid");
            Check.Equal(true, action is ActionCommunityRaid, "rally is a real selected action");
            Check.Equal(2, Party().Length, "defender remains");
            Check.Equal(people.Length, w.Map.Actors.Count(), "raid creates no people");
            Check.Equal(food, people.Sum(NpcIntentSupport.FoodUnits), "raid creates no provisions");
            Check.Equal(true, Party().All(a => a.Leader == null), "residency grants no command links");
            Check.Equal(true, Party().All(a => a.CommunityRaid != chief.CommunityRaid || a == chief), "volunteers carry independent briefings");
            Check.Equal(true, Party().All(a => w.Game.Rules.IsKnownEnemy(a, target)), "briefing communicates feud and remembered identification");
            Check.Equal(true, chief.Personality.FindCommunity(chief.SocialGroup.Identity).Notes.Any(n => n.Kind == "community_raid_assembled"), "dated raid news is retained");
        });
        ScenarioRunner.Add("npc/community-raid-readiness", () => Setup(5301), w =>
        {
            var action = new ActionCommunityRaid(chief, w.Game);
            chief.FoodPoints = 0; Check.Equal(false, action.IsLegal(), "starving chief cannot rally"); chief.FoodPoints = w.Game.Rules.ActorMaxFood(chief);
            chief.SleepPoints = 0; Check.Equal(false, action.IsLegal(), "sleepy chief cannot rally"); chief.SleepPoints = w.Game.Rules.ActorMaxSleep(chief);
            int hp = chief.HitPoints; chief.HitPoints = 1; Check.Equal(false, action.IsLegal(), "badly hurt chief cannot rally"); chief.HitPoints = hp;
            foreach (var a in chief.Community.Tribe.Residents.Where(a => a != chief)) a.IsSleeping = true;
            Check.Equal(false, action.IsLegal(), "sleeping residents cannot volunteer");
            foreach (var a in chief.Community.Tribe.Residents) a.IsSleeping = false;
            chief.Community.Tribe.Residents.Last().ClearCommunityMember();
            Check.Equal(false, action.IsLegal(), "two-person community retains its home rather than emptying it");
        });
        ScenarioRunner.Add("npc/community-raid-stale-and-neutral", () => Setup(5302), w =>
        {
            var known = chief.Personality.FindCommunity(target.SocialGroup.Identity); var home = known.ShelterLocation;
            known.ShelterLocation = null; Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "hostility alone supplies no target address");
            known.ShelterLocation = home; Time(w, 360 + WorldTime.TURNS_PER_DAY + 1);
            Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "old shelter report needs a fresh report"); Time(w, 360);
            var relation = chief.SocialGroup.Diplomacy.Relations[0]; relation.Feud = false; relation.Trust = -100;
            Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "mere distrust does not authorize a raid");
        });
        ScenarioRunner.Add("npc/community-raid-march-and-combat", () => Setup(5303), w =>
        {
            Assemble(w); Actor[] party = Party(); var defenders = target.PartyTree().ToArray(); var hp = defenders.Select(a => a.HitPoints).ToArray();
            Func<bool> injured = () => defenders.Where((a, i) => a.IsDead || a.HitPoints < hp[i]).Any();
            for (int turn = 361; turn < 510 && !injured(); turn++)
            { Time(w, turn); foreach (var actor in party.Where(a => !a.IsDead)) { var action = Turn(w, actor);
                if (turn < 373 || turn % 10 == 0) Console.WriteLine("Raid turn " + turn + " " + actor.UnmodifiedName + " " + actor.Location.Position + " " + action.GetType().Name +
                    " duty=" + (actor.CommunityRaid == null ? "none" : actor.CommunityRaid.Phase + "/" + actor.CommunityRaid.Reason + "/blocked=" + actor.CommunityRaid.Blocked)); } }
            Check.Equal(true, injured(), "organized residents reach the enemy shelter and inflict real combat injury");
            Check.Equal(true, party.Any(a => a.CommunityRaid != null && a.CommunityRaid.SawDefenders), "raiders identify actual defenders locally");
            Check.Equal(true, chief.Community.Tribe.Residents.Any(a => a.CommunityRaid == null && a.Location.Position.X < 11), "home defender stays behind");
        });
        ScenarioRunner.Add("npc/community-raid-health-and-return", () => Setup(5304), w =>
        {
            Assemble(w); var party = Party();
            for (int turn = 361; turn < 369; turn++) { Time(w, turn); foreach (var actor in party) Turn(w, actor); }
            chief.HitPoints = Math.Max(1, w.Game.Rules.ActorMaxHPs(chief) / 2);
            Time(w, 370); Turn(w, chief);
            Check.Equal(true, chief.CommunityRaid == null || chief.CommunityRaid.Phase == CommunityRaidPhase.Returning, "hurt commander turns back");
            for (int turn = 371; turn < 430 && chief.CommunityRaid != null; turn++)
            { Time(w, turn); foreach (var actor in party) Turn(w, actor); }
            Check.Equal(null, chief.CommunityRaid, "survivor completes the physical return");
            Check.Equal(true, chief.ResidentCommunity.KnownHome.Matches(chief.Location.Map, chief.Location.Position), "return reaches known departure shelter");
            Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "one-day sortie cooldown survives return");
        });
        ScenarioRunner.Add("npc/community-raid-peace-contact", () => Setup(5305), w =>
        {
            Assemble(w); var party = Party();
            foreach (var a in party) w.Map.PlaceActorAt(a, new Point(13 + Array.IndexOf(party, a), 6));
            var relation = chief.SocialGroup.Diplomacy.Relations[0]; relation.Feud = false; relation.LastPeaceTurn = 361; relation.Revision++;
            chief.Personality.LearnDiplomacy(CommunityDiplomacySystem.Message(chief.SocialGroup, relation, 361), 361, "signed peace", 100);
            CommunityRaidSystem.Update(w.Game, chief, new Actor[0]);
            var volunteer = party.First(a => a != chief);
            Check.Equal(CommunityRaidPhase.Outbound, volunteer.CommunityRaid.Phase, "distant shared diplomacy does not telepathically recall volunteers");
            CommunityRaidSystem.Update(w.Game, volunteer, new[] { chief });
            Check.Equal(CommunityRaidPhase.Returning, volunteer.CommunityRaid.Phase, "visible nearby retreat call is actionable");
        });
        ScenarioRunner.Add("npc/community-raid-no-hidden-tracking", () => Setup(5306), w =>
        {
            Assemble(w); var old = chief.CommunityRaid.Objective.Copy();
            w.Map.PlaceActorAt(target, new Point(24, 8)); target.SocialGroup.CommunityName = "Secret new name";
            Check.Equal(old.Anchor, chief.CommunityRaid.Objective.Anchor, "hidden relocation does not redirect the sortie");
            Check.Equal("East refuge", chief.CommunityRaid.TargetName, "briefing is a saved name snapshot");
            chief.Personality.Knowledge.See(Party().First(a => a != chief), 360);
            Time(w, 382); CommunityRaidSystem.Update(w.Game, chief, new Actor[0]);
            Check.Equal(null, chief.CommunityRaid, "lost contact abandons raid while already home");
        });
        ScenarioRunner.Add("npc/community-raid-save-reload", () => Setup(5307), w =>
        {
            Assemble(w); var id = chief.PersonalityIdentity; var targetId = target.PersonalityIdentity;
            var raidId = chief.CommunityRaid.Id; var point = chief.CommunityRaid.Objective.Anchor;
            w = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(Save(w, "community-raid-departure")));
            chief = NpcIntentSupport.Find(w.Map, id); target = NpcIntentSupport.Find(w.Map, targetId);
            Check.Equal(raidId, chief.CommunityRaid.Id, "format5 retains operation identity"); Check.Equal(point, chief.CommunityRaid.Objective.Anchor, "target snapshot persists");
            Check.Equal(2, Party().Length, "volunteers survive reload");
            Check.Equal(false, Object.ReferenceEquals(chief.CommunityRaid, Party().First(a => a != chief).CommunityRaid), "personal briefings remain independent");
            for (int turn = 361; turn < 369; turn++) { Time(w, turn); foreach (var a in Party()) { var action = Turn(w, a);
                Console.WriteLine("Reload raid " + turn + " " + a.UnmodifiedName + " " + a.Location.Position + " " + action.GetType().Name + "/" + (a.CommunityRaid == null ? "none" : a.CommunityRaid.Phase.ToString())); } }
            Check.Equal(true, chief.Location.Position != new Point(4, 4), "real saved controller resumes its march");
        });
        ScenarioRunner.Add("npc/community-raid-refusal-and-orders", () => Setup(5308), w =>
        {
            var residents = chief.Community.Tribe.Residents.Where(a => a != chief).ToArray();
            foreach (var a in residents) a.Personality.Opinion(chief.PersonalityIdentity, chief.UnmodifiedName).Feeling = -100;
            Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "unwilling residents refuse even their chief");
            foreach (var a in residents) { a.Personality.Opinion(chief.PersonalityIdentity, chief.UnmodifiedName).Feeling = 0;
                ((OrderableAI)a.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, a.Location)); }
            Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "existing orders exclude volunteers");
            foreach (var a in residents) ((OrderableAI)a.Controller).SetOrder(null);
            var player = w.Game.Player; player.SetCommunityHousehold(chief.Community);
            var follower = NpcIntentSupport.Actor(w, "Player follower", 6, 4); player.AddFollower(follower);
            var nested = NpcIntentSupport.Actor(w, "Nested follower", 6, 5); follower.AddFollower(nested);
            Assemble(w);
            Check.Equal(null, player.CommunityRaid, "player membership grants no raid authority");
            Check.Equal(null, follower.CommunityRaid, "personal follower stays with player"); Check.Equal(null, nested.CommunityRaid, "nested follower stays with player");
        });
        ScenarioRunner.Add("npc/community-raid-blocked-passage", () => Setup(5309), w =>
        {
            var door = (djack.RogueSurvivor.Engine.MapObjects.DoorWindow)w.Map.GetMapObjectAt(10, 6);
            door.BarricadePoints = 100; Assemble(w); var party = Party();
            for (int turn = 361; turn < 380 && party.Any(a => a.CommunityRaid != null); turn++)
            { Time(w, turn); foreach (var actor in party) Turn(w, actor); }
            Check.Equal(true, party.All(a => a.CommunityRaid == null), "blocked raid is abandoned with finite work");
            Check.Equal(100, door.BarricadePoints, "walking raid does not demolish an impassable barricade");
            Check.Equal(true, chief.Personality.FindCommunity(chief.SocialGroup.Identity).Notes.Any(n => n.Text.Contains("route blocked")), "failure is recorded without victory");
        });
        ScenarioRunner.Add("npc/community-raid-known-exits", () => Setup(5310), w =>
        {
            Map away = DistantShelter(w);
            Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "unknown exit graph cannot launch a distant raid");
            chief.Personality.Knowledge.Exits.Add(new NpcKnownExit(new Location(w.Map, new Point(13, 6)), new Location(away, new Point(4, 6))));
            chief.Personality.Knowledge.Exits.Add(new NpcKnownExit(new Location(away, new Point(4, 6)), new Location(w.Map, new Point(13, 6))));
            Assemble(w); Actor[] party = Party();
            for (int turn = 361; turn < 410 && party.Any(a => a.Location.Map != away); turn++)
            { Time(w, turn); away.LocalTime.TurnCounter++; foreach (var actor in party) Turn(w, actor); }
            Check.Equal(true, party.All(a => a.Location.Map == away), "volunteers use actual known exit actions to reach distant map");
            Check.Equal(true, party.All(a => a.CommunityRaid != null && a.CommunityRaid.Phase == CommunityRaidPhase.Outbound), "different map clocks do not instantly expire mission");
            Check.Equal(true, party.All(a => a.Personality.Knowledge.Exits.Count >= 2), "briefing communicates remembered outbound and return directions");
            Guid chiefId = chief.PersonalityIdentity, targetId = target.PersonalityIdentity;
            int due = chief.CommunityRaid.Deadline;
            string file = Save(w, "community-raid-midjourney");
            Check.Equal(true, Session.Load(file, Session.SaveFormat.FORMAT_BIN), "native session loader reconstructs the world during a raid");
            w = NpcIntentSupport.Restore(w, Session.Get);
            var residents = w.Map.District.Maps.SelectMany(m => m.Actors).ToArray();
            chief = residents.Single(a => a.PersonalityIdentity == chiefId); party = Party(); away = chief.Location.Map;
            target = residents.Single(a => a.PersonalityIdentity == targetId);
            Check.Equal(due, chief.CommunityRaid.Deadline, "midjourney native reload preserves personal mission deadline");
            Check.Equal(true, party.All(a => a.Location.Map == away), "offscreen volunteers remain on the distant map after reload");
            foreach (var a in party) CommunityRaidSystem.Return(w.Game, a, "controlled retreat after real crossing");
            Save(w, "community-raid-returning");
            for (int turn = 411; turn < 460 && party.Any(a => a.Location.Map != w.Map || a.CommunityRaid != null); turn++)
            { Time(w, turn); away.LocalTime.TurnCounter++; foreach (var actor in party) Turn(w, actor); }
            Check.Equal(true, party.All(a => a.Location.Map == w.Map && a.CommunityRaid == null), "actual return exits and home arrival complete distant sortie");
        });
        ScenarioRunner.Add("npc/community-raid-casualty-retreat", () => Setup(5311), w =>
        {
            Assemble(w); var volunteer = Party().First(a => a != chief);
            w.Map.PlaceActorAt(chief, new Point(13, 6)); w.Map.PlaceActorAt(volunteer, new Point(14, 6));
            chief.Personality.Knowledge.See(volunteer, 360);
            CommunityDiplomacyScenario.Kill(w, target, volunteer);
            CommunityRaidSystem.Update(w.Game, chief, new[] { target });
            Check.Equal(CommunityRaidPhase.Returning, chief.CommunityRaid.Phase, "known actual casualty causes surviving raider to retreat");
            var action = Turn(w, chief);
            Check.Equal(false, action is ActionMeleeAttack, "retreat travel takes precedence over a fresh attack");
        });
        ScenarioRunner.Add("npc/community-raid-bounded-news", () => Setup(5312), w =>
        {
            var p = w.Game.Player;
            var known = p.Personality.LearnCommunity(chief.SocialGroup.Identity, chief.SocialGroup.CommunityName, 360, "earlier introduction");
            Assemble(w);
            Check.Equal(false, known.Notes.Any(n => n.Kind == "community_raid_assembled"), "player outside the room does not receive hidden planning");
            w.Map.PlaceActorAt(p, new Point(6, 6)); CommunityMembershipScenario.Fov(w);
            Check.Equal(true, CommunityKnowledgeSystem.Talk(w.Game, chief, p, false), "local community conversation retells raid news");
            var report = known.Notes.Single(n => n.Kind == "community_raid_assembled");
            Check.Equal(true, report.Reported, "journal labels retold planning as reported");
            Check.Equal(360, report.EventTurn, "retelling preserves the original plan date");
            Check.Equal(true, report.Source.Contains(chief.UnmodifiedName), "journal identifies its speaker");
        });
        ScenarioRunner.Add("npc/community-raid-abandoned-target", () => Setup(5313), w =>
        {
            Assemble(w); var party = Party(); foreach (var a in target.PartyTree().ToArray()) w.Map.RemoveActor(a);
            for (int turn = 361; turn < 500 && party.Any(a => a.CommunityRaid != null); turn++)
            { Time(w, turn); foreach (var actor in party) Turn(w, actor); }
            Check.Equal(true, party.All(a => a.CommunityRaid == null), "empty target produces a bounded investigation and return");
            Check.Equal(true, chief.Personality.FindCommunity(chief.SocialGroup.Identity).Notes.Any(n => n.Text.Contains("no identified defenders")), "empty observation is recorded as limited evidence");
        });
        ScenarioRunner.Add("npc/community-raid-genuine-legacy", () => TownScenarioFactory.Create(5314, false), w =>
        {
            var saved = BinarySaveStore.Load<Session>(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_LEGACY_FILE"));
            Check.Equal(true, saved.CurrentMap.CountActors > 0, "genuine old fixture includes actors");
            foreach (var a in saved.CurrentMap.Actors) Check.Equal(null, a.CommunityRaid, "pre-community format5 actors have no invented duty");
        });
        ScenarioRunner.Add("npc/community-raid-both-communities", () => Setup(5315), w =>
        {
            var extra = NpcIntentSupport.Actor(w, "East defender", 22, 5, "brave"); target.AddFollower(extra);
            var group = target.SocialGroup; var cells = new System.Collections.Generic.List<Point>();
            for (int y = 2; y < 10; y++) for (int x = 19; x < 27; x++) cells.Add(new Point(x, y));
            w.Map.AddXpdBase(new XpdBase(target, cells));
            w.Game.InitializeLegacySettlementHome(target);
            foreach (var a in target.PartyTree())
            { NpcIntentSupport.Food(w, a, 3); var weapon = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT); a.Inventory.AddAll(weapon); w.Game.DoEquipItem(a, weapon); }
            group.Diplomacy = new CommunityDiplomacyState(); var feud = group.Diplomacy.Get(PoliticalIdentity.Of(chief.SocialGroup));
            feud.Feud = true; feud.Grievance = 80; feud.DeclaredTurn = 360; feud.Revision = 1;
            target.Personality.LearnDiplomacy(CommunityDiplomacySystem.Message(group, feud, 360), 360, "own declaration", 100);
            var known = target.Personality.LearnCommunity(chief.SocialGroup.Identity, chief.SocialGroup.CommunityName, 360, "returning scout");
            known.ShelterLocation = chief.SocialGroup.Home.Place.Copy(); known.ShelterTurn = 360; known.ShelterSource = "returning scout";
            Assemble(w); target.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, w.Try(new ActionCommunityRaid(target, w.Game)), "rival community independently assembles its own sortie");
            Check.Equal(target.SocialGroup.Identity, chief.CommunityRaid.Target, "west party targets east community");
            Check.Equal(chief.SocialGroup.Identity, target.CommunityRaid.Target, "east party targets west community");
            Check.Equal(false, chief.CommunityRaid.Id == target.CommunityRaid.Id, "competing operations retain separate identities");
            Check.Equal(1, target.PartyTree().Count(a => a.CommunityRaid == null), "rival also retains a resident at home");
        });
        ScenarioRunner.Add("npc/community-raid-entrusted-reserves", () => Setup(5316), w =>
        {
            foreach (var item in chief.Inventory.Items.OfType<ItemFood>()) item.TradeCargo = new TradeCargoTag { Shipment = Guid.NewGuid(), Courier = chief.PersonalityIdentity };
            Check.Equal(false, new ActionCommunityRaid(chief, w.Game).IsLegal(), "entrusted freight cannot supply a community's military provisions");
            Check.Equal(true, chief.Inventory.Items.OfType<ItemFood>().All(f => f.IsEntrustedCargo), "readiness check neither consumes nor releases cargo");
        });
        ScenarioRunner.Add("npc/community-raid-urgent-care", () => Setup(5317), w =>
        {
            Assemble(w); w.Map.PlaceActorAt(chief, new Point(13, 6));
            var medicine = new ItemMedicine(w.Game.GameItems.MEDIKIT); chief.Inventory.AddAll(medicine);
            chief.HitPoints = w.Game.Rules.ActorMaxHPs(chief) / 2; int hp = chief.HitPoints;
            Time(w, 361); Turn(w, chief);
            Check.Equal(true, chief.HitPoints > hp, "wounded retreating survivor uses actual carried medicine before marching");
            Check.Equal(CommunityRaidPhase.Returning, chief.CommunityRaid.Phase, "healing preserves withdrawal duty");
            NpcIntentSupport.Food(w, chief, 2); chief.FoodPoints = 0; int food = NpcIntentSupport.FoodUnits(chief);
            Time(w, 362); Turn(w, chief);
            Check.Equal(true, NpcIntentSupport.FoodUnits(chief) < food, "urgent eating retains priority during withdrawal");
        });
    }
}
