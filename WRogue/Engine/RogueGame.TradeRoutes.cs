using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        sealed class FreightOffer
        {
            public Item Cargo, Payment;
            public NpcKnownPerson Recipient;
            public string Resource;
            public int Units;
        }
        int TradeClock { get { return m_Session.WorldTime.TurnCounter; } }
        static bool OpenFreightRecord(TradeShipment record)
        {
            return record.Status == "Awaiting courier report" || record.Status.StartsWith("Payment unavailable") || record.Status.StartsWith("Payment waiting");
        }
        bool TradeContact(Actor player, Actor other)
        {
            return player != null && other != null && player != other && player.IsPlayer &&
                !player.IsDead && !player.IsSleeping && !other.IsDead && !other.IsSleeping &&
                other.Model.Abilities.IsIntelligent && !other.Model.Abilities.IsUndead &&
                player.Inventory != null && other.Inventory != null && player.Location.Map == other.Location.Map &&
                m_Rules.GridDistance(player.Location.Position, other.Location.Position) <= 1 &&
                !m_Rules.AreEnemies(player, other);
        }
        static CommunityShelterLocation TradePlace(Location location)
        {
            // The geometry of a remembered building may have changed since observation.
            return new CommunityShelterLocation { MapSeed = location.Map.Seed, MapName = location.Map.Name,
                District = location.Map.District == null ? (Point?)null : location.Map.District.WorldPosition, Anchor = location.Position };
        }
        bool TradeUsable(Item item, string resource)
        {
            return item != null && item.Quantity > 0 && !item.IsEquipped &&
                (resource == "food" ? item is ItemFood && !m_Rules.IsFoodSpoiled((ItemFood)item, TradeClock) :
                item is ItemMedicine && ((ItemMedicine)item).Healing > 0);
        }
        List<Item> TradeSurplus(Actor owner)
        {
            var ai = owner.Controller as BaseAI;
            return ai == null || !ai.Directives.CanTrade ? new List<Item>() : owner.Inventory.Items.Where(i =>
                !i.IsEntrustedCargo && TradeSpareUnits(owner, i) > 0).ToList();
        }
        int TradeSpareUnits(Actor owner, Item item)
        {
            if (item.IsEntrustedCargo) return 0;
            if (TradeUsable(item, "food"))
            {
                if (m_Rules.IsActorHungry(owner)) return 0;
                // Count quantities and expiry, not just stack objects. Keep a day of personal nutrition.
                int nutrition = owner.Inventory.Items.Where(i => !i.IsEntrustedCargo && TradeUsable(i, "food"))
                    .Sum(i => i.Quantity * m_Rules.ActorItemNutritionValue(owner, m_Rules.FoodItemNutrition((ItemFood)i, TradeClock)));
                int unit = m_Rules.ActorItemNutritionValue(owner, m_Rules.FoodItemNutrition((ItemFood)item, TradeClock));
                return unit <= 0 ? 0 : Math.Min(item.Quantity, Math.Max(0, (nutrition - m_Rules.ActorMaxFood(owner) / 2) / unit));
            }
            if (TradeUsable(item, "medicine") && owner.HitPoints >= m_Rules.ActorMaxHPs(owner) && owner.Infection == 0)
            {
                int units = owner.Inventory.Items.Where(i => !i.IsEntrustedCargo && TradeUsable(i, "medicine")).Sum(i => i.Quantity);
                return Math.Min(item.Quantity, Math.Max(0, units - 1));
            }
            return 0;
        }
        void TradeRemember(Actor owner, Actor person, string text)
        {
            if (owner.Personality == null) owner.Personality = new PersonalityState();
            owner.Personality.TradeRoutes.Visit(new TradeVisit { Person = person.PersonalityIdentity,
                Name = person.UnmodifiedName, Text = text, Turn = TradeClock, Place = TradePlace(person.Location) });
        }
        List<FreightOffer> FreightOffers(Actor player, Actor sender)
        {
            var offers = new List<FreightOffer>();
            if (!TradeContact(player, sender) || sender.Personality == null ||
                player.Personality != null && player.Personality.HasTradeRoutes && player.Personality.TradeRoutes.Active != null) return offers;
            var state = sender.Personality.TradeRoutes;
            if (state.Issued.Count(OpenFreightRecord) >= 16 ||
                PersonalitySystem.Attitude(sender, player) <= -30) return offers;
            var reliability = sender.Personality.Person(player.PersonalityIdentity);
            if (state.Issued.Any(s => s.Courier == player.PersonalityIdentity && s.Status == "Reported missing cargo") &&
                (reliability == null || reliability.Trust == 0)) return offers;
            var surplus = TradeSurplus(sender);
            int local = sender.Location.Map.LocalTime.TurnCounter;
            foreach (var person in sender.Personality.Knowledge.People)
            {
                if (person.Id == Guid.Empty || person.Id == player.PersonalityIdentity || person.Id == sender.PersonalityIdentity || person.Dead || person.Hostile ||
                    person.Place.Map == null || person.Confidence < 40 || person.Place.Map == sender.Location.Map ||
                    local - person.SeenTurn > 2 * WorldTime.TURNS_PER_DAY || person.SeenTurn > local ||
                    state.Issued.Any(s => s.Recipient == person.Id && TradeClock - s.Created < 2 * WorldTime.TURNS_PER_DAY)) continue;
                var here = sender.Location.Map.District; var there = person.Place.Map.District;
                if (here != null && there != null && Math.Abs(here.WorldPosition.X - there.WorldPosition.X) +
                    Math.Abs(here.WorldPosition.Y - there.WorldPosition.Y) > 1) continue;
                if (PersonalitySystem.Bias(sender, DecisionKind.Compassion) < -10 && CommunityDiplomacySystem.KnownTrust(sender, person.Id) < 10) continue;
                // Read only this sender's remembered need, never the remote recipient's live state.
                foreach (string resource in new[] { "food", "medicine" })
                {
                    int need = resource == "food" ? person.FoodNeed : person.MedicalNeed;
                    int date = resource == "food" ? person.FoodNeedTurn : person.MedicalTurn;
                    int confidence = resource == "food" ? person.FoodConfidence : person.MedicalConfidence;
                    if (need <= 0 || confidence < 40 || date > local || local - date > 2 * WorldTime.TURNS_PER_DAY) continue;
                    Item cargo = surplus.FirstOrDefault(i => TradeUsable(i, resource));
                    Item payment = surplus.FirstOrDefault(i => i != cargo && !TradeUsable(i, resource)) ?? surplus.FirstOrDefault(i => i != cargo);
                    if (cargo == null || payment == null) continue;
                    int units = Math.Min(3, TradeSpareUnits(sender, cargo));
                    // The proposed fee must also be spare after dispatch, though it is not locked away.
                    if (TradeUsable(payment, resource))
                    {
                        if (resource == "medicine") units = Math.Min(units, sender.Inventory.Items.Where(i => !i.IsEntrustedCargo && TradeUsable(i, resource)).Sum(i => i.Quantity) - 2);
                        else
                        {
                            int cargoUnit = m_Rules.ActorItemNutritionValue(sender, m_Rules.FoodItemNutrition((ItemFood)cargo, TradeClock));
                            int paymentUnit = m_Rules.ActorItemNutritionValue(sender, m_Rules.FoodItemNutrition((ItemFood)payment, TradeClock));
                            units = Math.Min(units, TradeSpareUnits(sender, cargo) - (paymentUnit + cargoUnit - 1) / cargoUnit);
                        }
                    }
                    if (units <= 0) continue;
                    offers.Add(new FreightOffer { Cargo = cargo, Payment = payment, Recipient = person, Resource = resource, Units = units });
                    if (offers.Count == 6) return offers;
                }
            }
            return offers;
        }
        static bool SameTradeLot(Item a, Item b)
        {
            if (a.Model != b.Model || a.IsEquipped || b.IsEquipped || a.IsEntrustedCargo || b.IsEntrustedCargo) return false;
            var af = a as ItemFood; var bf = b as ItemFood;
            return af == null || bf != null && af.IsPerishable == bf.IsPerishable &&
                (!af.IsPerishable || af.BestBefore.TurnCounter == bf.BestBefore.TurnCounter);
        }
        static int TradeCapacity(Inventory inventory, Item item, bool entrusted = false)
        {
            if (inventory == null) return 0;
            if (entrusted) return inventory.IsFull ? 0 : item.Quantity;
            return inventory.ReleasedCargoCapacity(item);
        }
        static Item MoveTradeUnits(Inventory from, Inventory to, Item item, int units, TradeCargoTag mark = null)
        {
            if (units <= 0 || !from.Contains(item) || item.Quantity < units) throw new InvalidOperationException("Invalid cargo transfer");
            Item moved;
            if (units == item.Quantity) { from.RemoveAllQuantity(item); moved = item; }
            else moved = item.SplitUnits(units);
            moved.TradeCargo = mark;
            // All callers check capacity before mutation. Preserve expiry when using a full inventory.
            if (!to.IsFull) { to.AddIntact(moved); return moved; }
            foreach (var existing in to.Items.Where(i => SameTradeLot(i, moved)).ToArray())
            {
                int count = Math.Min(moved.Quantity, existing.Model.StackingLimit - existing.Quantity);
                existing.Quantity += count; moved.Quantity -= count;
                if (moved.Quantity == 0) break;
            }
            if (moved.Quantity != 0) throw new InvalidOperationException("Trade capacity changed");
            to.RecordTradeReceipt(units);
            return moved;
        }
        bool AcceptFreight(Actor player, Actor sender, FreightOffer selected)
        {
            var offer = FreightOffers(player, sender).FirstOrDefault(o => o.Recipient.Id == selected.Recipient.Id &&
                o.Resource == selected.Resource && o.Cargo == selected.Cargo && o.Payment == selected.Payment);
            if (offer == null || player.Inventory.IsFull) return false;
            if (player.Personality == null) player.Personality = new PersonalityState();
            var state = player.Personality.TradeRoutes;
            var shipment = new TradeShipment { Id = djack.RogueSurvivor.Engine.SimulationClock.NewIdentity(), Sender = sender.PersonalityIdentity,
                Courier = player.PersonalityIdentity, Recipient = offer.Recipient.Id, SenderName = sender.UnmodifiedName,
                CourierName = player.UnmodifiedName, RecipientName = offer.Recipient.Name, Resource = offer.Resource,
                CargoModel = offer.Cargo.Model.ID, CargoName = offer.Cargo.Model.SingleName, Units = offer.Units,
                PaymentModel = offer.Payment.Model.ID, PaymentName = offer.Payment.Model.SingleName, PaymentUnits = 1,
                Created = TradeClock, Due = TradeClock + 2 * WorldTime.TURNS_PER_DAY,
                Destination = TradePlace(offer.Recipient.Place), Origin = TradePlace(sender.Location) };
            var tag = new TradeCargoTag { Shipment = shipment.Id, Sender = shipment.Sender, Courier = shipment.Courier,
                Recipient = shipment.Recipient, SenderName = shipment.SenderName, RecipientName = shipment.RecipientName };
            MoveTradeUnits(sender.Inventory, player.Inventory, offer.Cargo, shipment.Units, tag);
            state.Active = shipment; state.Cargo = tag;
            var issued = shipment.Copy(); issued.Status = "Awaiting courier report";
            var ledger = sender.Personality.TradeRoutes.Issued;
            while (ledger.Count >= 16) ledger.RemoveAt(ledger.FindIndex(s => !OpenFreightRecord(s)));
            ledger.Add(issued);
            DoSay(sender, player, "Carry " + shipment.Units + " " + shipment.CargoName + " to " + shipment.RecipientName +
                ". Return with their receipt for " + shipment.PaymentUnits + " " + shipment.PaymentName +
                ". Due world turn " + shipment.Due + ". Payment is promised, not reserved. Cargo use or loss leaves a shortfall.", Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
            TradeRemember(player, sender, "Entrusted " + shipment.Units + " " + shipment.CargoName + " for " + shipment.RecipientName + ".");
            SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
        }
        bool HandleEntrustedGift(Actor actor, Actor target, Item cargo)
        {
            if (cargo.TradeCargo == null) return false;
            var state = actor.Personality != null && actor.Personality.HasTradeRoutes ? actor.Personality.TradeRoutes : null;
            var job = state == null ? null : state.Active;
            if (!cargo.IsEntrustedCargo && (job == null || job.Id != cargo.TradeCargo.Shipment ||
                target.PersonalityIdentity != job.Recipient && target.PersonalityIdentity != job.Sender)) return false;
            if (job == null || job.Id != cargo.TradeCargo.Shipment || !TradeContact(actor, target) ||
                !actor.Inventory.Contains(cargo) ||
                target.PersonalityIdentity != job.Recipient && target.PersonalityIdentity != job.Sender)
            { AddMessage(MakeErrorMessage("Entrusted cargo: deliver to its named recipient, return to its sender, or divert it through V / 6.")); return true; }
            bool returning = target.PersonalityIdentity == job.Sender;
            if (!returning && !TradeUsable(cargo, job.Resource))
            { AddMessage(MakeErrorMessage("This cargo is no longer usable. Return it or explain the loss to the sender.")); return true; }
            // Capacity is assessed as released cargo, without changing its marker before commitment.
            var marker = cargo.TradeCargo; cargo.TradeCargo = null;
            int capacity = TradeCapacity(target.Inventory, cargo); cargo.TradeCargo = marker;
            int units = returning ? Math.Min(capacity, cargo.Quantity) :
                Math.Min(Math.Min(capacity, cargo.Quantity), Math.Max(0, job.Units - job.Delivered));
            if (units <= 0) { AddMessage(MakeErrorMessage("No room for this cargo; no delivery was made.")); return true; }
            MoveTradeUnits(actor.Inventory, target.Inventory, cargo, units);
            if (returning) { job.Returned += units; job.Status = "Cargo returned; report to sender"; }
            else
            {
                job.Delivered += units; job.LastReceipt = TradeClock;
                if (TradeClock <= job.Due) job.TimelyDelivered += units;
                job.Status = job.Delivered == job.Units ? "Receipt carried; return for payment" : "Partial receipt carried";
            }
            string receipt = returning ? "Received " + units + " returned units." : "Received " + units + " " + job.CargoName + " from " + job.SenderName + ". Receipt world turn " + TradeClock + ".";
            DoSay(target, actor, receipt, Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
            TradeRemember(actor, target, receipt); TradeRemember(target, actor, receipt);
            if (!returning && m_Session.GamePreset.NpcPersonalitiesEnabled)
            {
                // A courier's actual delivery can fulfill an existing personal supply promise.
                // Returning the sender's own goods never enters this aid/commitment path.
                PersonalitySystem.Report(this, new SignificantEvent(job.Resource == "food" ? "shared_food" : "shared_medicine",
                    actor, target, target.Location.Map, target.Location.Position, target.Location.Map.LocalTime.TurnCounter)
                    { Units = units, Resource = job.Resource });
            }
            SpendActorActionPoints(actor, Rules.BASE_ACTION_COST); return true;
        }
        bool ReportFreight(Actor player, Actor sender, bool waivePayment = false)
        {
            var state = player.Personality == null || !player.Personality.HasTradeRoutes ? null : player.Personality.TradeRoutes;
            var job = state == null ? null : state.Active;
            if (job == null && state != null && TradeContact(player, sender))
            {
                var suspended = state.History.LastOrDefault(s => s.Sender == sender.PersonalityIdentity && s.Status == "Journey suspended; obligation unresolved");
                if (suspended != null) { state.History.Remove(suspended); state.Active = job = suspended; }
            }
            if (job == null || sender.PersonalityIdentity != job.Sender || !TradeContact(player, sender)) return false;
            var issued = sender.Personality.TradeRoutes.Issued.Find(s => s.Id == job.Id);
            if (issued == null) { AddMessage(MakeErrorMessage("The sender no longer retains this agreement.")); return false; }
            if (player.Inventory.Items.Any(i => i.IsEntrustedCargo && i.TradeCargo.Shipment == job.Id))
            { AddMessage(MakeErrorMessage("Return the remaining cargo with G, or explicitly divert it, before reporting the outcome.")); return false; }
            // This is the first time the absent sender learns the receipt; journal viewing cannot do this.
            issued.Delivered = job.Delivered; issued.Returned = job.Returned; issued.LastReceipt = job.LastReceipt;
            issued.TimelyDelivered = job.TimelyDelivered; issued.Reported = job.Reported = TradeClock;
            bool fulfilled = job.TimelyDelivered == job.Units;
            string outcome;
            if (fulfilled && !waivePayment)
            {
                Item payment = TradeSurplus(sender).FirstOrDefault(i => i.Model.ID == job.PaymentModel && i.Quantity >= job.PaymentUnits);
                if (payment == null || TradeCapacity(player.Inventory, payment) < job.PaymentUnits)
                {
                    outcome = payment == null ? "Payment unavailable; the sender still owes the agreed goods." : "Payment waiting; make room in your inventory.";
                    issued.Status = job.Status = outcome; issued.Report = outcome;
                    DoSay(sender, player, outcome, Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
                    SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
                }
                MoveTradeUnits(sender.Inventory, player.Inventory, payment, job.PaymentUnits);
                outcome = "Delivered and paid";
                ReportPersonalityEvent("traded", player, sender, sender.Location.Map, sender.Location.Position);
            }
            else
            {
                outcome = fulfilled ? "Delivery acknowledged; payment waived" :
                    job.Delivered + job.Returned >= job.Units ? "Cargo accounted for; delivery terms not fulfilled" : "Reported missing cargo";
            }
            job.Status = issued.Status = outcome; issued.Report = job.Report = "Delivered " + job.Delivered + ", returned " + job.Returned +
                ", missing " + Math.Max(0, job.Units - job.Delivered - job.Returned) + "; last receipt " + job.LastReceipt + "; reported " + TradeClock + ".";
            if (outcome == "Reported missing cargo")
                sender.Personality.Opinion(player.PersonalityIdentity, player.UnmodifiedName).AdjustSocial(trust: -5, grievance: 5);
            TradeRemember(player, sender, outcome + ". " + job.Report); TradeRemember(sender, player, outcome + ". " + job.Report);
            DoSay(sender, player, outcome + ". " + job.Report, Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
            state.Close(); SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
        }
        bool DivertFreight(Actor player)
        {
            var state = player.Personality == null || !player.Personality.HasTradeRoutes ? null : player.Personality.TradeRoutes;
            if (state == null || state.Active == null || state.Cargo == null || state.Cargo.Released) return false;
            state.Cargo.Released = true;
            state.Active.Status = "Cargo diverted; delivery obligation remains";
            AddMessage(MakeErrorMessage("Cargo released for personal use or trade. Its undelivered quantity remains owed; the sender has not learned this yet."));
            SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
        }
        bool ReplaceFreight(Actor player)
        {
            var state = player.Personality == null || !player.Personality.HasTradeRoutes ? null : player.Personality.TradeRoutes;
            var job = state == null ? null : state.Active;
            if (job == null) return false;
            int carried = player.Inventory.Items.Where(i => i.IsEntrustedCargo && i.TradeCargo.Shipment == job.Id).Sum(i => i.Quantity);
            int missing = Math.Max(0, job.Units - job.Delivered - job.Returned - carried);
            Item replacement = player.Inventory.Items.FirstOrDefault(i => !i.IsEntrustedCargo && i.Model.ID == job.CargoModel && TradeUsable(i, job.Resource));
            if (missing == 0 || replacement == null)
            { AddMessage(MakeErrorMessage("No missing units to replace, or you lack usable personal supplies of the agreed type.")); return false; }
            int units = Math.Min(missing, replacement.Quantity);
            if (units < replacement.Quantity && player.Inventory.IsFull)
            { AddMessage(MakeErrorMessage("Make a free slot for the replacement lot.")); return false; }
            if (state.Cargo == null || state.Cargo.Released)
                state.Cargo = new TradeCargoTag { Shipment = job.Id, Courier = job.Courier, Sender = job.Sender, Recipient = job.Recipient,
                    SenderName = job.SenderName, RecipientName = job.RecipientName };
            if (units < replacement.Quantity)
            { replacement = replacement.SplitUnits(units); player.Inventory.AddIntact(replacement); }
            replacement.TradeCargo = state.Cargo;
            job.Status = "Personal supplies committed as replacement cargo";
            SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
        }
        bool HandleTradeRoutes(Actor player, Actor speaker)
        {
            if (!TradeContact(player, speaker)) return false;
            int choice = ScavengingChoose("Trade / deliveries with " + speaker.UnmodifiedName, new[] {
                "Hear food / medicine trade offers", "Negotiate personal barter", "Ask for a delivery commission",
                "Report delivery / loss and collect payment", "Waive payment and close reported delivery",
                "Commit personal supplies to replace missing cargo", "Divert entrusted cargo (obligation remains)",
                "Tell this person about a remembered neighbour in need" });
            if (choice < 0) return false;
            if (choice == 0)
            {
                var surplus = TradeSurplus(speaker);
                string text = surplus.Count == 0 ? "I have no spare food or healing medicine to offer." :
                    "I could trade " + String.Join(", ", surplus.Take(4).Select(i => TradeSpareUnits(speaker, i) + " " + i.Model.SingleName)) + ". We must agree on an exchange in person.";
                DoSay(speaker, player, text, Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
                TradeRemember(player, speaker, text); SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
            }
            if (choice == 1)
            {
                var ai = speaker.Controller as BaseAI;
                if (ai == null || !ai.Directives.CanTrade || !m_Rules.CanActorInitiateTradeWith(player, speaker))
                { AddMessage(MakeErrorMessage("You cannot initiate a barter with this person now.")); return false; }
                return HandlePlayerTradeNegociation(player, speaker);
            }
            if (choice == 2)
            {
                var offers = FreightOffers(player, speaker);
                if (offers.Count == 0)
                {
                    DoSay(speaker, player, "No commission: I need spare cargo and payment, a reliable courier, and a remembered neighbour in need elsewhere. One shipment at a time.", Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
                    SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
                }
                int selected = ScavengingChoose("Choose a delivery commission", offers.Select(o => o.Units + " " + o.Cargo.Model.SingleName +
                    " for " + o.Recipient.Name + " / " + ScavengingSystem.PlaceLabel(TradePlace(o.Recipient.Place)) +
                    " / need reported local turn " + (o.Resource == "food" ? o.Recipient.FoodNeedTurn : o.Recipient.MedicalTurn) +
                    " / payment: 1 " + o.Payment.Model.SingleName).ToArray());
                if (selected < 0) return false;
                int confirm = ScavengingChoose("Delivery terms", new[] { "Accept: due world turn " + (TradeClock + 2 * WorldTime.TURNS_PER_DAY) +
                    "; return for payment after actual receipt. Payment is not reserved. Cargo use/loss remains your responsibility; contact the sender to resolve it.", "Decline" });
                if (confirm != 0) return false;
                if (AcceptFreight(player, speaker, offers[selected])) return true;
                AddMessage(MakeErrorMessage("The offer changed, or your inventory needs a free slot.")); return false;
            }
            if (choice == 3) return ReportFreight(player, speaker);
            if (choice == 4)
            {
                if (ScavengingChoose("Close without payment?", new[] { "Yes: record the outcome and release the payment claim", "Keep agreement" }) != 0) return false;
                return ReportFreight(player, speaker, true);
            }
            if (choice == 5) return ReplaceFreight(player);
            if (choice == 7) return ShareFreightNeed(player, speaker);
            if (ScavengingChoose("Divert entrusted cargo?", new[] { "Yes: use or trade it personally; undelivered cargo remains owed", "Keep cargo entrusted" }) != 0) return false;
            return DivertFreight(player);
        }
        bool ShareFreightNeed(Actor player, Actor speaker)
        {
            if (player.Personality == null || speaker.Personality == null) return false;
            var reports = player.Personality.Knowledge.People.Where(p => p.Id != speaker.PersonalityIdentity &&
                p.Id != player.PersonalityIdentity && !p.Dead && !p.Hostile && p.Place.Map != null &&
                (p.FoodNeed > 0 && p.FoodConfidence >= 60 || p.MedicalNeed > 0 && p.MedicalConfidence >= 60)).ToArray();
            if (reports.Length == 0)
            { AddMessage(MakeErrorMessage("You have no sufficiently reliable remembered need to report. Meet people and hear their requests.")); return false; }
            int selected = ScavengingChoose("Share a dated need (conditions may have changed)", reports.Select(p => p.Name + " at " +
                ScavengingSystem.PlaceLabel(TradePlace(p.Place)) + " | last seen local turn " + p.SeenTurn).ToArray());
            if (selected < 0) return false;
            var report = reports[selected]; var knowledge = speaker.Personality.Knowledge;
            var old = knowledge.Person(report.Id);
            if (old == null)
            {
                knowledge.LearnPerson(new NpcKnownPerson { Id = report.Id, Name = report.Name, Place = report.Place,
                    SeenTurn = report.SeenTurn, Confidence = Math.Max(0, report.Confidence - 20), Source = NpcKnowledgeSource.Told });
                old = knowledge.Person(report.Id);
            }
            else if (old.SeenTurn < report.SeenTurn || old.SeenTurn == report.SeenTurn && old.Confidence < report.Confidence - 20)
            {
                old.Name = report.Name; old.Place = report.Place; old.SeenTurn = report.SeenTurn;
                old.Confidence = Math.Max(0, report.Confidence - 20); old.Source = NpcKnowledgeSource.Told;
            }
            // Never replace newer/direct evidence with an older, less certain account.
            if (old.FoodNeedTurn < report.FoodNeedTurn || old.FoodNeedTurn == report.FoodNeedTurn && old.FoodConfidence < report.FoodConfidence - 20)
            { old.FoodNeed = report.FoodNeed; old.FoodNeedTurn = report.FoodNeedTurn; old.FoodConfidence = Math.Max(0, report.FoodConfidence - 20); }
            if (old.MedicalTurn < report.MedicalTurn || old.MedicalTurn == report.MedicalTurn && old.MedicalConfidence < report.MedicalConfidence - 20)
            { old.MedicalNeed = report.MedicalNeed; old.MedicalTurn = report.MedicalTurn; old.MedicalConfidence = Math.Max(0, report.MedicalConfidence - 20); }
            DoSay(player, speaker, report.Name + " needed " + (report.FoodNeed > 0 ? "food" : "medicine") + " at " +
                ScavengingSystem.PlaceLabel(TradePlace(report.Place)) + ". My last sighting was local turn " + report.SeenTurn + ". Conditions may have changed.", Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
            TradeRemember(player, speaker, "Shared a dated need for " + report.Name + ".");
            SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
        }
        bool SuspendFreight(Actor player)
        {
            var state = player.Personality == null || !player.Personality.HasTradeRoutes ? null : player.Personality.TradeRoutes;
            if (state == null || state.Active == null) return false;
            if (ScavengingChoose("Suspend this journey?", new[] { "Suspend: cargo becomes available for personal use, but the obligation remains unresolved. Report to the sender later.", "Keep journey active" }) != 0) return false;
            state.Active.Status = "Journey suspended; obligation unresolved";
            state.Close(); SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
        }
        IList<string> TradeJournalLines(Actor player)
        {
            var lines = new List<string> { "Trade circuits: heard offers, carried receipts and reported outcomes. Stock and people may have changed.",
                "V / 6: offers, barter, commissions, reporting. G: actual delivery or return. C: suspend journey with obligation unresolved. Personal barter remains available." };
            if (player.Personality == null || !player.Personality.HasTradeRoutes) { lines.Add("No trade circuit records yet."); return lines; }
            var state = player.Personality.TradeRoutes; var job = state.Active;
            if (job != null)
            {
                AppendJournalLine(lines, "ACTIVE: " + job.Status + " | " + job.SenderName + " -> " + job.RecipientName);
                AppendJournalLine(lines, job.Units + " " + job.CargoName + "; usable units required. Due WORLD turn " + job.Due +
                    (TradeClock > job.Due ? "; deadline passed (timely receipts still count)" : "; now " + TradeClock));
                AppendJournalLine(lines, "Last reported destination: " + ScavengingSystem.PlaceLabel(job.Destination));
                AppendJournalLine(lines, "Sender at departure: " + ScavengingSystem.PlaceLabel(job.Origin));
                AppendJournalLine(lines, "Actual received: " + job.Delivered + " (" + job.TimelyDelivered + " on time); returned " + job.Returned +
                    "; last receipt world turn " + job.LastReceipt + ". Return to report; the sender has no remote receipt.");
                int held = player.Inventory.Items.Where(i => i.IsEntrustedCargo && i.TradeCargo.Shipment == job.Id).Sum(i => i.Quantity);
                AppendJournalLine(lines, "Entrusted units carried by you now: " + held + ". Missing or diverted goods remain your responsibility. Payment: " + job.PaymentUnits + " " + job.PaymentName + ", promised, not reserved.");
            }
            foreach (var visit in state.Visits.AsEnumerable().Reverse())
                AppendJournalLine(lines, "World turn " + visit.Turn + " | " + visit.Name + " at " + ScavengingSystem.PlaceLabel(visit.Place) + ": " + visit.Text);
            foreach (var old in state.History.AsEnumerable().Reverse())
                AppendJournalLine(lines, "Completed: " + old.SenderName + " -> " + old.RecipientName + " | " + old.Status + " | " + old.Report);
            return lines;
        }
    }
}
