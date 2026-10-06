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
using djack.RogueSurvivor.Gameplay.Personality;

static class ScavengingScenario
{
    internal static void Journal(ScenarioWorld w, params Keys[] keys)
    {
        var ui = (ScenarioUI)w.Game.UI;
        ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.Tab);
        foreach (var key in keys) ui.QueueWaitKey(key);
        ui.QueueWaitKey(Keys.Escape);
        int before = w.Game.Player.ActionPoints;
        Check.Equal(false, Check.Call(w.Game, "HandleHeardJournal"), "notebook returns without consuming a turn");
        Check.Equal(before, w.Game.Player.ActionPoints, "notebook operations are free");
    }
    internal static void Plan(ScenarioWorld w, bool request = false)
    {
        Journal(w, request ? new[] { Keys.N, Keys.Down, Keys.Down, Keys.Enter, Keys.Enter, Keys.Enter } :
            new[] { Keys.N, Keys.Enter, Keys.Enter, Keys.Enter });
        Check.Equal(true, w.Game.Player.Personality.Scavenging.Active != null, "N records an expedition through keyboard input");
    }
    static SupplyLead FoodLead(Actor player, Point point)
    { return player.Personality.Scavenging.Leads.Single(l => l.Resource == "food" && !l.SiteOnly && l.Place.Anchor == point); }
    static ItemFood Stock(ScenarioWorld w, Point point, int units)
    { var food = new ItemFood(w.Game.GameItems.CANNED_FOOD) { Quantity = units }; w.Map.DropItemAt(food, point); return food; }
    static void V(ScenarioWorld w, params Keys[] keys)
    {
        foreach (var key in keys) ((ScenarioUI)w.Game.UI).QueueWaitKey(key);
        w.Game.Player.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, Check.Call(w.Game, "HandlePlayerTalk", w.Game.Player), "actual V input completes conversation");
        Check.Equal(0, w.Game.Player.ActionPoints, "completed V spends exactly one action");
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/scavenging-visible-and-stale", () => CommunityMembershipScenario.Arena(5001), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2); var point = new Point(5, 2);
            w.Map.AddZone(new Zone("store", new Rectangle(4, 1, 5, 5)) { BuildingKind = BuildingKind.Grocery });
            var stock = Stock(w, point, 3); CommunityMembershipScenario.Fov(w);
            var lead = FoodLead(p, point);
            Check.Equal(3, lead.Latest.Units, "visible stock records actual units");
            Check.Equal(true, p.Personality.Scavenging.Leads.Any(l => l.SiteOnly && !l.Latest.Confirmed), "store identity only establishes stock-unconfirmed hint");
            w.Map.PlaceActorAt(p, new Point(22, 7)); CommunityMembershipScenario.Time(w, 10);
            w.Map.RemoveItemAt(stock, point);
            var text = (IList<string>)Check.Call(w.Game, "ScavengingJournalLines", p, 0, 0);
            Check.Equal(3, lead.Latest.Units, "opening notebook cannot discover unseen depletion");
            Check.Equal(0, lead.Latest.ObservedTurn, "unseen depletion stays dated");
            w.Map.PlaceActorAt(p, new Point(2, 2)); CommunityMembershipScenario.Time(w, 20);
            Check.Equal(0, lead.Latest.Units, "return visibility updates only the exact empty spot");
            Check.Equal(true, p.Personality.Scavenging.Leads.Single(l => l.SiteOnly).Latest.Confirmed == false, "empty pile does not clear whole store");
            int known = p.Personality.Scavenging.Leads.Count;
            p.IsSleeping = true; Stock(w, new Point(3, 3), 2); CommunityMembershipScenario.Time(w, 21);
            Check.Equal(known, p.Personality.Scavenging.Leads.Count, "sleeping player gains no stock knowledge");
        });
        ScenarioRunner.Add("npc/scavenging-supply-conversation", () => CommunityMembershipScenario.Arena(5002), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2), speaker = NpcIntentSupport.Actor(w, "Guide", 3, 2);
            for (int i = 0; i < 4; i++)
            {
                var place = new Location(w.Map, new Point(10 + i, 2)); var food = Stock(w, place.Position, i + 1);
                w.Map.LocalTime.TurnCounter = i;
                ScavengingSystem.ObserveNpc(new NpcPerceptionContext(w.Game, speaker, place, items: w.Map.GetItemsAt(place.Position), risk: 1), "food");
            }
            CommunityMembershipScenario.Time(w, 10);
            V(w, Keys.D5);
            var heard = p.Personality.Scavenging.Leads.Where(l => l.Latest.Source.Contains("Guide")).ToList();
            Check.Equal(3, heard.Count, "V/5 shares at most three eligible supply leads");
            Check.Equal(3, heard.Max(l => l.Latest.ObservedTurn), "report preserves observation time");
            Check.Equal(true, heard.All(l => l.Latest.LearnedTurn == 10 && l.Latest.Confidence == 70 && l.Latest.Claimed), "hearing retains learned date, attenuated confidence and spoken risk");
            V(w, Keys.D5); V(w, Keys.D5);
            Check.Equal(4, p.Personality.Scavenging.Leads.Count(l => l.Latest.Source.Contains("Guide")), "repeated query teaches remaining eligible report without duplicating prior reports");
            p.ActionPoints = 100; ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Escape);
            Check.Equal(false, Check.Call(w.Game, "HandlePlayerTalk", p), "supply menu cancellation is free"); Check.Equal(100, p.ActionPoints, "no cancellation AP charge");
            NpcEvents.Publish(w.Game, "requested_food", speaker, p);
            ((ScenarioUI)w.Game.UI).QueueWaitKey(Keys.Escape);
            Check.Equal(false, Check.Call(w.Game, "HandlePlayerTalk", p), "pending Y/N request retains priority over menu");
            Check.Equal(true, NpcConversation.Pending(p, speaker) != null, "supply query cannot silently accept/refuse request");
            V(w, Keys.Y); Check.Equal(true, p.Personality.Commitments.Any(c => c.Status == NpcCommitmentStatus.Active), "actual reply creates ordinary promise");
        });
        ScenarioRunner.Add("npc/scavenging-report-boundaries", () => CommunityMembershipScenario.Arena(5003), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2), speaker = NpcIntentSupport.Actor(w, "Guide", 3, 2);
            Point point = new Point(4, 2); var food = Stock(w, point, 2); CommunityMembershipScenario.Time(w, 40);
            var snapshot = CommunityShelterLocation.At(new Location(w.Map, point));
            var report = new NpcFact { Kind = "food_cache", EventId = 700, EventTurn = 20, LearnedTurn = 20, Units = 9, Confidence = 90, SupplyLocation = snapshot };
            NpcKnowledgeSystem.Hear(w.Game, p, speaker, report);
            Check.Equal(2, FoodLead(p, point).Latest.Units, "older rumor cannot overwrite newer direct observation");
            w.Map.PlaceActorAt(p, new Point(22, 7)); w.Map.RemoveItemAt(food, point);
            CommunityMembershipScenario.Time(w, 50);
            var inventory = new Inventory(1); inventory.AddAll(food);
            ScavengingSystem.ObserveNpc(new NpcPerceptionContext(w.Game, speaker, new Location(w.Map, point), items: inventory), "food");
            // The spot is visibly empty now; actual remembered-location perception supersedes the earlier cache.
            ScavengingSystem.ObserveEmptyNpc(w.Game, speaker);
            Check.Equal(0, speaker.Personality.Knowledge.Facts.Last(f => f.Kind == "food_cache").Units, "NPC records visible depletion after the inventory disappears");
            int before = p.Personality.Scavenging.Leads.Count;
            ScavengingSystem.Share(w.Game, speaker, p, speaker.Personality.Knowledge.Facts.Last(f => f.Kind == "food_cache"));
            Check.Equal(before, p.Personality.Scavenging.Leads.Count, "distant player cannot hear a supply query broadcast");
            Check.Equal(2, FoodLead(p, point).Latest.Units, "distant unheard depletion leaves last observation intact");
        });
        ScenarioRunner.Add("npc/scavenging-trip-return-and-farming", () => CommunityMembershipScenario.Arena(5004), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2); NpcIntentSupport.Food(w, p, 1); Stock(w, new Point(6, 2), 2);
            CommunityMembershipScenario.Fov(w); p.ActionPoints = 100;
            // Plan the selected lead as destination (open search is first; Down selects the known spot).
            Journal(w, Keys.N, Keys.Enter, Keys.Down, Keys.Enter, Keys.Enter);
            var trip = p.Personality.Scavenging.Active; Check.Equal(-1, trip.ArrivedTurn, "planning does not grant remote arrival");
            while (p.Location.Position.X < 5) { w.Bump(p, Direction.E); CommunityMembershipScenario.Time(w, w.Map.LocalTime.TurnCounter + 1); }
            Check.Equal(true, trip.ArrivedTurn >= 0, "physical approach records arrival");
            var food = w.Map.GetItemsAt(6, 2).Items.OfType<ItemFood>().First();
            Check.Equal(true, w.Try(new ActionTakeItem(p, w.Game, new Point(6, 2), food)), "normal production pickup gathers stock");
            while (p.Location.Position.X > 2) { w.Bump(p, Direction.W); CommunityMembershipScenario.Time(w, w.Map.LocalTime.TurnCounter + 1); }
            p.ActionPoints = 100; Journal(w, Keys.E, Keys.Y);
            Check.Equal(true, trip.Returned, "E/Y at return records actual return"); Check.Equal(2, trip.FinalFood - trip.DepartureFood, "trip uses final carried difference");
            Plan(w); var next = p.Personality.Scavenging.Active;
            var carried = p.Inventory.Items.OfType<ItemFood>().First(); int qty = carried.Quantity;
            w.Game.DoDropItem(p, carried); var ground = w.Map.GetItemsAt(p.Location.Position).Items.OfType<ItemFood>().First();
            w.Try(new ActionTakeItem(p, w.Game, p.Location.Position, ground)); p.ActionPoints = 100; Journal(w, Keys.E, Keys.Y);
            Check.Equal(0, next.FinalFood - next.DepartureFood, "drop/pickup cycling creates no scavenging credit");
            Plan(w); w.Map.PlaceActorAt(p, new Point(20, 2)); Journal(w, Keys.E, Keys.Y);
            Check.Equal(false, p.Personality.Scavenging.Completed.Last().Returned, "ending away is not a return");
            Plan(w); Journal(w, Keys.R, Keys.C, Keys.Y); Check.Equal(true, p.Personality.Scavenging.Completed.Last().Cancelled, "R/C records cancellation without rewards");
            Journal(w, Keys.F, Keys.F, Keys.F, Keys.Enter, Keys.Escape);
        });
        ScenarioRunner.Add("npc/scavenging-request-real-delivery", () => CommunityMembershipScenario.Arena(5005), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2), recipient = NpcIntentSupport.Actor(w, "Hungry", 3, 2); recipient.FoodPoints = 100;
            NpcEvents.Publish(w.Game, "requested_food", recipient, p); V(w, Keys.Y);
            Plan(w, true); var trip = p.Personality.Scavenging.Active;
            var promise = p.Personality.Commitments.Single(c => c.Id == trip.CommitmentId);
            Journal(w, Keys.E, Keys.Y); Check.Equal(NpcCommitmentStatus.Active, promise.Status, "finishing a notebook job never fulfills promise");
            Plan(w, true); trip = p.Personality.Scavenging.Active;
            NpcIntentSupport.Food(w, p, 1); var ui = (ScenarioUI)w.Game.UI;
            ui.QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, recipient.Location.Position)); p.ActionPoints = 100;
            Check.Equal(true, Check.Call(w.Game, "HandlePlayerGiveItem", p, CommunityClarityScenario.InventorySlot(w, 0)), "real G gives to adjacent beneficiary");
            Check.Equal(NpcCommitmentStatus.Kept, promise.Status, "actual transfer alone fulfills linked commitment");
            p.ActionPoints = 100; Journal(w, Keys.E, Keys.Y);
            Check.Equal(true, String.Join("\n", (IList<string>)Check.Call(w.Game, "ScavengingJournalLines", p, 0, 0)).Contains("Kept"), "completed record follows player's own fulfilled promise evidence");
            recipient.FoodPoints = 100; CommunityMembershipScenario.Time(w, 60);
            NpcEvents.Publish(w.Game, "requested_food", recipient, p); V(w, Keys.Y); Plan(w, true);
            var active = p.Personality.Commitments.Last(c => c.Status == NpcCommitmentStatus.Active); int deadline = active.DueTurn;
            Journal(w, Keys.C, Keys.Y); Check.Equal(NpcCommitmentStatus.Active, active.Status, "cancelling expedition leaves promise intact");
            Check.Equal(deadline, active.DueTurn, "trip cancellation never extends deadline");
        });
        ScenarioRunner.Add("npc/scavenging-save-bounds-and-legacy", () => CommunityMembershipScenario.Arena(5006), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2); Stock(w, new Point(5, 2), 2); CommunityMembershipScenario.Fov(w);
            Journal(w, Keys.N, Keys.Enter, Keys.Down, Keys.Enter, Keys.Enter);
            var state = p.Personality.Scavenging; Guid id = state.Active.Destination.Id;
            for (int i = 0; i < 90; i++) state.Record("food", "older report", false, new CommunityShelterLocation { MapName = "remote" + i, MapSeed = i, Anchor = new Point(1, 1) },
                new SupplyObservation { ObservedTurn = i, LearnedTurn = i, Confidence = 60, Confirmed = true, Units = 1, Source = "reported" });
            Check.Equal(64, state.Leads.Count, "leads bound to 64"); Check.Equal(id, state.Active.Destination.Id, "active destination survives trimming");
            for (int i = 100; i < 110; i++) state.Record("food", "report", false, state.Leads.Last().Place,
                new SupplyObservation { ObservedTurn = i, Confidence = 60, Units = i, Confirmed = true, Source = "reported" });
            Check.Equal(4, state.Leads.Last().Observations.Count, "observation history bound to four");
            for (int i = 0; i < 20; i++) state.Completed.Add(new ScavengingTrip());
            // Use the production completion path to enforce retained completed-history size.
            Journal(w, Keys.E, Keys.Y); Check.Equal(16, state.Completed.Count, "completed trips bound to sixteen");
            Plan(w); string file = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "scavenging-active-format-5.dat");
            BinarySaveStore.Save(file, Session.Get); var loaded = BinarySaveStore.Load<Session>(file);
            var restored = NpcIntentSupport.Restore(w, loaded); var restoredState = restored.Game.Player.Personality.Scavenging;
            Check.Equal(state.Active.Id, restoredState.Active.Id, "active trip persists with format five");
            Check.Equal(64, restoredState.Leads.Count, "bounded notebook persists"); Check.Equal(16, restoredState.Completed.Count, "completed history persists");
            foreach (var type in new[] { typeof(ScavengingState), typeof(ScavengingTrip), typeof(SupplyLead), typeof(SupplyObservation), typeof(CommunityShelterLocation) })
                Check.Equal(false, type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).Any(f => f.FieldType == typeof(Map) || f.FieldType == typeof(Actor)), "new records hold no live map/actor fields");
            var legacy = BinarySaveStore.Load<Session>(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_LEGACY_FILE"));
            Check.Equal(false, legacy.CurrentMap.Actors.First(a => a.IsPlayer).Personality.HasScavenging, "genuine old format-five notebook starts empty");
            Check.Equal(false, legacy.GamePreset.PatchySupplies, "old preset never redistributes on load");
        });
        ScenarioRunner.Add("npc/scavenging-legacy-resource-report", () => CommunityMembershipScenario.Arena(5009), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2), speaker = NpcIntentSupport.Actor(w, "Old guide", 3, 2);
            var spot = new Location(w.Map, new Point(20, 2));
            var old = new NpcFact { Kind = "food_cache", EventId = 912, Units = 3, Place = spot, EventTurn = 0, Confidence = 90, Source = NpcKnowledgeSource.Witness };
            speaker.Personality.Knowledge.Learn(old);
            w.Map.DropItemAt(new ItemFood(w.Game.GameItems.CANNED_FOOD) { Quantity = 9 }, spot.Position);
            w.Map.AddXpdBase(new XpdBase(speaker, new[] { spot.Position })); CommunityMembershipScenario.Time(w, 20);
            V(w, Keys.D5);
            var learned = p.Personality.Scavenging.Leads.Single(l => l.Place.Anchor == spot.Position);
            Check.Equal(3, learned.Latest.Units, "older saved resource fact can be reported without reading hidden current stock");
            Check.Equal(0, learned.Latest.ObservedTurn, "legacy report preserves original observation date");
            Check.Equal(false, learned.Latest.Claimed, "unwitnessed new claim cannot be added to an old resource report");
            Check.Equal(null, old.SupplyLocation, "conversation does not retrofit live map state into stored old fact");
        });
        ScenarioRunner.Add("npc/scavenging-medicine-claims-and-expiry", () => CommunityMembershipScenario.Arena(5007), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2), leader = CommunityMembershipScenario.Group(w);
            var spot = new Point(6, 4); var pills = new ItemMedicine(w.Game.GameItems.PILLS_SLP);
            w.Map.DropItemAt(pills, spot); CommunityMembershipScenario.Fov(w);
            Check.Equal(false, p.Personality.Scavenging.Leads.Any(l => !l.SiteOnly && l.Resource == "medicine"), "non-healing pills never count as expedition medicine");
            var kit = new ItemMedicine(w.Game.GameItems.MEDIKIT); w.Map.DropItemAt(kit, spot); CommunityMembershipScenario.Fov(w);
            var lead = p.Personality.Scavenging.Leads.Single(l => !l.SiteOnly && l.Resource == "medicine");
            Check.Equal(true, lead.Latest.Claimed, "visible supplies retain an ownership warning"); Check.Equal(null, lead.Latest.ClaimName, "unknown community name stays unknown");
            ScavengingSystem.ObserveNpc(new NpcPerceptionContext(w.Game, leader, new Location(w.Map, spot), items: w.Map.GetItemsAt(spot)), "medicine");
            var fact = leader.Personality.Knowledge.Facts.Last(f => f.Kind == "medicine_cache");
            Check.Equal(true, fact.SupplyClaimed && fact.Risk == 0, "owner knows the stock is claimed even when personally allowed to take it");
            w.Map.RemoveItemAt(kit, spot); CommunityMembershipScenario.Fov(w);
            Check.Equal(0, lead.Latest.Units, "same-turn witnessed depletion replaces direct stock sighting");
            w.Map.PlaceActorAt(p, new Point(3, 2)); CommunityMembershipScenario.Time(w, 40);
            leader.HitPoints = w.Game.Rules.ActorMaxHPs(leader) / 3;
            NpcEvents.Publish(w.Game, "requested_medicine", leader, p); V(w, Keys.Y); Plan(w, true);
            var trip = p.Personality.Scavenging.Active; var promise = p.Personality.Commitments.Single(c => c.Id == trip.CommitmentId);
            Check.Equal("medicine", trip.Resource, "real medicine request links the medicine expedition");
            CommunityMembershipScenario.Time(w, promise.DueTurn); NpcPromises.Expire(p, w.Game.NpcContent);
            Check.Equal(NpcCommitmentStatus.Broken, promise.Status, "ordinary expiration remains active while expedition is recorded");
            Journal(w, Keys.C, Keys.Y); Check.Equal(NpcCommitmentStatus.Broken, promise.Status, "cancelling never forgives an expired promise");
        });
        ScenarioRunner.Add("npc/scavenging-real-j-input", () => CommunityMembershipScenario.Arena(5008), w =>
        {
            Actor p = NpcIntentSupport.Player(w, 2, 2); CommunityMembershipScenario.Fov(w); p.ActionPoints = 100;
            var ui = (ScenarioUI)w.Game.UI; ui.QueueKey(Keys.J); ui.QueueKey(Keys.NumPad5);
            foreach (var key in new[] { Keys.Tab, Keys.Tab, Keys.N, Keys.Enter, Keys.Enter, Keys.Enter, Keys.Escape }) ui.QueueWaitKey(key);
            Check.Call(w.Game, "HandlePlayerActor", p);
            Check.Equal(true, p.Personality.Scavenging.Active != null, "actual J binding opens third Tab view and N plans a trip");
            Check.Equal(0, p.ActionPoints, "only following normal Wait consumes the turn");
        });
    }
}
