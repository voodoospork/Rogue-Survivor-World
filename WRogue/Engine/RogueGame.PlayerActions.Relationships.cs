using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        static string FeelingLabel(int feeling)
        {
            if (feeling >= 25) return "friendly";
            if (feeling > 0) return "warm";
            if (feeling <= -25) return "hostile";
            if (feeling < 0) return "wary";
            return "neutral";
        }

        IList<string> RelationshipLines(Actor player)
        {
            List<string> lines = new List<string>();
            PersonalityState state = player == null ? null : player.Personality;
            if (state == null)
            {
                lines.Add("No relationships recorded yet.");
                return lines;
            }
            AppendRelationships(lines, "People", state.People, "");
            AppendRelationships(lines, "Groups", state.Groups, "'s group");
            AppendRelationships(lines, "Factions", state.Factions, "");
            if (lines.Count == 0) lines.Add("No relationships recorded yet.");
            return lines;
        }

        static void AppendRelationships(List<string> lines, string heading,
            ICollection<RelationshipRecord> records, string suffix)
        {
            if (records.Count == 0) return;
            List<RelationshipRecord> sorted = new List<RelationshipRecord>(records);
            sorted.Sort((a, b) => String.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            if (lines.Count > 0) lines.Add("");
            lines.Add(heading);
            foreach (RelationshipRecord record in sorted)
                lines.Add(String.Format("  {0}{1}: {2}{3}", record.Name, suffix,
                    FeelingLabel(record.Feeling), record.Trust > 0 || record.Fear > 0 || record.Grievance > 0 ?
                    String.Format(" (trust {0}, fear {1}, grievance {2})", record.Trust, record.Fear, record.Grievance) : ""));
        }

        void HandleRelationships()
        {
            IList<string> lines = RelationshipLines(m_Player);
            const int linesPerPage = 45;
            int page = 0;
            bool done = false;
            while (!done)
            {
                m_UI.UI_Clear(Color.Black);
                DrawHeader();
                int y = BOLD_LINE_SPACING;
                m_UI.UI_DrawStringBold(Color.Yellow, "Your relationships", 0, y);
                y += 2 * BOLD_LINE_SPACING;
                for (int i = page * linesPerPage; i < lines.Count && i < (page + 1) * linesPerPage; i++)
                {
                    m_UI.UI_DrawStringBold(Color.White, lines[i], 0, y);
                    y += BOLD_LINE_SPACING;
                }
                DrawFootnote(Color.White, "PgUp/PgDn to move, ESC to leave");
                m_UI.UI_Repaint();
                Keys key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.Escape) done = true;
                else if (key == Keys.PageDown && (page + 1) * linesPerPage < lines.Count) page++;
                else if (key == Keys.PageUp && page > 0) page--;
            }
        }

        IList<string> HeardJournalLines(Actor player)
        {
            List<string> lines = new List<string>();
            if (player != null && player.Personality != null)
                for (int i = player.Personality.HeardJournal.Count - 1; i >= 0; i--)
                {
                    HeardJournalEntry heard = player.Personality.HeardJournal[i];
                    AppendJournalLine(lines, String.Format("Turn {0} | {1} | {2}: \"{3}\"", heard.Turn,
                        heard.Kind == "heard_rumor" ? "Rumor" : heard.Kind == "heard_request" ? "Request" : "Reply",
                        heard.Speaker, heard.Text));
                }
            if (lines.Count == 0) lines.Add("You have not heard any rumors or requests yet.");
            return lines;
        }

        static void AppendJournalLine(List<string> lines, string line)
        {
            const int width = 100;
            while (line.Length > width)
            {
                int split = line.LastIndexOf(' ', width);
                if (split < 20) split = width;
                lines.Add(line.Substring(0, split));
                line = "    " + line.Substring(split).TrimStart();
            }
            lines.Add(line);
        }

        IList<string> CommunityJournalLines(Actor player)
        {
            List<string> lines = new List<string>();
            if (player != null && player.Personality != null)
            {
                if (player.CommunityMembership != null)
                {
                    PlayerCommunityMembership membership = player.CommunityMembership;
                    AppendJournalLine(lines, "Member of " + membership.Name + " | accepted turn " + membership.JoinedTurn + " | invited by " + membership.AcceptedBy);
                    AppendJournalLine(lines, PlayerCommunityMembership.Terms);
                    lines.Add("L: Leave with your followers (one action). No scheduled duties or contribution quota.");
                }
                if (player.Personality.HasCommitments)
                    foreach (NpcCommitment promise in player.Personality.Commitments)
                        if (promise.Promisor == player.PersonalityIdentity && promise.Status == NpcCommitmentStatus.Active)
                            AppendJournalLine(lines, "Personal promise: " + promise.Units + " " + promise.Resource + " to " + promise.BeneficiaryName +
                                " | due turn " + promise.DueTurn + " | promised by you");
                foreach (CommunityPeaceOffer offer in player.Personality.PeaceOffers)
                    AppendJournalLine(lines, "Carried peace terms: " + offer.OriginName + " -> " + offer.DestinationName +
                        " | authorized turn " + offer.CreatedTurn + " | expires turn " + offer.ExpiresTurn + " | " +
                        (offer.Ratified ? "ratified; personal enemies remain" : offer.ExpiresTurn < m_Session.WorldTime.TurnCounter ? "expired" : offer.Answered ? offer.Response : "awaiting response"));
                foreach (KnownCommunity community in player.Personality.Communities)
                {
                    if (lines.Count > 0) lines.Add("");
                    AppendJournalLine(lines, community.Name + " | learned turn " + community.NameTurn + " | " + community.NameSource);
                    foreach (KnownDiplomacy report in player.Personality.KnownDiplomacy)
                        if (report.Message.Community == community.Identity)
                            AppendJournalLine(lines, "  Last reported relation: " + report.Message.Target.Name + " - " + report.Message.Stance +
                                " | Turn " + report.Message.Turn + " | " + report.Source + " (heard turn " + report.LearnedTurn + ", confidence " + report.Confidence + "%) | " + report.Message.Reason +
                                (report.Message.PeaceTurn == 0 ? "" : " | peace ratified turn " + report.Message.PeaceTurn + "; personal enemies remain"));
                    string title = community.IsTribe ? "Chief" : "Leader";
                    AppendJournalLine(lines, community.Leader == null ? "  " + title + ": unknown" :
                        "  " + title + ": " + community.Leader + " | Turn " + community.LeaderTurn + " | " + community.LeaderSource);
                    AppendJournalLine(lines, community.Shelter == null ? "  Shelter: unknown" :
                        "  Shelter: " + community.Shelter + " | Turn " + community.ShelterTurn + " | " + community.ShelterSource);
                    if (community.Members.Count == 0) lines.Add("  Members: unknown");
                    foreach (CommunityMember member in community.Members)
                        AppendJournalLine(lines, "  " + member.Name + ": " + member.Status + " | Turn " + member.Turn + " | " + member.Source);
                    for (int i = community.Notes.Count - 1; i >= 0; i--)
                    {
                        CommunityNote note = community.Notes[i];
                        AppendJournalLine(lines, "  Turn " + note.EventTurn + " | " + note.Source +
                            (note.Reported ? " (heard turn " + note.LearnedTurn + ")" : "") + " | " + note.Text);
                    }
                }
            }
            if (lines.Count == 0) lines.Add("No communities known yet. Talk to survivors with " + s_KeyBindings.Get(PlayerCommand.TALK) + ".");
            lines.Insert(0, "Last known information. Dates are observations or reports, not a live census.");
            return lines;
        }

        bool HandleHeardJournal()
        {
            IList<string> lines = HeardJournalLines(m_Player);
            var placeColors = new RecordsTextColors(new ResidentRecord[0]);
            const int linesPerPage = 35;
            int page = 0;
            int view = 0, filter = 0, selected = 0;
            while (true)
            {
                m_UI.UI_Clear(Color.Black);
                DrawHeader();
                int y = BOLD_LINE_SPACING;
                m_UI.UI_DrawStringBold(Color.Yellow, view == 3 ? "Trade circuits" : view == 2 ? "Scavenging" : view == 1 ? "Communities" : "What you heard", 0, y);
                y += 2 * BOLD_LINE_SPACING;
                for (int i = page * linesPerPage; i < lines.Count && i < (page + 1) * linesPerPage; i++)
                {
                    DrawRecordsText(lines[i], 0, y, Color.White, placeColors);
                    y += BOLD_LINE_SPACING;
                }
                DrawFootnote(Color.White, view == 2 ? "Tab: next view | Up/Down: lead | Enter: details | F: filter | N: plan | E: finish | C: cancel | R: return here | Esc" :
                    "Tab: heard / communities / scavenging / trade, PgUp/PgDn: page, ESC: close" +
                    (view == 1 && m_Player.CommunityMembership != null ? ", L: leave community" : ""));
                m_UI.UI_Repaint();
                Keys key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.Escape) return false;
                if (view == 3 && key == Keys.C && SuspendFreight(m_Player)) return true;
                if (key == Keys.L && view == 1 && m_Player.CommunityMembership != null && ConfirmCommunityDeparture(m_Player)) return true;
                if (key == Keys.Tab) { view = (view + 1) % 4; page = 0;
                    lines = view == 3 ? TradeJournalLines(m_Player) : view == 2 ? ScavengingJournalLines(m_Player, filter, selected) : view == 1 ? CommunityJournalLines(m_Player) : HeardJournalLines(m_Player); }
                if (view == 2 && key != Keys.Tab)
                {
                    var leads = ScavengingLeads(m_Player, filter);
                    if (key == Keys.F) { filter = (filter + 1) % 3; selected = 0; page = 0; }
                    if (key == Keys.Up && selected > 0) selected--;
                    if (key == Keys.Down && selected + 1 < leads.Count) selected++;
                    if (key == Keys.Enter && selected < leads.Count) ScavengingDetails(leads[selected]);
                    if (key == Keys.N) PlanScavengingTrip(m_Player, selected < leads.Count ? leads[selected] : null);
                    if (key == Keys.E || key == Keys.C) FinishScavengingTrip(m_Player, key == Keys.C);
                    if (key == Keys.R && m_Player.Personality != null && m_Player.Personality.HasScavenging && m_Player.Personality.Scavenging.Active != null)
                        m_Player.Personality.Scavenging.Active.Return = CommunityShelterLocation.At(m_Player.Location);
                    lines = ScavengingJournalLines(m_Player, filter, selected);
                    if (key == Keys.Up || key == Keys.Down) { int index = 0; while (index < lines.Count && !lines[index].StartsWith("> ")) index++; page = index / linesPerPage; }
                }
                if (key == Keys.PageDown && (page + 1) * linesPerPage < lines.Count) page++;
                if (key == Keys.PageUp && page > 0) page--;
            }
        }
    }
}
