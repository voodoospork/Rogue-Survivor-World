using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        bool HandleCommunityRelations(Actor player, Actor leader)
        {
            if (!CommunityDiplomacySystem.WillTalk(this, player, leader)) return false;
            if (player.Personality == null) player.Personality = new PersonalityState();
            var relations = leader.SocialGroup.Diplomacy == null ? new List<CommunityRelation>() : leader.SocialGroup.Diplomacy.Relations;
            var offers = new List<CommunityPeaceOffer>();
            foreach (var carried in player.Personality.PeaceOffers)
                if (carried.Origin == leader.SocialGroup.Identity || carried.Destination == leader.SocialGroup.Identity) offers.Add(carried);
            int selected = 0, packet = 0;
            while (true)
            {
                ClearOverlays();
                CommunityRelation relation = relations.Count == 0 ? null : relations[selected];
                CommunityPeaceOffer offer = offers.Count == 0 ? null : offers[packet];
                var lines = new List<string> { leader.SocialGroup.CommunityName + " - relations / peace", "Left/Right: relationship   Space: carried offer", "1: Hear report   2: Request peace offer   3: Deliver offer   4: Ratify reply   5: Tribute accounts   Esc: cancel" };
                if (relation == null) lines.Add("No relationships recorded by this leader.");
                else
                {
                    var report = CommunityDiplomacySystem.ReportedMessage(leader, relation, m_Session.WorldTime.TurnCounter);
                    lines.Add("Leader's account: " + relation.Target.Name + " - " + report.Stance);
                    lines.Add(report.Cause == null ? "No known declared blood feud." : "Feud cause: " + report.Cause.Explanation + " (turn " + report.Cause.Turn + ")");
                    lines.Add(CommunityDiplomacySystem.PeaceReason(this, leader, relation) ?? "Willing to consider peace.");
                }
                if (offer != null)
                {
                    lines.Add("Carried: " + offer.OriginName + " -> " + offer.DestinationName + " | expires turn " + offer.ExpiresTurn);
                    lines.Add(offer.Ratified ? "Ratified; personal enemies remain." : offer.Answered ? offer.Response : "Authorized offer, awaiting response.");
                }
                foreach (string term in CommunityPeaceOffer.Terms.Split(new[] { ". " }, StringSplitOptions.None)) lines.Add(term);
                var wrapped = new List<string>();
                foreach (string line in lines) {
                    string current = "";
                    foreach (string word in line.Split(' ')) {
                        string candidate = current.Length == 0 ? word : current + " " + word;
                        if (m_UI.UI_BoldTextWidth(candidate) > CANVAS_WIDTH - 40 && current.Length > 0) { wrapped.Add(current); current = word; }
                        else current = candidate;
                    }
                    wrapped.Add(current);
                }
                AddOverlay(new OverlayPopup(wrapped.ToArray(), MODE_TEXTCOLOR, MODE_BORDERCOLOR, MODE_FILLCOLOR, new Point(0, 0)));
                RedrawPlayScreen();
                Keys key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.Escape) { ClearOverlays(); return false; }
                if (key == Keys.Left && relations.Count > 0) { selected = (selected + relations.Count - 1) % relations.Count; continue; }
                if (key == Keys.Right && relations.Count > 0) { selected = (selected + 1) % relations.Count; continue; }
                if (key == Keys.Space && offers.Count > 0) { packet = (packet + 1) % offers.Count; continue; }
                string reason = null; bool completed = false;
                if (key == Keys.D1 || key == Keys.NumPad1)
                {
                    ClearOverlays();
                    if (relation == null) AddCommunitySpeech("We have no recorded community relationships yet.", Color.LightSkyBlue);
                    else
                    {
                        var message = CommunityDiplomacySystem.ReportedMessage(leader, relation, m_Session.WorldTime.TurnCounter);
                        player.Personality.LearnDiplomacy(message, m_Session.WorldTime.TurnCounter, "reported by " + leader.UnmodifiedName, 90);
                        player.Personality.LearnCommunity(message.Community, message.Name, m_Session.WorldTime.TurnCounter, "reported by " + leader.UnmodifiedName);
                        if (message.Target.Group != Guid.Empty) player.Personality.LearnCommunity(message.Target.Group, message.Target.Name, m_Session.WorldTime.TurnCounter, "reported by " + leader.UnmodifiedName);
                        AddCommunitySpeech(message.Name + ": " + message.Stance + " with " + message.Target.Name + ". " + message.Reason + ". Report turn " + message.Turn +
                            (message.PeaceTurn > 0 ? "; peace ratified turn " + message.PeaceTurn : "") + ".", Color.LightSkyBlue);
                    }
                    completed = true;
                }
                else if (key == Keys.D2 || key == Keys.NumPad2)
                {
                    if (relation == null) reason = "No named rival is recorded.";
                    else
                    {
                        ClearOverlays(); AddCommunitySpeech("Carry these peace terms to " + relation.Target.Name + ", then return with their response? Y: accept, N/Esc: cancel.", Color.LightSkyBlue);
                        AddCommunitySpeech(CommunityPeaceOffer.Terms, Color.LightSkyBlue); RedrawPlayScreen();
                        Keys answer; do { answer = m_UI.UI_WaitKey().KeyCode; } while (answer != Keys.Y && answer != Keys.N && answer != Keys.Escape);
                        if (answer != Keys.Y) return false;
                        completed = CommunityDiplomacySystem.Authorize(this, player, leader, relation, out reason) != null;
                    }
                }
                else if (key == Keys.D3 || key == Keys.NumPad3)
                    completed = offer != null && CommunityDiplomacySystem.Deliver(this, player, leader, offer, out reason);
                else if (key == Keys.D4 || key == Keys.NumPad4)
                    completed = offer != null && CommunityDiplomacySystem.Ratify(this, player, leader, offer, out reason);
                else if (key == Keys.D5 || key == Keys.NumPad5)
                { ClearOverlays(); CommunityTributeSystem.Report(this, leader, player); completed = true; }
                else continue;
                ClearOverlays();
                if (!completed) AddCommunitySpeech(reason ?? "No suitable carried offer is selected.", Color.LightSkyBlue);
                SpendActorActionPoints(player, Rules.BASE_ACTION_COST); return true;
            }
        }
    }
}
