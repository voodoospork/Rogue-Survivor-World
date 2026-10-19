using System;
using System.Collections.Generic;
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

// Controlled fixtures provoke specified events. Generated trials separately retain real survival.
static class CommunityAdaptationScenario
{
    internal static ScenarioWorld Arena(int seed)
    {
        var w = TownScenarioFactory.Arena(seed, Enumerable.Repeat(new string('.', 30), 12).ToArray());
        Session.Get.GamePreset = GamePreset.World(); Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
        RogueGame.KeyBindings.ResetToDefaults();
        Room(w, 1, 1, 10, 10, "First refuge"); Room(w, 18, 1, 10, 10, "Other refuge");
        NpcIntentSupport.Player(w, 14, 5); Time(w, 360); return w;
    }
    static void Room(ScenarioWorld w, int x, int y, int width, int height, string name)
    {
        for (int j = y; j < y + height; j++) for (int i = x; i < x + width; i++)
        { bool edge = i == x || j == y || i == x + width - 1 || j == y + height - 1;
            w.SetTile(i, j, edge ? '#' : '.'); w.Map.GetTileAt(i, j).IsInside = !edge; }
        w.SetTile(x + width - 1, y + height / 2, '.');
        w.Map.PlaceMapObjectAt(new DoorWindow("door", "closed", "open", "broken", 40), new Point(x + width - 1, y + height / 2));
        w.Map.AddZone(new Zone(name, new Rectangle(x, y, width, height)) { BuildingKind = BuildingKind.House });
    }
    internal static void Time(ScenarioWorld w, int turn)
    { w.Map.LocalTime.TurnCounter = turn; Session.Get.WorldTime.TurnCounter = turn; CommunityMembershipScenario.Fov(w); }
    static void Npc(ScenarioWorld w, Actor a)
    { a.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.NpcTurn(a), "actual NPC decision is legal"); }
    internal static SocialGroup Found(ScenarioWorld w, out Actor a, out Actor b)
    {
        a = NpcIntentSupport.Actor(w, "Mira", 4, 4, "kind", "sociable");
        b = NpcIntentSupport.Actor(w, "Sam", 5, 4, "sociable");
        // Useful cooperation is produced by an actual transfer, not an injected fact.
        b.FoodPoints = 100; var food = NpcIntentSupport.Food(w, a, 1);
        w.Game.DoGiveItemTo(a, b, food);
        b.FoodPoints = w.Game.Rules.ActorMaxFood(b); // This controlled fixture isolates social actions.
        CommunityAdaptationSystem.Observe(w.Game, a, new[] { b }); CommunityAdaptationSystem.Observe(w.Game, b, new[] { a });
        // One party's useful aid is known to both participants, including the donor.
        Check.Equal(true, w.Try(new ActionCommunityDecision(a, w.Game, b, "propose")), "normal proposal action");
        Check.Equal(true, w.Try(new ActionCommunityDecision(b, w.Game, a, "answer")), "independent response action");
        Check.Equal(true, a.ResidentCommunity.Proposal.Accepted, "candidate agrees");
        Check.Equal(true, w.Try(new ActionCommunityDecision(a, w.Game, b, "found")), "normal founding claim action");
        Check.Equal(true, a.Community != null, "cooperation establishes a real tribe"); return a.Community;
    }
    static string Save(ScenarioWorld w, string name)
    { string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), name + ".dat"); BinarySaveStore.Save(path, Session.Get); return path; }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-organic-refuge-consent", () => Arena(5200), w =>
        {
            Actor a = NpcIntentSupport.Actor(w, "Mira", 4, 4), b = NpcIntentSupport.Actor(w, "Sam", 5, 4);
            var before = w.Map.Actors.ToArray();
            for (int n = 0; n < 59; n++) { Time(w, 360 + n); Npc(w, a); Npc(w, b); }
            Check.Equal(null, a.Community, "neutral co-location does not grant membership before observed refuge and consent");
            Check.Equal(null, b.Community, "both strangers remain independent");
            for (int n = 59; n < 300 && a.Community == null; n++) { Time(w, 360 + n); Npc(w, a); Npc(w, b); }
            Check.Equal(true, a.Community != null, "actual NPC turns eventually propose, agree and claim after shared refuge");
            Check.Same(a.Community, b.Community, "consenting founders share political identity");
            Check.Equal(null, a.Leader, "chief does not command the other founder"); Check.Equal(null, b.Leader, "resident has no forced command link");
            Check.Equal(0, a.Sheet.SkillTable.GetSkillLevel((int)Skills.IDs.LEADERSHIP), "chief gains no Leadership");
            Check.Equal(0, b.Sheet.SkillTable.GetSkillLevel((int)Skills.IDs.LEADERSHIP), "resident gains no Leadership");
            Check.Equal(before.Length, w.Map.Actors.Count(), "founding conserves actors");
            Check.Equal(0, w.Map.GroundInventories.Sum(i => i.Items.Count()), "founding generates no stock");
            Check.Equal(true, w.Game.ResolveCommunityHome(a) != null, "founding legally secures a home");
            Check.Equal(true, a.Community.Tribe.History.Any(n => n.Kind == "community_founded"), "dated founding evidence is retained");
            Console.WriteLine("Organic refuge formed at local turn " + w.Map.LocalTime.TurnCounter);
        });
        ScenarioRunner.Add("npc/community-organic-aid-pending-save", () => Arena(5201), w =>
        {
            Actor a = NpcIntentSupport.Actor(w, "Mira", 4, 4, "sociable"), b = NpcIntentSupport.Actor(w, "Sam", 5, 4, "sociable");
            b.FoodPoints = 100; var food = NpcIntentSupport.Food(w, a, 1); w.Game.DoGiveItemTo(a, b, food); b.FoodPoints = w.Game.Rules.ActorMaxFood(b);
            for (int n = 0; n < 12 && b.ResidentCommunity.Proposal == null; n++) { Time(w, 360 + n); Npc(w, a); }
            Check.Equal(true, b.ResidentCommunity.Proposal != null, "real NPC turns propose after useful aid and immediate social replies");
            string path = Save(w, "organic-pending-proposal"); Guid aid = a.PersonalityIdentity, bid = b.PersonalityIdentity;
            w = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(path)); a = NpcIntentSupport.Find(w.Map, aid); b = NpcIntentSupport.Find(w.Map, bid);
            Check.Same(a.ResidentCommunity.Proposal, b.ResidentCommunity.Proposal, "pending shared proposal survives format 5");
            for (int n = 0; n < 25 && a.Community == null; n++) { Time(w, 361 + n); Npc(w, b); Npc(w, a); }
            Check.Equal(true, a.Community != null, "NPCs consent and secure the claim after reload");
        });
        ScenarioRunner.Add("npc/community-organic-refusal-priorities", () => Arena(5202), w =>
        {
            Actor a = NpcIntentSupport.Actor(w, "Mira", 4, 4, "sociable"), b = NpcIntentSupport.Actor(w, "Sam", 5, 4, "solitary", "suspicious");
            b.FoodPoints = 100; w.Game.DoGiveItemTo(a, b, NpcIntentSupport.Food(w, a, 1)); b.FoodPoints = w.Game.Rules.ActorMaxFood(b);
            b.Personality.Opinion(a.PersonalityIdentity, a.UnmodifiedName).Feeling = -100;
            Check.Equal(true, w.Try(new ActionCommunityDecision(a, w.Game, b, "propose")), "a proposal does not imply consent");
            Check.Equal(true, w.Try(new ActionCommunityDecision(b, w.Game, a, "answer")), "candidate can refuse in a normal action");
            Check.Equal(null, a.Community, "refusal creates no tribe"); Check.Equal(null, a.ResidentCommunity.Proposal, "refused proposal clears");
            Check.Equal(540, a.ResidentCommunity.Contact(b.PersonalityIdentity).DeclinedUntil, "declined candidate gets 180-turn cooldown");
            b.IsSleeping = true; Check.Equal(false, w.Try(new ActionCommunityDecision(a, w.Game, b, "propose")), "sleeping candidates are unavailable"); b.IsSleeping = false;
            ((OrderableAI)a.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, a.Location));
            Check.Equal(null, CommunityAdaptationSystem.Select(w.Game, a, new[] { b }), "assigned work takes priority");
            ((OrderableAI)a.Controller).SetOrder(null); w.Game.DoMakeAggression(a, b);
            Check.Equal(false, w.Try(new ActionCommunityDecision(a, w.Game, b, "propose")), "hostility prevents founding");
        });
        ScenarioRunner.Add("npc/community-independent-household-access", () => Arena(5203), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            Actor p = w.Game.Player; w.Map.PlaceActorAt(p, new Point(3, 4));
            Actor follower = NpcIntentSupport.Actor(w, "Personal", 3, 3), nested = NpcIntentSupport.Actor(w, "Nested", 2, 3);
            follower.AddFollower(nested); p.AddFollower(follower); SocialGroup original = p.EnsurePersonalGroup();
            var order = new ActorOrder(ActorTasks.GUARD, nested.Location); ((OrderableAI)nested.Controller).SetOrder(order);
            chief.FoodPoints = 100; var aid = NpcIntentSupport.Food(w, p, 1); w.Game.DoGiveItemTo(p, chief, aid);
            p.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionJoinCommunity(p, w.Game, chief)), "earned invitation joins a whole household");
            Check.Equal(null, p.Leader, "chief does not become player's commander"); Check.Same(p, follower.Leader, "personal edge preserved");
            Check.Same(follower, nested.Leader, "nested edge preserved"); Check.Same(order, ((OrderableAI)nested.Controller).Order, "personal work retained");
            Check.Equal(5, group.Tribe.Residents.Count, "household counts every resident"); Check.Same(original, p.CommandGroup, "personal command identity remains intact");
            var home = w.Map.XpdBaseAt(chief.Location.Position); Check.Equal(true, home.Owns(nested), "shared access includes independent nested party");
            Check.Equal(false, home.CanManage(p), "shared access does not grant management");
            var item = new ItemRangedWeapon(w.Game.GameItems.SHOTGUN); w.Map.DropItemAt(item, p.Location.Position);
            Check.Equal(true, w.Try(new ActionTakeItem(p, w.Game, p.Location.Position, item)), "communal pickup is ordinary access");
            Check.Equal(false, w.Game.Rules.AreEnemies(p, chief), "membership pickup creates no theft hostility");
            p.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionLeaveCommunity(p, w.Game)), "leave away from command leader");
            Check.Same(original, p.SocialGroup, "departure restores original political identity"); Check.Same(order, ((OrderableAI)nested.Controller).Order, "departure preserves assignments");
            Check.Equal(2, group.Tribe.Residents.Count, "departure conserves independent roster"); Check.Equal(true, p.Inventory.Contains(item), "carried possessions remain");
        });
        ScenarioRunner.Add("npc/community-legacy-independent-upgrade", () => CommunityMembershipScenario.Arena(5204), w =>
        {
            Actor chief = CommunityMembershipScenario.Group(w); var members = chief.PartyTree().ToArray(); SocialGroup group = chief.SocialGroup;
            var order = new ActorOrder(ActorTasks.GUARD, members[1].Location); ((OrderableAI)members[1].Controller).SetOrder(order);
            Guid identity = group.Identity; string name = group.CommunityName;
            Session.Get.GamePreset = GamePreset.World(); CommunityAdaptationSystem.Observe(w.Game, chief, members);
            Check.Equal(true, group.Tribe != null, "ordinary processing upgrades the settled community once");
            Check.Equal(3, group.Tribe.Residents.Count, "legacy migration conserves residents"); Check.Equal(0, chief.CountFollowers, "legacy civic command edges released");
            Check.Same(order, ((OrderableAI)members[1].Controller).Order, "legacy explicit work remains intact");
            Check.Equal(identity, chief.Community.Identity, "stable identity preserved"); Check.Equal(name, chief.Community.CommunityName, "name preserved");
            Check.Equal(true, w.Map.XpdBaseAt(chief.Location.Position).Owns(members[1]), "old claim now serves independent residents");
            CommunityAdaptationSystem.Observe(w.Game, chief, members); Check.Equal(3, group.Tribe.Residents.Count, "upgrade is idempotent");
        });
        ScenarioRunner.Add("npc/community-food-space-knowledge", () => Arena(5205), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            Actor candidate = NpcIntentSupport.Actor(w, "Guest", 3, 4); int empty = CommunityAdaptationSystem.FoodShortfall(w.Game, chief, candidate);
            NpcIntentSupport.Food(w, candidate, 6); Check.Equal(empty, CommunityAdaptationSystem.FoodShortfall(w.Game, chief, candidate), "unreported private food is not communal stock");
            candidate.ResidentCommunity.ReportedFood = 5000; candidate.ResidentCommunity.FoodReportTurn = 360; candidate.ResidentCommunity.FoodReportedTo = Guid.NewGuid();
            Check.Equal(empty, CommunityAdaptationSystem.FoodShortfall(w.Game, chief, candidate), "report to a different chief cannot be read");
            candidate.ResidentCommunity.FoodReportedTo = chief.PersonalityIdentity; candidate.ResidentCommunity.ReportedDemand = 1440;
            Check.Equal(true, CommunityAdaptationSystem.FoodShortfall(w.Game, chief, candidate) < empty, "reported private supplies offset only their owner's demand");
            Time(w, 550); Check.Equal(true, CommunityAdaptationSystem.FoodShortfall(w.Game, chief, candidate) >= empty, "old provisions reports cannot be counted as fresh stock");
            var home = w.Map.XpdBaseAt(chief.Location.Position); int open = CommunityAdaptationSystem.SleepingSpace(w.Game, chief, w.Map, home.Cells);
            home.SetFoodRoom(new Rectangle(2, 2, 8, 8)); Check.Equal(0, CommunityAdaptationSystem.SleepingSpace(w.Game, chief, w.Map, home.Cells), "storage positions cannot be counted as floor beds");
            string reason; Check.Equal(false, CommunityAdaptationSystem.Admission(w.Game, chief, candidate, out reason), "physical space is a firm veto");
            Check.Equal(true, open >= 6, "legal floor sleep provides space without generating beds");
        });
        ScenarioRunner.Add("npc/community-chief-remnant-succession", () => Arena(5206), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident); Guid id = group.Identity;
            var root = w.Map.XpdBaseAt(chief.Location.Position); w.Map.AddXpdBase(new XpdBase(chief, new[] { new Point(12, 3) }, root));
            group.Diplomacy = new CommunityDiplomacyState();
            var relation = group.Diplomacy.Get(new PoliticalIdentity { Group = Guid.NewGuid(), Faction = -1, Name = "Other refuge" }); relation.Feud = true;
            w.Game.KillActor(null, chief, "controlled succession trial", false);
            Check.Same(resident, resident.CommunityChief, "willing independent resident succeeds without follower capacity");
            Check.Equal(id, resident.Community.Identity, "identity survives founder death"); Check.Equal(true, relation.Feud, "feud state survives");
            Check.Same(resident, root.GroupLeader, "root claim transfers"); Check.Same(resident, w.Map.XpdBaseAt(new Point(12, 3)).GroupLeader, "linked claim transfers");
            Check.Equal(1, group.Tribe.Residents.Count, "a committed lone remnant endures"); Check.Equal(false, group.Tribe.Dissolved, "one survivor is not extinction");
            w.Game.KillActor(null, resident, "controlled final death", false);
            Check.Equal(true, group.Tribe.Dissolved, "final committed death dissolves tribe"); Check.Equal(null, w.Map.XpdBaseAt(new Point(4, 4)), "dissolution releases home");
        });
        ScenarioRunner.Add("npc/community-chief-vacancy-not-extinction", () => Arena(5207), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident); var claim = w.Map.XpdBaseAt(chief.Location.Position);
            resident.IsSleeping = true; w.Game.KillActor(null, chief, "controlled vacancy", false);
            Check.Equal(Guid.Empty, group.LeaderId, "sleeping survivor leaves a chief vacancy"); Check.Equal(false, group.Tribe.Dissolved, "vacancy is not breakup");
            Check.Same(claim, w.Map.XpdBaseAt(new Point(4, 4)), "vacancy retains committed home");
            resident.IsSleeping = false; Npc(w, resident); Check.Same(resident, resident.CommunityChief, "awake remnant can recover through NPC action");
        });
        ScenarioRunner.Add("npc/community-real-relocation", () => Arena(5208), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident); var old = w.Map.XpdBaseAt(chief.Location.Position);
            var stock = new ItemFood(w.Game.GameItems.CANNED_FOOD); w.Map.DropItemAt(stock, new Point(2, 2));
            chief.Personality.Knowledge.RememberPlace(new NpcKnownPlace(new Location(w.Map, new Point(20, 5)), "shelter", 360));
            Check.Equal(true, w.Try(new ActionCommunityDecision(chief, w.Game, null, "relocate")), "chief communicates a known alternative");
            Check.Equal(true, resident.ResidentCommunity.ProposedMove != null, "actual awake listener receives a proposal and decides independently");
            string path = Save(w, "organic-relocation"); Check.Equal(true, BinarySaveStore.Load<Session>(path).CurrentMap.Actors.Any(a => a.Community != null && a.Community.Tribe.Relocation != null), "relocation proposal survives save");
            for (int n = 0; n < 180 && group.Tribe.Relocation != null; n++) { Time(w, 361 + n);
                chief.ActionPoints = Rules.BASE_ACTION_COST; var action = chief.Controller.GetAction(w.Game);
                if (n % 20 == 0 || action is ActionCommunityDecision) Console.WriteLine("Relocation " + n + " chief=" + chief.Location.Position +
                    " action=" + action.GetType().Name + (action is ActionCommunityDecision ? "/" + ((ActionCommunityDecision)action).Kind : "") +
                    " retry=" + chief.ResidentCommunity.RelocationRetryTurn + " resident=" + resident.Location.Position);
                Check.Equal(true, w.Try(action), "chief relocation turn legal"); Npc(w, resident); }
            Check.Equal(null, group.Tribe.Relocation, "normal NPC movement reaches and legally claims replacement");
            Check.Equal(true, chief.Location.Position.X >= 19, "chief physically travels through doors");
            Check.Equal(false, w.Map.XpdBases.Contains(old), "old home released after replacement secured");
            int foodUnits = w.Map.GroundInventories.Sum(i => i.Items.OfType<ItemFood>().Sum(f => f.Quantity)) +
                w.Map.Actors.Sum(a => NpcIntentSupport.FoodUnits(a));
            Check.Equal(2, foodUnits, "relocation conserves existing ground and carried food; ordinary pickups may carry stock");
        });
        ScenarioRunner.Add("npc/community-food-eviction-response-window", () => Arena(5209), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            chief.Personality = new PersonalityState(); chief.Personality.AddTrait(new TraitInstance("cruel"));
            w.Map.PlaceActorAt(w.Game.Player, new Point(5, 5));
            w.Game.DoGiveItemTo(resident, w.Game.Player, resident.Inventory.Items.OfType<ItemFood>().First());
            resident.FoodPoints = 100;
            Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, chief, "food_report")), "a real unmet need is reported through a production action");
            group.Tribe.ShortageReviews = 3; group.Tribe.ShortageSince = 300; // Controlled timing setup; evidence comes from a production request.
            Check.Equal(true, w.Try(new ActionCommunityDecision(chief, w.Game, resident, "warning")), "scarcity warning is communicated");
            Check.Equal(360, resident.ResidentCommunity.EvictionWarningTurn, "specific household actually receives warning");
            Check.Equal(false, w.Try(new ActionCommunityDecision(chief, w.Game, resident, "evict")), "premature eviction is an illegal action");
            Check.Same(group, resident.Community, "no premature eviction within 180-turn response window");
            Time(w, 540); Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, chief, "food_report")), "unmet hunger remains real after the response window");
            chief.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionCommunityDecision(chief, w.Game, resident, "evict")), "communicated scarcity decision after response window");
            Check.Equal(null, resident.Community, "whole target household departs"); Check.Equal(false, w.Game.Rules.AreEnemies(chief, resident), "eviction creates resentment without automatic violence");
            Check.Equal(true, resident.Personality.Person(chief.PersonalityIdentity).Grievance > 0, "resentment recorded"); Check.Equal(0, chief.ActionPoints, "eviction notice charges exactly once");
        });
        ScenarioRunner.Add("npc/community-independent-npc-admission", () => Arena(5220), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            Actor guest = NpcIntentSupport.Actor(w, "Guest", 3, 4, "sociable"), child = NpcIntentSupport.Actor(w, "Guest's companion", 3, 3);
            guest.AddFollower(child); var order = new ActorOrder(ActorTasks.GUARD, child.Location); ((OrderableAI)child.Controller).SetOrder(order);
            for (int n = 0; n < 90 && guest.Community == null; n++)
            { Time(w, 361 + n); chief.ActionPoints = Rules.BASE_ACTION_COST; ActorAction a = chief.Controller.GetAction(w.Game); Check.Equal(true, w.Try(a), "chief executes real admission turn");
                guest.ActionPoints = Rules.BASE_ACTION_COST; ActorAction b = guest.Controller.GetAction(w.Game); Check.Equal(true, w.Try(b), "guest executes independent response turn");
                if (n % 15 == 0) Console.WriteLine("Admission round " + n + ": " + a.GetType().Name + "/" + b.GetType().Name +
                    "; proposal=" + (guest.ResidentCommunity.Proposal == null ? "none" : guest.ResidentCommunity.Proposal.Answered + "/" + guest.ResidentCommunity.Proposal.Accepted)); }
            Check.Same(group, guest.Community, "chief and candidate perform actual NPC proposal, response and admission");
            Check.Same(group, child.Community, "every household member is admitted"); Check.Equal(4, group.Tribe.Residents.Count, "all household demand is counted");
            Check.Same(guest, child.Leader, "admission keeps independent command edge"); Check.Same(order, ((OrderableAI)child.Controller).Order, "household assignment retained");
            Check.Equal(0, chief.CountFollowers, "chief does not acquire admitted residents as followers");
        });
        ScenarioRunner.Add("npc/community-admission-personality-and-food", () => Arena(5221), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident); Actor guest = NpcIntentSupport.Actor(w, "Guest", 3, 4);
            string reason; Check.Equal(true, CommunityAdaptationSystem.Admission(w.Game, chief, guest, out reason), "compassion can justify admission despite a real two-day shortfall");
            chief.Personality = new PersonalityState(); chief.Personality.AddTrait(new TraitInstance("selfish"));
            Check.Equal(false, CommunityAdaptationSystem.Admission(w.Game, chief, guest, out reason), "cautious personality refuses the same food risk");
            var fresh = new ItemFood(w.Game.GameItems.GROCERIES, 360 + WorldTime.TURNS_PER_DAY); w.Map.DropItemAt(fresh, new Point(4, 3));
            CommunityAdaptationSystem.Observe(w.Game, chief, new[] { resident, guest });
            Check.Equal(w.Game.Rules.FoodItemNutrition(fresh, 360 + 2 * WorldTime.TURNS_PER_DAY), group.Tribe.Food.First(f => f.Place.Anchor == new Point(4, 3)).Nutrition,
                "existing decay calculation discounts expired food at the two-day horizon");
            var spoiled = new ItemFood(w.Game.GameItems.GROCERIES, 400); w.Map.DropItemAt(spoiled, new Point(4, 2));
            CommunityAdaptationSystem.Observe(w.Game, chief, new[] { resident, guest });
            Check.Equal(0, group.Tribe.Food.Where(f => f.Place.Anchor == new Point(4, 2)).Sum(f => f.Nutrition), "food spoiled by the two-day horizon is not counted as usable provisions");
        });
        ScenarioRunner.Add("npc/community-legacy-player-household-upgrade", () => CommunityMembershipScenario.Arena(5222), w =>
        {
            Actor chief = CommunityMembershipScenario.Group(w), player = NpcIntentSupport.Player(w, 3, 2);
            Actor follower = NpcIntentSupport.Actor(w, "Personal", 2, 2), nested = NpcIntentSupport.Actor(w, "Nested", 2, 3);
            follower.AddFollower(nested); player.AddFollower(follower); var original = player.SocialGroup;
            var order = new ActorOrder(ActorTasks.GUARD, nested.Location); ((OrderableAI)nested.Controller).SetOrder(order);
            chief.FoodPoints = 100; w.Game.DoGiveItemTo(player, chief, NpcIntentSupport.Food(w, player, 1));
            Check.Equal(true, w.Try(new ActionJoinCommunity(player, w.Game, chief)), "legacy membership established through production action");
            var group = chief.SocialGroup; Guid identity = group.Identity;
            string save = Save(w, "legacy-membership-before-tribe-upgrade");
            Session.Get.GamePreset = GamePreset.World(); CommunityAdaptationSystem.Observe(w.Game, chief, chief.PartyTree().ToArray());
            Check.Equal(null, player.Leader, "upgrade removes only player-to-chief link"); Check.Same(player, follower.Leader, "personal first edge remains");
            Check.Same(follower, nested.Leader, "nested edge remains"); Check.Same(order, ((OrderableAI)nested.Controller).Order, "nested order remains");
            Check.Same(original, player.CommandGroup, "saved original personal identity is restored as command identity");
            Check.Equal(identity, player.Community.Identity, "upgrade retains political identity"); Check.Equal(6, group.Tribe.Residents.Count, "upgrade has no duplicate or missing household residents");
            string upgraded = Save(w, "legacy-membership-after-tribe-upgrade"); Session loaded = BinarySaveStore.Load<Session>(upgraded);
            Actor savedPlayer = loaded.CurrentMap.Actors.First(a => a.IsPlayer);
            Check.Equal(null, savedPlayer.Leader, "independent membership survives reload"); Check.Equal(identity, savedPlayer.Community.Identity, "saved affiliation survives");
            Check.Equal(original.Identity, savedPlayer.CommandGroup.Identity, "command namespace survives alongside tribe namespace");
        });
        ScenarioRunner.Add("npc/community-player-household-scarcity-eviction", () => Arena(5223), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident); Actor p = w.Game.Player;
            w.Map.PlaceActorAt(p, new Point(3, 4)); Actor follower = NpcIntentSupport.Actor(w, "Personal", 3, 3); p.AddFollower(follower);
            var original = p.SocialGroup; var order = new ActorOrder(ActorTasks.GUARD, follower.Location); ((OrderableAI)follower.Controller).SetOrder(order);
            chief.FoodPoints = 100; w.Game.DoGiveItemTo(p, chief, NpcIntentSupport.Food(w, p, 1)); Check.Equal(true, w.Try(new ActionJoinCommunity(p, w.Game, chief)), "player earns and accepts membership");
            chief.Personality = new PersonalityState(); chief.Personality.AddTrait(new TraitInstance("cruel"));
            chief.Personality.Opinion(p.PersonalityIdentity, p.UnmodifiedName).Feeling = -100;
            w.Map.PlaceActorAt(p, new Point(5, 5)); w.Game.DoGiveItemTo(resident, p, resident.Inventory.Items.OfType<ItemFood>().First());
            resident.FoodPoints = 100; Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, chief, "food_report")), "resident reports actual unmet hunger");
            group.Tribe.ShortageReviews = 3; group.Tribe.ShortageSince = 300;
            Check.Equal(true, w.Try(new ActionCommunityDecision(chief, w.Game, p, "warning")), "player household receives actual notice");
            Time(w, 540); Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, chief, "food_report")), "production food request refreshes real shortage evidence");
            Check.Equal(true, w.Try(new ActionCommunityDecision(chief, w.Game, p, "evict")), "player receives no special scarcity protection");
            Check.Equal(null, p.CommunityMembership, "membership ends consistently"); Check.Same(original, p.SocialGroup, "personal identity returns");
            Check.Same(p, follower.Leader, "party remains together"); Check.Same(order, ((OrderableAI)follower.Controller).Order, "personal order survives eviction");
            Check.Equal(false, w.Game.Rules.AreEnemies(chief, p), "notice does not create automatic enemies");
            Check.Equal(true, p.Personality.FindCommunity(group.Identity).Notes.Any(n => n.Kind == "community_departure" && n.Text.Contains("Scarcity")), "dated eviction participation recorded");
        });
        ScenarioRunner.Add("npc/community-blocked-relocation-recovery", () => Arena(5225), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident); var original = w.Map.XpdBaseAt(chief.Location.Position);
            chief.Personality.Knowledge.RememberPlace(new NpcKnownPlace(new Location(w.Map, new Point(20, 5)), "shelter", 360));
            Check.Equal(true, w.Try(new ActionCommunityDecision(chief, w.Game, null, "relocate")), "actual relocation proposal");
            for (int y = 0; y < w.Map.Height; y++) w.SetTile(15, y, '#');
            Check.Call(chief.Controller, "BehaviorCommunityRelocation", w.Game);
            Check.Equal(390, chief.ResidentCommunity.RelocationRetryTurn, "blocked route retries after thirty local turns");
            Check.Same(original, w.Map.XpdBaseAt(new Point(4, 4)), "unreachable proposal keeps former claim");
            string saved = Save(w, "organic-blocked-move"); var loaded = BinarySaveStore.Load<Session>(saved);
            Check.Equal(390, loaded.CurrentMap.Actors.First(a => a.PersonalityIdentity == chief.PersonalityIdentity).ResidentCommunity.RelocationRetryTurn, "blocked retry persists");
            w.SetTile(15, 6, '.'); Time(w, 390);
            for (int n = 0; n < 140 && group.Tribe.Relocation != null; n++) { Time(w, 390 + n); Npc(w, chief); Npc(w, resident); }
            Check.Equal(null, group.Tribe.Relocation, "reopened legal detour completes ordinary travel and claim");
            Check.Equal(false, w.Map.XpdBases.Contains(original), "only successful arrival releases old home");
        });
        ScenarioRunner.Add("npc/community-refused-move-splinter", () => Arena(5226), w =>
        {
            Actor chief, resident; SocialGroup parent = Found(w, out chief, out resident);
            parent.Diplomacy = new CommunityDiplomacyState(); var rival = new PoliticalIdentity { Group = Guid.NewGuid(), Name = "Old rival", Faction = -1 };
            parent.Diplomacy.Get(rival).Feud = true;
            resident.Personality.Opinion(chief.PersonalityIdentity, chief.UnmodifiedName).Feeling = -100;
            chief.Personality.Knowledge.RememberPlace(new NpcKnownPlace(new Location(w.Map, new Point(20, 5)), "shelter", 360));
            w.Try(new ActionCommunityDecision(chief, w.Game, null, "relocate"));
            Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, chief, "move_answer")), "resident independently refuses through production action");
            Check.Equal(null, resident.ResidentCommunity.TravelHome, "refusal creates no forced route");
            Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, null, "depart")), "refuser communicates departure");
            Check.Equal(null, resident.Community, "leaving is not itself a new tribe"); Check.Equal(false, parent.Tribe.Dissolved, "committed chief keeps parent identity");
            for (int n = 0; n < 140 && parent.Tribe.Relocation != null; n++) { Time(w, 361 + n); Npc(w, chief); }
            Check.Equal(null, parent.Tribe.Relocation, "chief leaves physically and relinquishes former claim");
            Actor guest = NpcIntentSupport.Actor(w, "New neighbor", 6, 4, "sociable"); guest.FoodPoints = 100;
            w.Game.DoGiveItemTo(resident, guest, resident.Inventory.Items.OfType<ItemFood>().First()); guest.FoodPoints = w.Game.Rules.ActorMaxFood(guest);
            CommunityAdaptationSystem.Observe(w.Game, resident, new[] { guest }); CommunityAdaptationSystem.Observe(w.Game, guest, new[] { resident });
            Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, guest, "propose")), "stayer proposes a separate founding");
            w.Try(new ActionCommunityDecision(guest, w.Game, resident, "answer"));
            Check.Equal(true, w.Try(new ActionCommunityDecision(resident, w.Game, guest, "found")), "splinter must secure its own legal home");
            Check.Equal(true, resident.Community.Identity != parent.Identity, "splinter gets a new persistent identity");
            Check.Equal(true, resident.Community.Diplomacy == null || resident.Community.Diplomacy.Relations.All(r => !r.Feud), "parent feuds are not copied");
        });
        ScenarioRunner.Add("npc/community-known-exit-local-clocks", () => Arena(5227), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            var floorWorld = new ScenarioWorld(w.Seed + 100, Enumerable.Repeat(new string('.', 30), 12).ToArray()); Map floor = floorWorld.Map;
            w.Map.District.AddUniqueMap(floor); Room(floorWorld, 18, 1, 10, 10, "Upstairs refuge"); floor.LocalTime.TurnCounter = 50;
            w.Map.SetExitAt(new Point(4, 3), new Exit(floor, new Point(20, 4)) { IsAnAIExit = true });
            floor.SetExitAt(new Point(20, 4), new Exit(w.Map, new Point(4, 3)) { IsAnAIExit = true });
            chief.Personality.Knowledge.RememberPlace(new NpcKnownPlace(new Location(floor, new Point(20, 5)), "shelter", 360));
            w.Try(new ActionCommunityDecision(chief, w.Game, null, "relocate"));
            Check.Equal(null, Check.Call(chief.Controller, "BehaviorCommunityRelocation", w.Game), "unseen exits cannot silently provide a route");
            chief.ResidentCommunity.RelocationRetryTurn = 0;
            for (int n = 0; n < 70 && group.Tribe.Relocation != null; n++)
            { Time(w, 361 + n); floor.LocalTime.TurnCounter = 51 + n; Npc(w, chief); }
            Check.Same(floor, chief.Location.Map, "ordinary observation and exit actions reach the known alternative");
            Check.Equal(null, group.Tribe.Relocation, "replacement secured after actual intermap crossing");
            Check.Equal(true, CommunityAdaptationSystem.Turn(chief) >= 360, "a lagging map clock cannot reset local decision time");
            Check.Same(group, resident.Community, "absent resident is not dismissed or presumed extinct");
        });
        ScenarioRunner.Add("npc/community-independent-watch-survival", () => Arena(5228), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            chief.Personality = new PersonalityState(); resident.Personality = new PersonalityState(); // Isolate available watch from pending aid tasks.
            w.Game.ScheduleCommunityWatch(chief); Guid first = group.Home.Watcher;
            Check.Equal(false, first == Guid.Empty, "independent residents provide a watch without command links");
            Time(w, 360 + WorldTime.TURNS_PER_HOUR); w.Game.ScheduleCommunityWatch(chief);
            Check.Equal(false, first == group.Home.Watcher, "watch rotates among independent residents");
            Actor watcher = group.Tribe.Residents.First(a => a.PersonalityIdentity == group.Home.Watcher);
            Actor sleeper = watcher == chief ? resident : chief;
            w.Map.PlaceActorAt(watcher, new Point(8, 5)); w.Map.PlaceActorAt(sleeper, new Point(7, 5));
            for (int i = 0; i < 3; i++) sleeper.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
            sleeper.IsSleeping = true; Actor threat = NpcIntentSupport.Actor(w, "Observed threat", 7, 2);
            w.Game.DoMakeAggression(threat, watcher);
            watcher.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionCommunityAlarm(watcher, w.Game, threat)), "independent watcher communicates actual threat");
            Check.Equal(false, sleeper.IsSleeping, "existing alarm noise wakes nearby sleeper"); Check.Equal(0, watcher.ActionPoints, "alarm charges once");
            var claim = w.Map.XpdBaseAt(new Point(4, 4)); w.Map.RemoveXpdBase(claim);
            Check.Equal(null, w.Game.ResolveCommunityHome(resident), "claim loss invalidates cached home");
            Check.Equal(false, group.Tribe.Dissolved, "homeless residents retain their committed identity");
        });
        ScenarioRunner.Add("npc/community-voluntary-known-danger-and-trust", () => Arena(5229), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            Actor child = NpcIntentSupport.Actor(w, "Personal companion", 6, 4); resident.AddFollower(child);
            var order = new ActorOrder(ActorTasks.GUARD, child.Location); ((BaseAI)child.Controller).SetOrder(order);
            resident.Personality.Opinion(chief.PersonalityIdentity, chief.UnmodifiedName).Feeling = -100;
            Actor enemy = NpcIntentSupport.Actor(w, "Witnessed danger", 7, 4); w.Game.DoMakeAggression(enemy, resident);
            CommunityAdaptationSystem.Observe(w.Game, resident, new[] { enemy });
            w.Map.PlaceActorAt(enemy, new Point(29, 11));
            var decision = CommunityAdaptationSystem.Select(w.Game, resident, new[] { chief, child });
            Check.Equal(true, decision is ActionCommunityDecision, "personally known danger and loss of trust permit departure without hunger");
            Check.Equal(true, w.Try(decision), "voluntary departure uses a normal action");
            Check.Equal(null, resident.Community, "resident leaves independently"); Check.Equal(null, child.Community, "personal household leaves together");
            Check.Same(resident, child.Leader, "departure keeps personal command edge"); Check.Same(order, ((BaseAI)child.Controller).Order, "departure keeps assignment");
            Check.Equal(false, group.Tribe.Dissolved, "committed chief retains the tribe");
            Check.Equal(false, w.Game.Rules.AreEnemies(chief, resident, false), "departure creates no automatic hostility");
        });
        ScenarioRunner.Add("npc/community-proposal-local-clock-consent", () => Arena(5232), w =>
        {
            Actor a = NpcIntentSupport.Actor(w, "Mira", 4, 4, "kind", "sociable"), b = NpcIntentSupport.Actor(w, "Sam", 5, 4, "sociable");
            b.FoodPoints = 100; w.Game.DoGiveItemTo(a, b, NpcIntentSupport.Food(w, a, 1)); b.FoodPoints = w.Game.Rules.ActorMaxFood(b);
            CommunityAdaptationSystem.Observe(w.Game, a, new[] { b }); CommunityAdaptationSystem.Observe(w.Game, b, new[] { a });
            // Independent local clocks can differ after a visit to an area whose simulation lagged.
            b.ResidentCommunity.LocalClock = 1000;
            Check.Equal(true, w.Try(new ActionCommunityDecision(a, w.Game, b, "propose")), "actual proposal between actors with different local histories");
            var proposal = b.ResidentCommunity.Proposal;
            var pending = BinarySaveStore.Load<Session>(Save(w, "organic-proposal-different-clocks"));
            Check.Equal(1180, pending.CurrentMap.Actors.Single(savedActor => savedActor.PersonalityIdentity == b.PersonalityIdentity).ResidentCommunity.Proposal.CandidateExpiresTurn,
                "the recipient's own response deadline survives format-five persistence");
            ActorAction answer = CommunityAdaptationSystem.Select(w.Game, b, new[] { a });
            Check.Same(proposal, b.ResidentCommunity.Proposal, "newly heard proposal is not already expired on the recipient's clock");
            Check.Equal(true, answer != null && w.Try(answer), "recipient independently answers on their own local clock");
            Check.Equal(true, proposal.Accepted, "useful aid and consent remain valid despite timing differences");
            Check.Equal(true, w.Try(new ActionCommunityDecision(a, w.Game, b, "found")), "agreement completes without moving actors or changing either clock");
        });
        ScenarioRunner.Add("npc/community-private-party-is-not-tribe", () => Arena(5233), w =>
        {
            Actor commander = NpcIntentSupport.Actor(w, "Commander", 4, 4), follower = NpcIntentSupport.Actor(w, "Companion", 5, 4);
            commander.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
            Check.Equal(true, w.Try(new ActionTakeLead(commander, w.Game, follower)), "ordinary command recruitment");
            string reason; Check.Equal(true, w.Game.TryClaimXpdBase(commander, out reason), "party can claim an ordinary private camp");
            w.Game.InitializeCommunityHome(commander);
            Check.Equal(null, commander.SocialGroup.Home, "a fresh private command camp is not a legacy settlement");
            CommunityAdaptationSystem.Observe(w.Game, commander, new[] { follower });
            Check.Equal(null, commander.Community, "claiming does not bypass a founding agreement");
            Check.Equal(null, follower.Community, "being commanded does not automatically grant tribe affiliation");
            Check.Same(commander, follower.Leader, "ordinary party remains a command relationship");
        });
        ScenarioRunner.Add("npc/community-successor-scarcity-warning", () => Arena(5230), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident);
            group.Tribe.WarningTurn = 360; group.Tribe.ShortageReviews = 3; group.Tribe.ShortageSince = 300;
            resident.ResidentCommunity.EvictionWarningFrom = chief.PersonalityIdentity; resident.ResidentCommunity.EvictionWarningTurn = 360;
            group.Tribe.Note(new CommunityNote { Kind = "community_shortage", EventTurn = 360, Text = "Former chief's communicated warning" });
            w.Game.KillActor(null, chief, "controlled succession during a warning", false);
            Check.Same(resident, resident.CommunityChief, "willing resident succeeds during shortage");
            Check.Equal(-1, group.Tribe.WarningTurn, "new chief must issue their own communicated warning");
            Check.Equal(true, group.Tribe.History.Any(n => n.Kind == "community_shortage" && n.EventTurn == 360), "old warning remains dated history");
            Check.Equal(chief.PersonalityIdentity, resident.ResidentCommunity.EvictionWarningFrom, "succession does not invent a newly heard warning");
        });
        ScenarioRunner.Add("npc/community-stale-chief-and-apparent-abandonment", () => Arena(5224), w =>
        {
            Actor chief, resident; SocialGroup group = Found(w, out chief, out resident); Actor player = w.Game.Player;
            w.Map.PlaceActorAt(player, new Point(3, 4)); CommunityKnowledgeSystem.Talk(w.Game, chief, player, false);
            var known = player.Personality.FindCommunity(group.Identity); string name = known.Leader; int date = known.LeaderTurn;
            w.Map.PlaceActorAt(player, new Point(29, 11)); Time(w, 400); w.Game.KillActor(null, chief, "unwitnessed controlled chief death", false);
            Check.Equal(name, known.Leader, "unheard succession keeps chief snapshot dated"); Check.Equal(date, known.LeaderTurn, "journal opening cannot refresh unseen leadership");
            Check.Call(w.Game, "CommunityJournalLines", player); Check.Equal(date, known.LeaderTurn, "journal uses saved knowledge only");
            w.Map.PlaceActorAt(resident, new Point(20, 4)); w.Map.PlaceActorAt(player, new Point(4, 4)); Time(w, 460);
            Check.Equal(true, known.Notes.Any(n => n.Kind == "home_apparently_empty" && n.Text.Contains("survivors may be elsewhere")), "an empty reported home establishes only apparent abandonment");
            Check.Equal(false, group.Tribe.Dissolved, "missing contact is not extinction");
        });
    }
}
