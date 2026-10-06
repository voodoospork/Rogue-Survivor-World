using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        void ObservePlayerSupplies(Actor player)
        {
            if (player == null || !player.IsPlayer || player.IsDead || player.IsSleeping || player.Model.Abilities.IsUndead) return;
            if (player.Personality == null) player.Personality = new PersonalityState();
            var state = player.Personality.Scavenging;
            Map map = player.Location.Map;
            int turn = map.LocalTime.TurnCounter;
            var sites = new HashSet<Zone>();
            foreach (Point point in m_PlayerFOV)
            {
                var location = new Location(map, point);
                Zone zone = Zone.BuildingAt(location);
                if (zone != null && sites.Add(zone) && (zone.BuildingKind == BuildingKind.Grocery || zone.BuildingKind == BuildingKind.Pharmacy))
                    state.Record(zone.BuildingKind == BuildingKind.Grocery ? "food" : "medicine", Zone.BuildingLabel(zone.BuildingKind), true,
                        CommunityShelterLocation.At(location), new SupplyObservation { ObservedTurn = turn, LearnedTurn = turn,
                            Confidence = 100, Source = "seen by you", Confirmed = false });
                Inventory items = map.GetItemsAt(point);
                foreach (string resource in new[] { "food", "medicine" })
                {
                    int units = ScavengingSystem.Count(this, items, resource, turn);
                    bool remembered = state.Leads.Exists(l => !l.SiteOnly && l.Resource == resource && l.Place.Anchor == point && l.Place.Matches(map, point));
                    if (units == 0 && !remembered) continue;
                    XpdBase claim = map.XpdBaseAt(point);
                    var knownClaim = claim == null || claim.GroupLeader == null || claim.GroupLeader.SocialGroup == null ? null :
                        player.Personality.FindCommunity(claim.GroupLeader.SocialGroup.Identity);
                    state.Record(resource, zone == null ? "Ground stock spot" : Zone.BuildingLabel(zone.BuildingKind) + " stock spot", false,
                        CommunityShelterLocation.At(location), new SupplyObservation { ObservedTurn = turn, LearnedTurn = turn,
                            Confidence = 100, Units = units, Confirmed = true, Source = "seen by you", Claimed = claim != null,
                            ClaimName = knownClaim == null ? null : knownClaim.Name });
                }
            }
            var trip = state.Active;
            if (trip != null && trip.ArrivedTurn < 0 && trip.Destination != null && AtTripDestination(player, trip.Destination)) trip.ArrivedTurn = turn;
        }
        static bool AtTripDestination(Actor player, SupplyLead lead)
        {
            var current = CommunityShelterLocation.At(player.Location);
            if (!ScavengingState.SameMap(current, lead.Place)) return false;
            return lead.SiteOnly && lead.Place.BuildingBounds.HasValue ? lead.Place.BuildingBounds.Value.Contains(current.Anchor) :
                Math.Max(Math.Abs(current.Anchor.X - lead.Place.Anchor.X), Math.Abs(current.Anchor.Y - lead.Place.Anchor.Y)) <= 1;
        }
        bool HandleSupplyConversation(Actor player, Actor speaker)
        {
            if (!m_Session.GamePreset.NpcPersonalitiesEnabled || NpcConversation.Pending(player, speaker) != null) return false;
            DoSay(player, speaker, "Any leads on food or healing medicine?", Sayflags.IS_FREE_ACTION | Sayflags.IS_BRIEF);
            var reports = ScavengingSystem.Reports(speaker, player);
            if (reports.Count == 0) DoSay(speaker, player, "I have no new supply leads to share.", Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY);
            foreach (var report in reports) ScavengingSystem.Share(this, speaker, player, report);
            SpendActorActionPoints(player, Rules.BASE_ACTION_COST);
            return true;
        }
        List<SupplyLead> ScavengingLeads(Actor player, int filter)
        {
            if (player.Personality == null || !player.Personality.HasScavenging) return new List<SupplyLead>();
            return player.Personality.Scavenging.Leads.Where(l => filter == 0 || l.Resource == (filter == 1 ? "food" : "medicine"))
                .OrderByDescending(l => l.Latest == null ? -1 : l.Latest.ObservedTurn).ThenBy(l => l.Resource).ToList();
        }
        static string SupplySummary(SupplyLead lead)
        {
            var last = lead.Latest;
            return lead.Resource + " | " + lead.Label + " | " + (last == null || !last.Confirmed ? "stock unconfirmed" :
                last.Units + " last observed units at this spot") + " | observed turn " + (last == null ? 0 : last.ObservedTurn) +
                (last != null && last.Claimed ? " | CLAIMED" : "");
        }
        static string PromiseStatus(Actor player, ScavengingTrip trip)
        {
            if (trip.CommitmentId == 0) return "Personal trip";
            var promise = player.Personality == null || !player.Personality.HasCommitments ? null :
                player.Personality.Commitments.FirstOrDefault(c => c.Id == trip.CommitmentId && c.Promisor == player.PersonalityIdentity);
            return "Promise to " + trip.Beneficiary + " | due turn " + trip.Deadline + " | " +
                (promise == null ? "last known commitment no longer retained" : promise.Status.ToString() + ", remaining " + promise.Units + " units") +
                ". Only actual delivery fulfills it.";
        }
        IList<string> ScavengingJournalLines(Actor player, int filter, int selected)
        {
            var lines = new List<string> { "Dated supply knowledge. A spot is not the whole building; stock may have changed." };
            ScavengingState state = player.Personality == null || !player.Personality.HasScavenging ? null : player.Personality.Scavenging;
            if (state != null && state.Active != null)
            {
                var trip = state.Active;
                AppendJournalLine(lines, "ACTIVE: " + trip.Resource + " | departed turn " + trip.DepartedTurn + " | " +
                    (trip.ArrivedTurn < 0 ? "destination not yet reached" : "arrived turn " + trip.ArrivedTurn));
                AppendJournalLine(lines, PromiseStatus(player, trip));
                AppendJournalLine(lines, "Destination: " + (trip.Destination == null ? "open search" : SupplySummary(trip.Destination) + " / " + ScavengingSystem.PlaceLabel(trip.Destination.Place)));
                AppendJournalLine(lines, "Return: " + ScavengingSystem.PlaceLabel(trip.Return));
                AppendJournalLine(lines, "Departure carried: food " + trip.DepartureFood + ", healing medicine " + trip.DepartureMedicine + ". E: finish, C: cancel, R: change return here.");
            }
            else lines.Add("No active trip. N: plan a personal trip or an accepted request.");
            lines.Add("Filter: " + (filter == 0 ? "All" : filter == 1 ? "Food" : "Medicine"));
            var leads = ScavengingLeads(player, filter);
            if (leads.Count == 0) lines.Add("No matching leads. Explore stores or use V / 5: Supply leads.");
            for (int i = 0; i < leads.Count; i++)
                AppendJournalLine(lines, (i == selected ? "> " : "  ") + (i + 1) + ". " + SupplySummary(leads[i]) + " / " + ScavengingSystem.PlaceLabel(leads[i].Place));
            if (state != null && state.Completed.Count > 0)
            {
                lines.Add("Completed trips (latest first)");
                foreach (var trip in state.Completed.AsEnumerable().Reverse())
                {
                    AppendJournalLine(lines, "Turn " + trip.DepartedTurn + " -> " + trip.CompletedTurn + " | " + trip.Outcome +
                        " | arrived " + (trip.ArrivedTurn < 0 ? "not recorded" : trip.ArrivedTurn.ToString()) + " | carried change: food " +
                        (trip.FinalFood - trip.DepartureFood).ToString("+0;-0;0") + ", healing medicine " + (trip.FinalMedicine - trip.DepartureMedicine).ToString("+0;-0;0"));
                    AppendJournalLine(lines, PromiseStatus(player, trip));
                }
            }
            return lines;
        }
        void ScavengingDetails(SupplyLead lead)
        {
            var lines = new List<string> { lead.Resource + " / " + lead.Label, ScavengingSystem.PlaceLabel(lead.Place),
                lead.SiteOnly ? "Possible source; stock unconfirmed." : "Exact observed spot. Other shelves and rooms remain unconfirmed." };
            foreach (var o in lead.Observations.AsEnumerable().Reverse())
            {
                lines.Add("Observed turn " + o.ObservedTurn + " | learned turn " + o.LearnedTurn + " | " + o.Source + " | confidence " + o.Confidence + "%");
                lines.Add(o.Confirmed ? o.Units + " last observed usable units" : "stock unconfirmed");
                if (o.Claimed) lines.Add("Claimed supplies" + (o.ClaimName == null ? "" : " by " + o.ClaimName) + ": taking them may provoke the owners.");
            }
            ScavengingChoose("Supply lead details", lines, false);
        }
        int ScavengingChoose(string title, IList<string> entries, bool choose = true)
        {
            int selected = 0;
            while (true)
            {
                m_UI.UI_Clear(Color.Black); DrawHeader();
                m_UI.UI_DrawStringBold(Color.Yellow, title, 0, BOLD_LINE_SPACING);
                int y = 3 * BOLD_LINE_SPACING, start = choose ? selected / 10 * 10 : 0;
                for (int i = start; i < entries.Count && i < start + (choose ? 10 : 24); i++)
                {
                    var wrapped = new List<string>(); AppendJournalLine(wrapped, (choose && i == selected ? "> " : "  ") + entries[i]);
                    foreach (string line in wrapped) { m_UI.UI_DrawStringBold(i == selected && choose ? Color.LightGreen : Color.White, line, 0, y); y += BOLD_LINE_SPACING; }
                }
                DrawFootnote(Color.White, choose ? "Up/Down: select, Enter: choose, Esc: cancel" : "Enter / Esc: back"); m_UI.UI_Repaint();
                Keys key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.Escape) return -1;
                if (key == Keys.Enter) return selected;
                if (key == Keys.Up && selected > 0) selected--;
                if (key == Keys.Down && selected + 1 < entries.Count) selected++;
            }
        }
        void PlanScavengingTrip(Actor player, SupplyLead selected)
        {
            if (player.Personality == null) player.Personality = new PersonalityState();
            var state = player.Personality.Scavenging;
            if (state.Active != null) { ScavengingChoose("Finish or cancel your active trip first", new[] { "One expedition can be active at a time." }, false); return; }
            var jobs = player.Personality.HasCommitments ? player.Personality.Commitments.Where(c => c.Promisor == player.PersonalityIdentity &&
                c.Status == NpcCommitmentStatus.Active && c.DueTurn > player.Location.Map.LocalTime.TurnCounter &&
                (c.Resource == "food" || c.Resource == "medicine")).ToList() : new List<NpcCommitment>();
            var options = new List<string> { "Personal trip: food", "Personal trip: healing medicine" };
            foreach (var job in jobs) options.Add("Request: " + job.Units + " " + job.Resource + " for " + job.BeneficiaryName + " / due turn " + job.DueTurn);
            int choice = ScavengingChoose("Plan expedition", options);
            if (choice < 0) return;
            NpcCommitment promise = choice < 2 ? null : jobs[choice - 2];
            string resource = promise == null ? (choice == 0 ? "food" : "medicine") : promise.Resource;
            var destinations = ScavengingLeads(player, resource == "food" ? 1 : 2);
            if (selected != null && selected.Resource == resource) { destinations.Remove(selected); destinations.Insert(0, selected); }
            var locations = new List<string> { "Open search: no reported destination" };
            locations.AddRange(destinations.Select(l => SupplySummary(l) + " / " + ScavengingSystem.PlaceLabel(l.Place)));
            int destination = ScavengingChoose("Choose destination", locations);
            if (destination < 0) return;
            int turn = player.Location.Map.LocalTime.TurnCounter;
            int food = ScavengingSystem.Count(this, player.Inventory, "food", turn), med = ScavengingSystem.Count(this, player.Inventory, "medicine", turn);
            var prep = new List<string> { "Usable carried food: " + food + " / healing medicine: " + med,
                "Free inventory slots: " + (player.Inventory.MaxCapacity - player.Inventory.CountItems),
                "Hunger: " + (Rules.IsActorHungry(player) ? "hungry" : "not hungry") + " / sleep: " + (Rules.IsActorSleepy(player) ? "sleepy" : "not sleepy"),
                "Return defaults to your current position. R changes it later.",
                promise == null ? "Personal expedition." : "For " + promise.BeneficiaryName + " / due turn " + promise.DueTurn + ". Delivery through G fulfills the promise.",
                "Enter: record departure / Esc: cancel planning. Planning is free." };
            if (ScavengingChoose("Preparation", prep, false) < 0) return;
            state.Active = new ScavengingTrip { Resource = resource, DepartedTurn = turn, DepartureFood = food, DepartureMedicine = med,
                Return = CommunityShelterLocation.At(player.Location), Destination = destination == 0 ? null : destinations[destination - 1].Copy(),
                CommitmentId = promise == null ? 0 : promise.Id, Beneficiary = promise == null ? null : promise.BeneficiaryName, Deadline = promise == null ? 0 : promise.DueTurn };
            if (state.Active.Destination != null && AtTripDestination(player, state.Active.Destination)) state.Active.ArrivedTurn = turn;
        }
        void FinishScavengingTrip(Actor player, bool cancel)
        {
            if (player.Personality == null || !player.Personality.HasScavenging || player.Personality.Scavenging.Active == null) return;
            var state = player.Personality.Scavenging; var trip = state.Active;
            var current = CommunityShelterLocation.At(player.Location);
            bool returned = ScavengingState.SameMap(current, trip.Return) && Rules.GridDistance(current.Anchor, trip.Return.Anchor) <= 1;
            string outcome = cancel ? "cancelled" : returned ? "returned to planned point" : "ended away from planned return";
            AddCommunitySpeech("Record trip as " + outcome + "? Promises remain in force. Y: record, N / Esc: keep active.", Color.LightSkyBlue);
            RedrawPlayScreen();
            while (true)
            {
                Keys key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.N || key == Keys.Escape) return;
                if (key != Keys.Y) continue;
                trip.CompletedTurn = player.Location.Map.LocalTime.TurnCounter; trip.Cancelled = cancel; trip.Returned = !cancel && returned; trip.Outcome = outcome;
                trip.FinalFood = ScavengingSystem.Count(this, player.Inventory, "food", trip.CompletedTurn);
                trip.FinalMedicine = ScavengingSystem.Count(this, player.Inventory, "medicine", trip.CompletedTurn);
                state.Complete(trip); return;
            }
        }
    }
}
