using System;
using System.Collections;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;

// Controlled two-district fixtures. These verify real inputs/transfers, not natural offer frequency.
static class TradeCircuitScenario
{
    static Actor sender, recipient;
    static Map away;
    static ScenarioWorld Setup(int seed)
    {
        var w = CommunityAdaptationScenario.Arena(seed); var p = w.Game.Player;
        sender = NpcIntentSupport.Actor(w, "Mira", 14, 4, "kind", "sociable");
        var companion = NpcIntentSupport.Actor(w, "Sam", 16, 4); sender.AddFollower(companion);
        sender.SocialGroup.CommunityName = "Mira's household";
        sender.Inventory.MaxCapacity = 12;
        for (int n = 0; n < 5; n++) sender.Inventory.AddIntact(new ItemFood(w.Game.GameItems.CANNED_FOOD) { Quantity = w.Game.GameItems.CANNED_FOOD.StackingLimit });
        sender.Inventory.AddIntact(new ItemMedicine(w.Game.GameItems.MEDIKIT) { Quantity = 3 });
        recipient = NpcIntentSupport.Actor(w, "Terry", 24, 4, "kind");
        var other = NpcIntentSupport.Actor(w, "Robin", 25, 4); recipient.AddFollower(other);
        recipient.SocialGroup.CommunityName = "Terry's household";
        away = new Map(seed + 1, "Neighbouring shelter", 30, 12);
        for (int y = 0; y < 12; y++) for (int x = 0; x < 30; x++) away.SetTileModelAt(x, y, w.Game.GameTiles.FLOOR_ASPHALT);
        var world = new World(2); world[0, 0] = w.Map.District;
        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
        {
            if (x == 0 && y == 0) continue;
            var district = new District(new Point(x, y), DistrictKind.RESIDENTIAL);
            if (x == 1 && y == 0) district.EntryMap = away;
            else
            {
                var empty = new Map(seed + 10 + x + 2 * y, "unused", 30, 12);
                for (int j = 0; j < 12; j++) for (int i = 0; i < 30; i++) empty.SetTileModelAt(i, j, w.Game.GameTiles.FLOOR_ASPHALT);
                district.EntryMap = empty;
            }
            world[x, y] = district;
        }
        // Only the linked pair is used. Keep a distinct district identity for its destination.
        world[1, 0].EntryMap = away; Session.Get.World = world;
        w.Map.RemoveActor(recipient); away.PlaceActorAt(recipient, new Point(4, 4));
        w.Map.RemoveActor(other); away.PlaceActorAt(other, new Point(7, 4));
        var remembered = sender.Personality.Knowledge.See(recipient, 360);
        remembered.FoodNeed = 80; remembered.FoodNeedTurn = 360; remembered.FoodConfidence = 100;
        w.Map.SetExitAt(new Point(14, 6), new Exit(away, new Point(4, 6)) { IsAnAIExit = true });
        away.SetExitAt(new Point(4, 6), new Exit(w.Map, new Point(14, 6)) { IsAnAIExit = true });
        var options = RogueGame.Options; options.SimThread = false; options.SimulateDistricts = GameOptions.SimRatio.OFF;
        typeof(RogueGame).GetField("s_Options", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, options);
        CommunityMembershipScenario.Fov(w); return w;
    }
    static void Near(ScenarioWorld w, Actor person)
    {
        var p = w.Game.Player; var map = person.Location.Map;
        if (p.Location.Map != map) p.Location.Map.RemoveActor(p);
        var point = new Point(person.Location.Position.X, person.Location.Position.Y + 1);
        if (p.Location.Map != map || p.Location.Position != point) map.PlaceActorAt(p, point);
        Session.Get.CurrentMap = map; CommunityMembershipScenario.Fov(w);
    }
    static bool Talk(ScenarioWorld w, Actor person, params Keys[] keys)
    {
        Near(w, person); var p = w.Game.Player; var ui = (ScenarioUI)w.Game.UI;
        int count = p.Location.Map.Actors.Count(a => a != p && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent &&
            !w.Game.Rules.AreEnemies(p, a) && w.Game.Rules.GridDistance(p.Location.Position, a.Location.Position) <= 1);
        if (count > 1) ui.QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, person.Location.Position));
        ui.QueueWaitKey(Keys.D6); foreach (var key in keys) ui.QueueWaitKey(key);
        p.ActionPoints = Rules.BASE_ACTION_COST;
        return (bool)Check.Call(w.Game, "HandlePlayerTalk", p);
    }
    static Keys[] Menu(int choice, params Keys[] rest)
    { return Enumerable.Repeat(Keys.Down, choice).Concat(new[] { Keys.Enter }).Concat(rest).ToArray(); }
    static void Accept(ScenarioWorld w)
    {
        var offers = (IList)Check.Call(w.Game, "FreightOffers", w.Game.Player, sender);
        if (offers.Count == 0)
        {
            Console.WriteLine("No fixture offers; surplus=" + ((IList)Check.Call(w.Game, "TradeSurplus", sender)).Count +
                "; sender=" + sender.Location.Map.District.WorldPosition + "; recipient=" + recipient.Location.Map.District.WorldPosition);
            foreach (var item in sender.Inventory.Items) Console.WriteLine(item.Model.SingleName + " x" + item.Quantity + ": " + ((BaseAI)sender.Controller).RateItem(w.Game, item, true));
        }
        Check.Equal(true, Talk(w, sender, Menu(2, Keys.Enter, Keys.Enter)), "V / 6 accepts a real commission");
        Check.Equal(0, w.Game.Player.ActionPoints, "acceptance costs one normal action");
        Check.Equal(true, w.Game.Player.Personality.TradeRoutes.Active != null, "active agreement saved on courier");
    }
    static Item Cargo(ScenarioWorld w)
    { return w.Game.Player.Inventory.Items.First(i => i.IsEntrustedCargo); }
    static ScenarioWorld Restore(ScenarioWorld w, string file)
    {
        var restored = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(file));
        // The minimal helper reconstructs only the active map. Native loading reconstructs all maps.
        for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
            foreach (var map in Session.Get.World[x, y].Maps) map.ReconstructAuxiliaryFields();
        return restored;
    }
    static void Give(ScenarioWorld w, Actor person, Item item)
    {
        Near(w, person); var p = w.Game.Player; string reason;
        Check.Equal(true, w.Game.Rules.CanActorGiveItemTo(p, person, item, out reason), "G precondition: " + reason);
        int slot = p.Inventory.Items.TakeWhile(i => i != item).Count();
        ((ScenarioUI)w.Game.UI).QueueKey(CommunityClarityScenario.DirectionKey(p.Location.Position, person.Location.Position));
        p.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, Check.Call(w.Game, "HandlePlayerGiveItem", p, CommunityClarityScenario.InventorySlot(w, slot)), "actual G input delivers cargo");
        Check.Equal(0, p.ActionPoints, "cargo transfer costs exactly one action");
    }
    static int TotalFood(ScenarioWorld w)
    {
        var maps = new[] { Session.Get.World[0, 0].EntryMap, away };
        return maps.Sum(m => m.Actors.Sum(a => NpcIntentSupport.FoodUnits(a)) +
            m.GroundInventories.Sum(i => i.Items.OfType<ItemFood>().Sum(f => f.Quantity)));
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/trade-circuit-input-and-receipt", () => Setup(5600), w => {
            int total = TotalFood(w); Accept(w); var p = w.Game.Player; var job = p.Personality.TradeRoutes.Active;
            Check.Equal(total, TotalFood(w), "acceptance conserves every food unit");
            Give(w, recipient, Cargo(w));
            Check.Equal(job.Units, job.Delivered, "receipt requires physical transfer");
            Check.Equal(0, sender.Personality.TradeRoutes.Issued.Single().Delivered, "absent sender has no remote receipt");
            int medicine = p.Inventory.Items.OfType<ItemMedicine>().Sum(i => i.Quantity);
            Check.Equal(true, Talk(w, sender, Menu(3)), "report and collect through V / 6");
            Check.Equal(medicine + 1, p.Inventory.Items.OfType<ItemMedicine>().Sum(i => i.Quantity), "real payment received");
            Check.Equal(null, p.Personality.TradeRoutes.Active, "completed agreement releases active slot");
            Check.Equal("Delivered and paid", p.Personality.TradeRoutes.History.Single().Status, "paid outcome recorded");
            Check.Equal(total, TotalFood(w), "complete delivery conserves food");
            Check.Equal(false, Talk(w, sender, Menu(3)), "cannot collect a second time");
        });
        ScenarioRunner.Add("npc/trade-circuit-partial-and-reload", () => Setup(5601), w => {
            Accept(w); var p = w.Game.Player; var job = p.Personality.TradeRoutes.Active;
            recipient.Inventory.MaxCapacity = 1;
            recipient.Inventory.AddIntact(new ItemFood(w.Game.GameItems.CANNED_FOOD) { Quantity = w.Game.GameItems.CANNED_FOOD.StackingLimit - 1 });
            Give(w, recipient, Cargo(w)); Check.Equal(1, job.Delivered, "only actual received capacity earns receipt");
            Guid sid = sender.PersonalityIdentity, rid = recipient.PersonalityIdentity;
            string file = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "trade-partial.dat");
            BinarySaveStore.Save(file, Session.Get); w = Restore(w, file);
            away = Session.Get.CurrentMap; sender = NpcIntentSupport.Find(Session.Get.World[0, 0].EntryMap, sid); recipient = NpcIntentSupport.Find(away, rid);
            p = w.Game.Player; job = p.Personality.TradeRoutes.Active;
            Check.Equal(1, job.Delivered, "partial receipt survives actual format-5 round trip");
            Check.Equal(true, Cargo(w).TradeCargo == p.Personality.TradeRoutes.Cargo, "shared lot marker reconstructed");
            recipient.Inventory.MaxCapacity = 3; Give(w, recipient, Cargo(w));
            Check.Equal(job.Units, job.Delivered, "remaining delivery is credited once");
            Check.Equal(true, Talk(w, sender, Menu(3)), "reloaded receipt pays normally");
        });
        ScenarioRunner.Add("npc/trade-circuit-custody-and-consumption", () => Setup(5602), w => {
            Accept(w); var p = w.Game.Player; var item = Cargo(w); var job = p.Personality.TradeRoutes.Active;
            var own = new ItemFood(w.Game.GameItems.CANNED_FOOD); p.Inventory.AddAll(own); p.Inventory.Defrag();
            Check.Equal(true, p.Inventory.Contains(item) && p.Inventory.Contains(own), "personal and entrusted stacks remain distinct");
            string reason; var outsider = sender.Followers.First();
            Check.Equal(false, w.Game.Rules.CanActorGiveItemTo(p, outsider, item, out reason), "ordinary follower transfer cannot divert cargo silently");
            Check.Equal(BaseAI.TradeRating.REFUSE, ((BaseAI)sender.Controller).RateTradeOffer(w.Game, p, item, sender.Inventory.Items.Last()), "barter rejects entrusted goods before trust overrides");
            p.FoodPoints = 100; Check.Equal(true, w.Try(new ActionUseItem(p, w.Game, item)), "emergency eating is possible");
            Check.Equal(job.Units - 1, item.Quantity, "consumption leaves actual shortfall");
            Check.Equal(0, job.Delivered, "consumption cannot count as a delivery");
            Check.Equal(true, Talk(w, sender, Menu(5)), "commit a real personally owned replacement");
            Give(w, recipient, Cargo(w)); Give(w, recipient, Cargo(w));
            Check.Equal(job.Units, job.Delivered, "replacement and retained cargo fulfill actual quantity");
        });
        ScenarioRunner.Add("npc/trade-circuit-payment-shortage", () => Setup(5603), w => {
            Accept(w); Give(w, recipient, Cargo(w)); var p = w.Game.Player;
            foreach (var med in sender.Inventory.Items.OfType<ItemMedicine>().ToArray()) sender.Inventory.RemoveAllQuantity(med);
            Check.Equal(true, Talk(w, sender, Menu(3)), "sender explains actual unavailable payment");
            Check.Equal(true, p.Personality.TradeRoutes.Active.Status.Contains("Payment unavailable"), "payer's obligation retained");
            Check.Equal(0, p.Inventory.Items.OfType<ItemMedicine>().Sum(i => i.Quantity), "no replacement payment generated");
            Check.Equal(true, Talk(w, sender, Menu(4, Keys.Enter)), "courier may waive the unpayable fee explicitly");
            Check.Equal(null, p.Personality.TradeRoutes.Active, "waiver frees active slot");
        });
        ScenarioRunner.Add("npc/trade-circuit-world-clock", () => Setup(5604), w => {
            Accept(w); var job = w.Game.Player.Personality.TradeRoutes.Active;
            Session.Get.WorldTime.TurnCounter = job.Due + 1; away.LocalTime.TurnCounter = 10;
            Give(w, recipient, Cargo(w)); Check.Equal(0, job.TimelyDelivered, "stale destination clock cannot extend world deadline");
            Check.Equal(true, Talk(w, sender, Menu(3)), "late receipt can be reported and resolved");
            Check.Equal(false, w.Game.Player.Personality.TradeRoutes.History.Single().Status == "Delivered and paid", "late delivery does not earn the on-time commission");
        });
        ScenarioRunner.Add("npc/trade-circuit-expiry-preservation", () => Setup(5605), w => {
            Accept(w); var cargo = Cargo(w); var p = w.Game.Player;
            // Replace fixture food by a perishable lot carrying the same real custody marker.
            var perishable = new ItemFood(w.Game.GameItems.CANNED_FOOD, 10000) { Quantity = cargo.Quantity, TradeCargo = cargo.TradeCargo };
            p.Inventory.RemoveAllQuantity(cargo); p.Inventory.AddIntact(perishable);
            var part = perishable.SplitUnits(1); p.Inventory.AddAll(part); p.Inventory.Defrag();
            Check.Equal(2, p.Inventory.Items.Count(i => i.IsEntrustedCargo), "splitting retains distinct custody and expiry");
            Check.Equal(10000, ((ItemFood)part).BestBefore.TurnCounter, "split preserves expiry exactly");
            Give(w, recipient, part); Check.Equal(10000, recipient.Inventory.Items.OfType<ItemFood>().Single().BestBefore.TurnCounter, "actual delivery preserves expiry");
        });
        ScenarioRunner.Add("npc/trade-circuit-offer-knowledge-and-cancel", () => Setup(5606), w => {
            var source = sender.Personality.Knowledge.Person(recipient.PersonalityIdentity); source.FoodConfidence = 20;
            Check.Equal(0, ((IList)Check.Call(w.Game, "FreightOffers", w.Game.Player, sender)).Count, "uncertain need cannot generate a commission");
            source.FoodConfidence = 100;
            Check.Equal(false, Talk(w, sender, Menu(2, Keys.Enter, Keys.Escape)), "acceptance confirmation can cancel freely");
            Check.Equal(Rules.BASE_ACTION_COST, w.Game.Player.ActionPoints, "cancelled commission is free");
            Check.Equal(false, w.Game.Player.Personality.HasTradeRoutes, "cancellation creates no cargo state");
        });
        ScenarioRunner.Add("npc/trade-circuit-journal-is-snapshot", () => Setup(5607), w => {
            Accept(w); var p = w.Game.Player; string before = String.Join("\n", (System.Collections.Generic.IList<string>)Check.Call(w.Game, "TradeJournalLines", p));
            recipient.Name = "Unseen new name"; recipient.IsDead = true;
            var ui = (ScenarioUI)w.Game.UI; ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.Escape);
            p.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(false, Check.Call(w.Game, "HandleHeardJournal"), "journal view is free");
            Check.Equal(Rules.BASE_ACTION_COST, p.ActionPoints, "view spends no action");
            Check.Equal(before, String.Join("\n", (System.Collections.Generic.IList<string>)Check.Call(w.Game, "TradeJournalLines", p)), "unseen death and name changes do not refresh notebook");
        });
        ScenarioRunner.Add("npc/trade-circuit-diversion-and-report", () => Setup(5608), w => {
            Accept(w); var p = w.Game.Player; var cargo = Cargo(w);
            Check.Equal(true, Talk(w, sender, Menu(6, Keys.Enter)), "explicit diversion releases physical lot");
            Check.Equal(false, cargo.IsEntrustedCargo, "diverted goods usable for personal trade");
            Check.Equal(true, Talk(w, sender, Menu(3)), "missing cargo is reported without fabricated receipt");
            Check.Equal("Reported missing cargo", p.Personality.TradeRoutes.History.Single().Status, "actual loss remains recorded");
            Check.Equal(false, w.Game.Rules.AreEnemies(p, sender), "reported shortfall creates no automatic violent feud");
        });
        ScenarioRunner.Add("npc/trade-circuit-suspension-and-return", () => Setup(5609), w => {
            Accept(w); var p = w.Game.Player; var ui = (ScenarioUI)w.Game.UI;
            ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.C); ui.QueueWaitKey(Keys.Enter);
            p.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, Check.Call(w.Game, "HandleHeardJournal"), "explicit suspension spends an action and resumes play");
            Check.Equal(null, p.Personality.TradeRoutes.Active, "failed journey cannot trap the sole active slot");
            Check.Equal("Awaiting courier report", sender.Personality.TradeRoutes.Issued.Single().Status, "suspension does not notify absent participants");
            Check.Equal(true, Talk(w, sender, Menu(3)), "later contact resolves an archived obligation");
        });
        ScenarioRunner.Add("world/trade-circuit-round-trip", () => Setup(5610), w => {
            var p = w.Game.Player;
            p.Inventory.AddIntact(new ItemFood(w.Game.GameItems.CANNED_FOOD));
            recipient.Inventory.AddIntact(new ItemMedicine(w.Game.GameItems.MEDIKIT));
            recipient.Inventory.AddIntact(new ItemMedicine(w.Game.GameItems.MEDIKIT));
            recipient.FoodPoints = 100;
            Guid sid = sender.PersonalityIdentity, rid = recipient.PersonalityIdentity;
            int food = TotalFood(w); Accept(w);
            p.ActionPoints = 100;
            Check.Equal(true, w.Game.DoPlayerBump(p, Direction.S), "ordinary walk to outward exit");
            Check.Equal(true, w.Game.DoUseExit(p, p.Location.Position), "actual district crossing with both cargo types");
            Check.Same(away, p.Location.Map, "destination district reached physically");
            p.ActionPoints = 100; Check.Equal(true, w.Game.DoPlayerBump(p, Direction.N), "walk from landing to trading partner");
            Check.Equal(true, Talk(w, recipient, Menu(1, Keys.D1, Keys.D1, Keys.Enter)), "actual negotiated personal barter while carrying commission");
            Check.Equal(1, p.Inventory.Items.OfType<ItemMedicine>().Sum(i => i.Quantity), "personal merchant exchange received real medicine");
            Check.Equal(true, p.Inventory.Items.Any(i => i.IsEntrustedCargo), "personal barter leaves entrusted shipment intact");
            string file = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "trade-circuit-midjourney.dat");
            BinarySaveStore.Save(file, Session.Get); w = Restore(w, file);
            p = w.Game.Player; away = p.Location.Map; recipient = NpcIntentSupport.Find(away, rid);
            sender = NpcIntentSupport.Find(Session.Get.World[0, 0].EntryMap, sid);
            Give(w, recipient, Cargo(w));
            p.ActionPoints = 100; Check.Equal(true, w.Game.DoPlayerBump(p, Direction.S), "ordinary return walk to exit");
            Check.Equal(true, w.Game.DoUseExit(p, p.Location.Position), "actual return district crossing");
            p.ActionPoints = 100; Check.Equal(true, w.Game.DoPlayerBump(p, Direction.N), "return to the sender on foot");
            Check.Equal(true, Talk(w, sender, Menu(3)), "carried receipt settles commission after real return");
            Check.Equal(2, p.Inventory.Items.OfType<ItemMedicine>().Sum(i => i.Quantity), "personal barter proceeds plus actual courier payment");
            Check.Equal(food, TotalFood(w), "round trip conserves food through barter, delivery and reload");
            Console.WriteLine("Two-district controlled journey: outward/return exits, personal barter, mid-trip reload, actual cargo delivery and actual payment passed.");
        });
        ScenarioRunner.Add("npc/trade-circuit-drop-pickup", () => Setup(5611), w => {
            Accept(w); var p = w.Game.Player; var cargo = Cargo(w); var tag = cargo.TradeCargo;
            p.ActionPoints = 100; w.Game.DoDropItem(p, cargo);
            Check.Equal(false, p.Inventory.Contains(cargo), "cargo is physically on ground");
            Check.Equal(true, w.Try(new ActionTakeItem(p, w.Game, p.Location.Position, cargo)), "normal pickup recovers the actual lot");
            Check.Same(tag, Cargo(w).TradeCargo, "drop and pickup preserve custody marker");
            Check.Equal(0, p.Personality.TradeRoutes.Active.Delivered, "drop/pickup cycling earns no receipt");
        });
        ScenarioRunner.Add("npc/trade-circuit-return-no-aid-credit", () => Setup(5614), w => {
            int food = TotalFood(w); Accept(w); var p = w.Game.Player;
            Check.Equal(false, Talk(w, sender, Menu(3)), "holding cargo cannot produce a completed report");
            Check.Equal(-1, sender.Personality.TradeRoutes.Issued.Single().Reported, "cancelled report teaches no new outcome");
            Give(w, sender, Cargo(w));
            Check.Equal(false, NpcIntentSupport.HasEvent(sender, "shared_food"), "returning the owner's cargo is not donated aid");
            Check.Equal(true, Talk(w, sender, Menu(3)), "return and close uncompleted journey");
            Check.Equal(0, p.Inventory.Items.OfType<ItemMedicine>().Sum(i => i.Quantity), "returned cargo earns no commission");
            Check.Equal(food, TotalFood(w), "dispatch and return conserve every food unit");
        });
        ScenarioRunner.Add("npc/trade-circuit-existing-promise", () => Setup(5615), w => {
            Accept(w); var p = w.Game.Player;
            var promise = new NpcCommitment { Id = 654321, Promisor = p.PersonalityIdentity, Beneficiary = recipient.PersonalityIdentity,
                PromisorName = p.UnmodifiedName, BeneficiaryName = recipient.UnmodifiedName, Resource = "food", Units = 1,
                DueTurn = 540, Status = NpcCommitmentStatus.Active };
            p.Personality.RememberCommitment(promise); recipient.Personality.RememberCommitment(promise);
            Give(w, recipient, Cargo(w));
            Check.Equal(NpcCommitmentStatus.Kept, p.Personality.Commitments.Single().Status, "real courier delivery also satisfies actual personal resource promise");
            Check.Equal(NpcCommitmentStatus.Kept, recipient.Personality.Commitments.Single().Status, "beneficiary witnesses fulfillment");
            Check.Equal(0, sender.Personality.TradeRoutes.Issued.Single().Delivered, "promise fulfillment still supplies no remote receipt to sender");
        });
        ScenarioRunner.Add("npc/trade-circuit-genuine-legacy", () => TownScenarioFactory.Create(5616, false), w => {
            string file = Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_LEGACY_FILE");
            var saved = BinarySaveStore.Load<Session>(file);
            Check.Equal(true, saved.CurrentMap.CountActors > 0, "genuine pre-community fixture contains actors");
            foreach (var actor in saved.CurrentMap.Actors)
            {
                if (actor.Personality != null) Check.Equal(false, actor.Personality.HasTradeRoutes, "old actors start with empty trade state");
                if (actor.Inventory != null) foreach (var item in actor.Inventory.Items)
                    Check.Equal(null, item.TradeCargo, "old inventory acquires no invented ownership marker");
            }
        });
        ScenarioRunner.Add("npc/trade-circuit-share-dated-need", () => Setup(5612), w => {
            var p = w.Game.Player; sender.Personality.Knowledge.People.Clear();
            var report = p.Personality.Knowledge.See(recipient, 360);
            report.FoodNeed = 70; report.FoodNeedTurn = 360; report.FoodConfidence = 100;
            Check.Equal(true, Talk(w, sender, Menu(7, Keys.Enter)), "player speaks a dated need through V / 6");
            var learned = sender.Personality.Knowledge.Person(recipient.PersonalityIdentity);
            Check.Equal(70, learned.FoodNeed, "sender learns the stated need");
            Check.Equal(80, learned.FoodConfidence, "retelling reduces certainty");
            learned.FoodNeed = 0; learned.FoodNeedTurn = 400; learned.FoodConfidence = 100;
            Check.Equal(true, Talk(w, sender, Menu(7, Keys.Enter)), "old report can be retold without overriding new evidence");
            Check.Equal(0, learned.FoodNeed, "newer firsthand resolved need remains authoritative");
            Check.Equal(0, ((IList)Check.Call(w.Game, "FreightOffers", p, sender)).Count, "stale report cannot produce a resolved-need shipment");
        });
        ScenarioRunner.Add("npc/trade-circuit-reserves-and-sleep", () => Setup(5613), w => {
            sender.IsSleeping = true;
            Check.Equal(0, ((IList)Check.Call(w.Game, "FreightOffers", w.Game.Player, sender)).Count, "sleeping sender cannot consent");
            sender.IsSleeping = false; sender.FoodPoints = 10;
            Check.Equal(0, ((IList)Check.Call(w.Game, "FreightOffers", w.Game.Player, sender)).Count, "hungry sender does not commission their survival food");
            sender.FoodPoints = w.Game.Rules.ActorMaxFood(sender);
            sender.HitPoints = w.Game.Rules.ActorMaxHPs(sender) / 2;
            var offers = (IList)Check.Call(w.Game, "FreightOffers", w.Game.Player, sender);
            // Food may pay for food, but needed healing medicine may not be offered or commissioned.
            foreach (object offer in offers)
            {
                var field = offer.GetType().GetField("Payment");
                Check.Equal(false, field.GetValue(offer) is ItemMedicine, "needed personal medicine stays with its owner");
            }
        });
        foreach (int value in new[] { 5210, 5211, 5212 })
        {
            int seed = value;
            ScenarioRunner.Add("world/trade-circuit-generated-suppliers-" + seed, () => TownScenarioFactory.Create(seed, false), w => {
                CommunityNewWorldScenario.Generate(w, false, true, Skills.IDs.CARPENTRY, true);
                int actors = 0, suppliers = 0, food = 0, medicine = 0, possibleCommissions = 0;
                for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) foreach (var map in Session.Get.World[x, y].Maps)
                    foreach (var person in map.Actors.Where(a => !a.IsPlayer && !a.IsDead && a.Controller is BaseAI && a.Inventory != null))
                    {
                        actors++;
                        var spare = (System.Collections.Generic.List<Item>)Check.Call(w.Game, "TradeSurplus", person);
                        if (spare.Count > 0) suppliers++;
                        if (spare.Any(i => i is ItemFood)) food++;
                        if (spare.Any(i => i is ItemMedicine)) medicine++;
                        if (spare.Count >= 2) possibleCommissions++;
                    }
                string line = "Fresh organic seed " + seed + ": NPCs=" + actors + ", suppliers=" + suppliers + ", food=" + food +
                    ", medicine=" + medicine + ", owners with distinct spare cargo/payment lots=" + possibleCommissions +
                    ". A commission also requires communicated cross-map need, consent, and player contact. Generation-only diagnostic; no completed natural journey claimed.";
                Console.WriteLine(line);
                File.WriteAllText(Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "trade-generated-suppliers-" + seed + ".txt"), line);
                Check.Equal(true, actors > 0, "diagnostic measured real generated residents");
            });
        }
    }
}
