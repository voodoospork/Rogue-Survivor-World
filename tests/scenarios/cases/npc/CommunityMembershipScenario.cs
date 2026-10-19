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
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityMembershipScenario
{
    internal static ScenarioWorld Arena(int seed)
    {
        var world = TownScenarioFactory.Arena(seed, "........................", "........................", "........................",
            "........................", "........................", "........................", "........................", "........................");
        Session.Get.GamePreset = GamePreset.World();
        Session.Get.GamePreset.Name = "Legacy settlement comparison";
        Session.Get.GamePreset.OrganicCommunities = false;
        Session.Get.GamePreset.InitialCommunities = true;
        Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
        RogueGame.KeyBindings.ResetToDefaults();
        return world;
    }
    internal static Actor Group(ScenarioWorld world)
    {
        Actor leader = NpcIntentSupport.Actor(world, "Mira", 4, 2, "loyal", "brave", "lawful");
        leader.AddFollower(NpcIntentSupport.Actor(world, "Sam", 5, 2));
        leader.AddFollower(NpcIntentSupport.Actor(world, "Lee", 5, 3));
        leader.SocialGroup.CommunityName = "Mira's circle";
        leader.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
        leader.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
        var cells = new List<Point>();
        for (int x = 4; x <= 8; x++) for (int y = 1; y <= 5; y++)
        { cells.Add(new Point(x, y)); world.Map.GetTileAt(x, y).IsInside = true; }
        world.Map.AddZone(new Zone("Mira's house", new Rectangle(4, 1, 5, 5)) { BuildingKind = BuildingKind.House });
        world.Map.PlaceMapObjectAt(new DoorWindow("door", "closed", "open", "broken", 40), new Point(8, 3));
        var claim = new XpdBase(leader, cells); claim.SetFoodRoom(new Rectangle(4, 4, 3, 1));
        claim.SetWeaponRoom(new Rectangle(7, 4, 1, 1)); world.Map.AddXpdBase(claim);
        world.Game.InitializeCommunityHome(leader);
        return leader;
    }
    static Actor Party(ScenarioWorld world)
    {
        Actor player = NpcIntentSupport.Player(world, 3, 2);
        Actor sam = NpcIntentSupport.Actor(world, "Personal", 2, 2);
        Actor child = NpcIntentSupport.Actor(world, "Nested", 2, 3);
        sam.AddFollower(child); player.AddFollower(sam);
        ((OrderableAI)sam.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, sam.Location));
        ((OrderableAI)child.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, child.Location));
        Fov(world); return player;
    }
    internal static void Fov(ScenarioWorld world)
    { Check.Call(world.Game, "UpdatePlayerFOV", world.Game.Player); world.Game.ComputeViewRect(world.Game.Player.Location.Position); }
    internal static void Time(ScenarioWorld world, int turn)
    {
        world.Map.LocalTime.TurnCounter = turn; Session.Get.WorldTime.TurnCounter = turn;
        // This driver advances both clocks explicitly; retain that relationship in optional-mode fixtures.
        var mode=typeof(GamePreset).GetField("Simulation");
        if(mode!=null && Convert.ToInt32(mode.GetValue(Session.Get.GamePreset))==2)
            Check.Call(world.Game,"AlignAdaptiveClock",world.Map,turn);
        Fov(world);
    }
    static void Aid(ScenarioWorld world, Actor player, Actor recipient)
    {
        recipient.FoodPoints = 100;
        ItemFood food = NpcIntentSupport.Food(world, player, 1);
        Give(world, player, recipient, food);
    }
    static void Give(ScenarioWorld world, Actor player, Actor recipient, Item food)
    {
        player.ActionPoints = Rules.BASE_ACTION_COST;
        int slot = 0; while (player.Inventory[slot] != food) slot++;
        ((ScenarioUI)world.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position, recipient.Location.Position));
        Check.Equal(true, Check.Call(world.Game, "HandlePlayerGiveItem", player, CommunityClarityScenario.InventorySlot(world, slot)), "real useful food aid through G input is legal");
        Check.Equal(0, player.ActionPoints, "actual aid spends one action");
    }
    static void Join(ScenarioWorld world, Actor player, Actor leader)
    {
        Aid(world, player, leader);
        player.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, world.Try(new ActionJoinCommunity(player, world.Game, leader)), "aid earns a production invitation action");
        Check.Equal(0, player.ActionPoints, "join costs exactly one action");
    }
    static object Talk(ScenarioWorld world, Actor player)
    {
        Actor leader = world.Map.Actors.First(a => a.UnmodifiedName == "Mira");
        int nearby = world.Map.Actors.Count(a => a != player && !a.IsDead && !a.IsSleeping &&
            a.Model.Abilities.IsIntelligent && world.Game.Rules.GridDistance(a.Location.Position, player.Location.Position) <= 1 && !world.Game.Rules.AreEnemies(player, a));
        if (nearby > 1) ((ScenarioUI)world.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(player.Location.Position, leader.Location.Position));
        return Check.Call(world.Game, "HandlePlayerTalk", player);
    }
    static void Conservation(Actor player, Actor leader)
    {
        var physical = leader.PartyTree().Select(a => a.PersonalityIdentity).ToList();
        Check.Equal(physical.Count, physical.Distinct().Count(), "actor appears in physical tree once");
        Check.Equal(physical.Count, leader.SocialGroup.Members.Count, "saved roster conserves physical party");
        Check.Equal(true, physical.All(id => leader.SocialGroup.Members.Contains(id)), "roster contains exactly the full nested group");
        foreach (Actor member in leader.PartyTree()) Check.Same(leader.SocialGroup, member.SocialGroup, "all nested affiliations share identity");
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-membership-party-access", () => Arena(4950), world =>
        {
            Actor player = Party(world), leader = Group(world);
            SocialGroup original = player.SocialGroup;
            var personal = player.PartyTree().ToList();
            ActorOrder order = ((OrderableAI)personal[1].Controller).Order, childOrder = ((OrderableAI)personal[2].Controller).Order;
            var privateClaim = new XpdBase(player, new[] { new Point(1, 1) }); world.Map.AddXpdBase(privateClaim);
            string reason;
            Check.Equal(false, CommunityMembershipSystem.CanJoin(world.Game, player, leader, out reason), "no invitation before known aid");
            Check.Equal(2, world.Game.Rules.ActorMaxFollowers(leader), "NPC starts at ordinary two-person command capacity");
            leader.SocialGroup.Members.Add(Guid.NewGuid()); // Reproduce a stale roster entry in an older group.
            Join(world, player, leader);
            Check.Equal(3, leader.CountFollowers, "one independent player party is admitted beyond command capacity");
            Check.Same(leader, player.Leader, "NPC remains leader");
            Check.Same(order, ((OrderableAI)personal[1].Controller).Order, "personal order preserved on join");
            Check.Same(childOrder, ((OrderableAI)personal[2].Controller).Order, "nested order preserved on join");
            Check.Equal(0, original.Members.Count, "old roster has no stale party entries");
            Conservation(player, leader);
            XpdBase home = world.Map.XpdBaseAt(leader.Location.Position);
            foreach (Actor member in personal) Check.Equal(true, home.Owns(member), "shared access includes nested personal followers");
            Check.Equal(false, home.CanManage(player), "membership cannot manage NPC rooms");
            Check.Equal(false, privateClaim.Owns(leader), "private claim stays personal");
            Check.Equal(true, privateClaim.Owns(personal[2]), "private claim still accepts personal subtree");
            world.Map.PlaceActorAt(player, new Point(7, 2)); Fov(world);
            player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(false, Check.Call(world.Game, "HandlePlayerXpdBase", player), "actual Ctrl+B denies communal storage changes");
            Check.Equal(Rules.BASE_ACTION_COST, player.ActionPoints, "denied management costs no action");
            Item gun = new ItemRangedWeapon(world.Game.GameItems.SHOTGUN); world.Map.DropItemAt(gun, player.Location.Position);
            Check.Equal(true, world.Try(new ActionTakeItem(player, world.Game, player.Location.Position, gun)), "member picks up existing communal gun");
            Check.Equal(false, world.Game.Rules.AreEnemies(player, leader), "shared weapon pickup is not theft");
            ItemFood stock = new ItemFood(world.Game.GameItems.CANNED_FOOD); world.Map.DropItemAt(stock, personal[2].Location.Position);
            personal[2].ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Try(new ActionTakeItem(personal[2], world.Game, personal[2].Location.Position, stock)), "nested follower takes ordinary stock");
            Check.Equal(false, world.Game.Rules.CanActorTakeLead(player, leader.Followers.First(a => !a.IsPlayer), out reason), "cannot poach fellow community members");
            int skill = player.Sheet.SkillTable.GetSkillLevel((int)Skills.IDs.LEADERSHIP);
            var ui = (ScenarioUI)world.Game.UI;
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueKey(Keys.J); ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.L); ui.QueueWaitKey(Keys.Y);
            Check.Call(world.Game, "HandlePlayerActor", player);
            Check.Equal(0, player.ActionPoints, "J departure ends player action and resumes the scheduler");
            Check.Equal(null, player.Leader, "party leaves away from leader");
            Check.Same(original, player.SocialGroup, "original personal group object and identity restored");
            Check.Equal(personal.Count, original.Members.Count, "restored roster contains current party once");
            Check.Same(order, ((OrderableAI)personal[1].Controller).Order, "departure preserves orders");
            Check.Same(childOrder, ((OrderableAI)personal[2].Controller).Order, "departure preserves nested orders");
            Check.Equal(true, player.Inventory.Contains(gun), "departure retains carried supplies");
            Check.Equal(false, world.Game.Rules.AreEnemies(player, leader), "departure creates no aggression");
            Check.Equal(skill, player.Sheet.SkillTable.GetSkillLevel((int)Skills.IDs.LEADERSHIP), "membership provides no farmable skill reward");
            Check.Equal(false, home.Owns(player), "later foreign stock is no longer shared");
            Check.Equal(true, player.Personality.FindCommunity(home.CommunityIdentity).Notes.Any(n => n.Kind == "community_departure"), "dated departure recorded");
            Check.Equal(true, player.Personality.FindCommunity(original.Identity).Members.Any(m => m.Id == player.PersonalityIdentity), "participation updates the restored affiliation");
            string affiliation = String.Join("\n", (string[])Check.Call(world.Game, "DescribeActor", personal[2]));
            Check.Equal(true, affiliation.Contains("Last known community: " + original.CommunityName), "same-turn departure prefers the known restored affiliation");
            ItemFood foreign = new ItemFood(world.Game.GameItems.CANNED_FOOD); world.Map.DropItemAt(foreign, player.Location.Position);
            CommunityCultureSystem.Ensure(world.Game,leader);leader.SocialGroup.Culture.Enforcement=80;
            CommunityCultureSystem.SayPolicy(world.Game,leader,null,true);
            player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Try(new ActionTakeItem(player, world.Game, player.Location.Position, foreign)), "post-departure pickup uses production theft action");
            Check.Equal(true, world.Game.Rules.AreEnemies(player, leader), "later witnessed foreign pickup has normal theft consequences");
        });
        ScenarioRunner.Add("npc/community-membership-input", () => Arena(4951), world =>
        {
            Actor player = Party(world), leader = Group(world); var ui = (ScenarioUI)world.Game.UI;
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.Escape);
            Check.Equal(false, Talk(world, player), "leader menu cancellation is free");
            Check.Equal(Rules.BASE_ACTION_COST, player.ActionPoints, "cancel spends nothing");
            Aid(world, player, leader);
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.D2); ui.QueueWaitKey(Keys.N);
            Check.Equal(true, Talk(world, player), "invitation can be declined");
            Check.Equal(null, player.CommunityMembership, "declining keeps independence");
            Check.Equal(0, player.ActionPoints, "completed refusal costs one action");
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.D2); ui.QueueWaitKey(Keys.Y);
            Check.Equal(true, Talk(world, player), "actual V/2/Y joins party");
            Check.Equal(true, String.Join("\n", ui.DrawnStrings).Contains("communal ground supplies"), "terms shown before acceptance");
            int trust = player.TrustInLeader;
            Aid(world, player, leader);
            Check.Equal(trust, player.TrustInLeader, "voluntary donation has no forced-transfer penalty");
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.D3); ui.QueueWaitKey(Keys.Y);
            Check.Equal(true, Talk(world, player), "actual V/3/Y leaves party");
            Check.Equal(null, player.CommunityMembership, "membership ends consistently");
        });
        ScenarioRunner.Add("npc/community-membership-hostility-and-knowledge", () => Arena(4952), world =>
        {
            Actor player = Party(world), leader = Group(world); string reason;
            Actor beneficiary = leader.Followers.First();
            world.Map.PlaceActorAt(player, new Point(5, 1));
            world.Map.PlaceActorAt(leader, new Point(20, 1));
            Aid(world, player, beneficiary);
            Check.Equal(false, CommunityMembershipSystem.CanJoin(world.Game, player, leader, out reason), "unknown aid cannot earn invitation");
            world.Map.PlaceActorAt(leader, new Point(4, 2));
            NpcFact evidence = beneficiary.Personality.Knowledge.Facts.Last(f => f.Kind == "helped");
            NpcKnowledgeSystem.Hear(world.Game, leader, beneficiary, evidence);
            Check.Equal(true, CommunityMembershipSystem.CanJoin(world.Game, player, leader, out reason), "actual encounter report can teach leader about aid");
            Actor nested = player.PartyTree().Last();
            world.Game.DoMakeAggression(nested, beneficiary);
            Check.Equal(false, world.Game.Rules.AreEnemies(player, leader), "a nested personal dispute does not declare war on the whole community");
            Check.Equal(false, CommunityMembershipSystem.CanJoin(world.Game, player, leader, out reason), "invitation rejects nested personal dispute");
            Check.Equal(true, nested.IsAggressorOf(beneficiary), "membership never clears explicit hostility");
        });
        ScenarioRunner.Add("npc/community-membership-recruit-dismiss", () => Arena(4953), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            player.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
            player.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
            Actor outsider = NpcIntentSupport.Actor(world, "New personal follower", 3, 1);
            player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Try(new ActionTakeLead(player, world.Game, outsider)), "ordinary recruitment still admits outsiders");
            Conservation(player, leader); Check.Same(player, outsider.Leader, "new recruit follows player");
            player.ActionPoints = Rules.BASE_ACTION_COST; world.Game.DoCancelLead(player, outsider);
            Check.Equal(false, leader.SocialGroup.Members.Contains(outsider.PersonalityIdentity), "dismissal removes community affiliation");
            Check.Equal(false, world.Map.XpdBaseAt(leader.Location.Position).Owns(outsider), "dismissed follower loses supply access");
            Check.Equal("left", player.Personality.FindCommunity(leader.SocialGroup.Identity).Members.Single(m => m.Id == outsider.PersonalityIdentity).Status, "actual personal dismissal updates dated affiliation knowledge");
            player.ActionPoints = Rules.BASE_ACTION_COST; world.Try(new ActionLeaveCommunity(player, world.Game));
            Check.Equal(false, player.SocialGroup.Members.Contains(outsider.PersonalityIdentity), "departing party does not restore dismissed roster entry");
        });
        ScenarioRunner.Add("npc/community-membership-request-beneficiary", () => Arena(4954), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            leader.FoodPoints = world.Game.Rules.ActorMaxFood(leader);
            Actor hungry = leader.Followers.First(a => !a.IsPlayer); hungry.FoodPoints = 100;
            leader.Personality.NextCommunityRequestTurn = 0;
            var ui = (ScenarioUI)world.Game.UI;
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.D1);
            Check.Equal(true, Talk(world, player), "leader news offers actual member need");
            NpcReaction pending = NpcConversation.Pending(player, leader);
            Check.Equal(hungry.PersonalityIdentity, pending.CommunityBeneficiary, "request identifies actual beneficiary");
            Check.Equal(true, String.Join("\n", player.Personality.HeardJournal.Select(h => h.Text)).Contains("within 180 turns"), "request explains deadline");
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.Y);
            Check.Equal(true, Talk(world, player), "outstanding reply bypasses membership menu");
            NpcCommitment promise = player.Personality.Commitments.Single(p => p.Status == NpcCommitmentStatus.Active);
            Check.Equal(hungry.PersonalityIdentity, promise.Beneficiary, "promise benefits the hungry member rather than leader");
            string savedPromise = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "membership-commitment-format-5.dat");
            BinarySaveStore.Save(savedPromise, Session.Get);
            Session loadedPromise = BinarySaveStore.Load<Session>(savedPromise);
            Actor savedPlayer = loadedPromise.CurrentMap.Actors.First(a => a.IsPlayer);
            Check.Equal(promise.Beneficiary, savedPlayer.Personality.Commitments.Single(p => p.Status == NpcCommitmentStatus.Active).Beneficiary, "format 5 retains actual beneficiary commitment");
            Check.Equal(true, savedPlayer.CommunityMembership != null, "active promise and membership persist together");
            player.ActionPoints = Rules.BASE_ACTION_COST; world.Try(new ActionLeaveCommunity(player, world.Game));
            Check.Equal(NpcCommitmentStatus.Active, promise.Status, "leaving retains outstanding personal promise");
            world.Map.PlaceActorAt(player, new Point(5, 1)); Aid(world, player, hungry);
            Check.Equal(NpcCommitmentStatus.Kept, promise.Status, "real delivery after departure fulfills existing promise");
        });
        ScenarioRunner.Add("npc/community-membership-wary-rejection", () => Arena(4957), world =>
        {
            Actor player = Party(world), leader = Group(world); SocialGroup original = player.SocialGroup;
            leader.Personality = new PersonalityState(); leader.Personality.AddTrait(new TraitInstance("suspicious"));
            Aid(world, player, leader); leader.Personality.Person(player.PersonalityIdentity).Feeling = -100; string reason;
            Check.Equal(CommunityTone.Wary, CommunityEncounterSystem.Tone(world.Game, leader, player), "known useful aid still permits a wary disposition");
            player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(false, world.Try(new ActionJoinCommunity(player, world.Game, leader)), "wary leader rejects invitation");
            Check.Same(original, player.SocialGroup, "rejected admission leaves personal identity untouched");
            Check.Equal(null, player.Leader, "rejected admission changes no party edges");
            Check.Equal(Rules.BASE_ACTION_COST, player.ActionPoints, "rejected action is free");
            Check.Equal(false, world.Game.Rules.AreEnemies(player, leader), "wary rejection creates no aggression");
            leader.Personality = new PersonalityState(); leader.Personality.AddTrait(new TraitInstance("sociable")); Aid(world, player, leader);
            world.Map.RemoveXpdBase(world.Map.XpdBaseAt(leader.Location.Position));
            Check.Equal(false, CommunityMembershipSystem.CanJoin(world.Game, player, leader, out reason), "loss of home invalidates a known-aid invitation");
            Check.Equal(true, reason.Contains("valid home"), "rejection explains real home loss");
        });
        ScenarioRunner.Add("npc/community-membership-failed-succession", () => Arena(4958), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            Guid original = player.CommunityMembership.PersonalGroup.Identity, identity = player.SocialGroup.Identity;
            XpdBase root = world.Map.XpdBaseAt(leader.Location.Position);
            world.Map.AddXpdBase(new XpdBase(leader, new[] { new Point(10, 4) }, root));
            foreach (Actor member in leader.Followers.Where(a => !a.IsPlayer)) member.IsSleeping = true;
            world.Game.KillActor(null, leader, "no eligible NPC successor", false);
            Check.Equal(null, player.CommunityMembership, "failed NPC succession releases an active player membership");
            Check.Equal(null, player.Leader, "breakup restores independent player party");
            Check.Equal(original, player.SocialGroup.Identity, "breakup preserves original group identity");
            Check.Equal(ActorTasks.GUARD, ((OrderableAI)player.Followers.First().Controller).Order.Task, "breakup preserves personal orders");
            Check.Equal(null, world.Map.XpdBaseAt(new Point(4, 2)), "root claim is released");
            Check.Equal(null, world.Map.XpdBaseAt(new Point(10, 4)), "linked claim is released");
            Check.Equal(true, player.Personality.FindCommunity(identity).Notes.Any(n => n.Kind == "community_departure"), "actual breakup participation is recorded");
        });
        ScenarioRunner.Add("npc/community-membership-nested-expulsion", () => Arena(4959), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            Actor offender = player.PartyTree().Last(), victim = leader.Followers.First(a => !a.IsPlayer);
            Guid original = player.CommunityMembership.PersonalGroup.Identity; SocialGroup community = player.SocialGroup;
            world.Map.PlaceActorAt(offender, new Point(4, 1)); Fov(world);
            offender.ActionPoints = Rules.BASE_ACTION_COST; world.Game.DoMeleeAttack(offender, victim);
            for (int turn = 0; turn < 8 && player.CommunityMembership != null; turn++)
            { Time(world, turn); leader.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, world.NpcTurn(leader), "real security handles a nested personal offender"); }
            Check.Equal(null, player.CommunityMembership, "nested serious violation expels the complete player party");
            Check.Equal(original, player.SocialGroup.Identity, "nested expulsion restores personal identity");
            Check.Same(player.Followers.First(), offender.Leader, "nested expulsion preserves follower edges");
            Check.Equal(false, community.Members.Contains(offender.PersonalityIdentity), "nested offender has no stale community affiliation");
            Check.Equal(ActorTasks.GUARD, ((OrderableAI)offender.Controller).Order.Task, "nested expulsion retains personal orders");
        });
        ScenarioRunner.Add("npc/community-membership-medicine-refusal", () => Arena(4966), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            leader.FoodPoints = world.Game.Rules.ActorMaxFood(leader);
            Actor patient = leader.Followers.First(a => !a.IsPlayer); patient.HitPoints = 5;
            leader.Personality.NextCommunityRequestTurn = 0;
            var ui = (ScenarioUI)world.Game.UI;
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.D1); Talk(world, player);
            Check.Equal(patient.PersonalityIdentity, NpcConversation.Pending(player, leader).CommunityBeneficiary, "medical request names real injured member");
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.N); Talk(world, player);
            Check.Equal(true, NpcIntentSupport.HasEvent(player, "request_refused"), "declining uses ordinary refusal consequences");
            Check.Equal(true, player.CommunityMembership != null, "declining creates no mandatory duty or immediate expulsion");
            Time(world, 61); player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.D1); Talk(world, player);
            player.ActionPoints = Rules.BASE_ACTION_COST; ui.QueueWaitKey(Keys.Y); Talk(world, player);
            NpcCommitment promise = player.Personality.Commitments.Single(p => p.Status == NpcCommitmentStatus.Active);
            Check.Equal("medicine", promise.Resource, "accepted medical promise retains resource");
            world.Map.PlaceActorAt(player, new Point(5, 1)); Fov(world);
            var unrelated = new ItemMedicine(world.Game.GameItems.PILLS_SLP); player.Inventory.AddAll(unrelated);
            int aidBefore = patient.Personality.Events.Count(e => e.Kind == "helped");
            Give(world, player, patient, unrelated);
            Check.Equal(aidBefore, patient.Personality.Events.Count(e => e.Kind == "helped"), "sleep tablets do not earn injury-aid credit");
            Check.Equal(NpcCommitmentStatus.Active, promise.Status, "unrelated medicine cannot fulfill an injury-treatment promise");
            var medicine = new ItemMedicine(world.Game.GameItems.MEDIKIT); player.Inventory.AddAll(medicine);
            Give(world, player, patient, medicine);
            Check.Equal(NpcCommitmentStatus.Kept, promise.Status, "actual healing medicine fulfills the promise through G input");
            Check.Equal(true, patient.Personality.Events.Count(e => e.Kind == "helped") > aidBefore, "useful treatment earns real aid evidence");
            Check.Equal(5, patient.HitPoints, "giving grants no automatic recovery");
        });
        ScenarioRunner.Add("npc/community-membership-personal-autonomy", () => Arena(4967), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            leader.Personality.AddTrait(new TraitInstance("healer"));
            Actor patient = leader.Followers.First(a => !a.IsPlayer), other = leader.Followers.Last(a => !a.IsPlayer);
            patient.HitPoints = 1; other.IsSleeping = true;
            foreach (Actor personal in player.PartyTree().Where(a => !a.IsPlayer)) personal.Personality.AddTrait(new TraitInstance("kind"));
            NpcIntentSupport.Turn(world, patient);
            world.Map.DropItemAt(new ItemMedicine(world.Game.GameItems.MEDIKIT) { Quantity = 2 }, new Point(7, 2));
            NpcIntentSupport.Turn(world, leader);
            var proposed = world.Game.NpcContent.Collective("group_medicine").Propose(new NpcCollectiveContext(world.Game, leader,
                world.Map.Actors.Where(a => a != leader && NpcKnowledgeSystem.Visible(world.Game, leader, a.Location)).ToList()));
            Check.Equal(true, proposed == null || !player.PartyTree().Any(a => a.PersonalityIdentity == proposed.Plan.CollectorId), "production collective proposal excludes the personal subtree");
            foreach (Actor personal in player.PartyTree().Where(a => !a.IsPlayer))
            {
                Check.Equal(null, NpcIntentSupport.Intent(personal, "gather_group_medicine"), "NPC leader cannot automatically assign the personal subtree");
                Check.Equal(ActorTasks.GUARD, ((OrderableAI)personal.Controller).Order.Task, "personal orders remain in charge");
            }
            leader.RemoveFollower(other); string reason;
            Check.Equal(2, leader.CountFollowers, "one NPC and the admitted player party remain");
            Check.Equal(1, world.Game.Rules.ActorCommandedFollowers(leader), "player party consumes no NPC command slot");
            other.IsSleeping = false;
            Check.Equal(true, world.Game.Rules.CanActorTakeLead(leader, other, out reason), "ordinary NPC recruitment can refill its own vacant slot");
        });
        ScenarioRunner.Add("npc/community-membership-live-reload-cache", () => Arena(4968), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            Map previousMap = world.Map; Guid identity = player.SocialGroup.Identity;
            world.Game.ResolveCommunityHome(player);
            var cache = (System.Collections.IDictionary)typeof(RogueGame).GetField("m_CommunityHomes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world.Game);
            Check.Equal(1, cache.Count, "active session has a resolved home cache");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "membership-live-reload-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            Check.Equal(true, Check.Call(world.Game, "LoadGame", path), "production reload succeeds in the same game instance");
            Check.Equal(0, cache.Count, "production reload immediately clears transient homes");
            player = world.Game.Player;
            Check.Equal(identity, player.CommunityMembership.CommunityId, "actual reload preserves membership");
            ResolvedCommunityHome home = world.Game.ResolveCommunityHome(player);
            Check.Equal(false, home.Map == previousMap, "resolved home belongs to the reconstructed world");
            string legacy = Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_LEGACY_FILE");
            Check.Equal(true, Check.Call(world.Game, "LoadGame", legacy), "production reload also loads the genuine older format-5 world");
            Check.Equal(0, cache.Count, "older world clears caches without creating new home state");
            Check.Equal(null, world.Game.Player.CommunityMembership, "older world starts with no membership");
        });
        ScenarioRunner.Add("npc/community-membership-save-succession", () => Arena(4955), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            SocialGroup group = leader.SocialGroup; Guid original = player.CommunityMembership.PersonalGroup.Identity;
            Actor successor = leader.Followers.First(a => !a.IsPlayer);
            successor.Personality.AddTrait(new TraitInstance("loyal"));
            XpdBase root = world.Map.XpdBaseAt(leader.Location.Position);
            var linked = new XpdBase(leader, new[] { new Point(10, 4) }, root); linked.SetWeaponRoom(new Rectangle(10, 4, 1, 1)); world.Map.AddXpdBase(linked);
            world.Game.ScheduleCommunityWatch(leader);
            Guid watcher = group.Home.Watcher;
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "membership-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            ScenarioWorld saved = NpcIntentSupport.Restore(world, BinarySaveStore.Load<Session>(path));
            player = saved.Game.Player; leader = NpcIntentSupport.Find(saved.Map, leader.PersonalityIdentity);
            group = leader.SocialGroup; root = saved.Map.XpdBaseAt(leader.Location.Position); linked = saved.Map.XpdBaseAt(new Point(10, 4));
            Check.Equal(watcher, group.Home.Watcher, "saved watcher survives reload");
            Check.Equal(original, player.CommunityMembership.PersonalGroup.Identity, "original party identity persists");
            Check.Equal(true, saved.Game.ResolveCommunityHome(player) != null, "transient home cache resolves after reload");
            successor = NpcIntentSupport.Find(saved.Map, successor.PersonalityIdentity);
            player.Personality.FindCommunity(group.Identity).Leader = "Mira";
            world = saved; world.Map.PlaceActorAt(player, new Point(22, 7)); Fov(world);
            world.Game.KillActor(null, leader, "scenario death", false);
            Check.Equal(group.Identity, player.SocialGroup.Identity, "successful succession keeps player community identity");
            Check.Equal("Mira's circle", player.SocialGroup.CommunityName, "name remains stable");
            Check.Same(successor, player.Leader, "living NPC remains community leader");
            Check.Same(successor, root.GroupLeader, "root claim transfers before dead-leader cleanup");
            Check.Same(successor, linked.GroupLeader, "linked claim transfers with storage metadata");
            Check.Equal(true, linked.WeaponRoom.HasValue, "linked storage settings remain");
            Check.Equal("Mira", player.Personality.FindCommunity(group.Identity).Leader, "unwitnessed succession leaves journal leader dated");
            Conservation(player, successor);
            Check.Equal(ActorTasks.GUARD, ((OrderableAI)player.Followers.First().Controller).Order.Task, "succession keeps personal orders");
            player.ActionPoints = Rules.BASE_ACTION_COST; world.Try(new ActionLeaveCommunity(player, world.Game));
            Check.Equal(original, player.SocialGroup.Identity, "departure after succession restores original identity");
        });
        ScenarioRunner.Add("npc/community-membership-expulsion-breakup", () => Arena(4956), world =>
        {
            Actor player = Party(world), leader = Group(world); Join(world, player, leader);
            Guid original = player.CommunityMembership.PersonalGroup.Identity; SocialGroup group = leader.SocialGroup;
            Actor victim = leader.Followers.First(a => !a.IsPlayer);
            world.Map.PlaceActorAt(player, new Point(4, 1)); Fov(world);
            player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Game.Rules.CanActorMeleeAttack(player, victim), "serious violation uses actual attack");
            world.Game.DoMeleeAttack(player, victim);
            for (int turn = 0; turn < 8 && player.CommunityMembership != null; turn++)
            {
                Time(world, turn); leader.ActionPoints = Rules.BASE_ACTION_COST;
                Check.Equal(true, world.NpcTurn(leader), "actual NPC security action is legal");
                if (player.CommunityMembership == null) Check.Equal(0, leader.ActionPoints, "expulsion costs one NPC action");
            }
            Check.Equal(null, player.CommunityMembership, "real security expulsion removes membership");
            Check.Equal(original, player.SocialGroup.Identity, "expulsion restores entire personal party");
            Check.Equal(false, group.Members.Contains(player.PersonalityIdentity), "expulsion removes stale group entries");
            Check.Equal(ActorTasks.GUARD, ((OrderableAI)player.Followers.First().Controller).Order.Task, "expulsion preserves personal orders");
            Check.Equal(true, NpcIntentSupport.HasEvent(player, "member_expelled"), "production expulsion event persists");
            foreach (Actor follower in leader.Followers) follower.IsSleeping = true;
            world.Game.KillActor(null, leader, "no successor", false);
            Check.Equal(null, world.Map.XpdBaseAt(new Point(4, 2)), "failed succession releases claim normally");
            Check.Equal(null, world.Game.ResolveCommunityHome(group.Members.Count == 0 ? player : world.Map.GetActorAt(5, 2)), "lost home cannot schedule autonomous work");
        });
    }
}
