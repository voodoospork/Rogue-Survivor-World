using System.Drawing;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay.Personality;
using Message = djack.RogueSurvivor.Data.Message;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        bool HandlePlayerTalk(Actor player)
        {
            RobberySystem.Update(this, player);
            if (RobberySystem.Robber(this, player) != null) return HandleRobberyAnswer(player);
            Actor target = null;
            int nearby = 0;
            for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                Point tile = new Point(player.Location.Position.X + dx, player.Location.Position.Y + dy);
                if (!player.Location.Map.IsInBounds(tile)) continue;
                Actor candidate = player.Location.Map.GetActorAt(tile);
                if (candidate == null || candidate.IsDead || candidate.IsSleeping ||
                    !candidate.Model.Abilities.IsIntelligent || candidate.Model.Abilities.IsUndead ||
                    m_Rules.AreEnemies(player, candidate)) continue;
                target = candidate; nearby++;
            }
            if (nearby == 0) { AddMessage(MakeErrorMessage("No one willing to talk nearby.")); return false; }
            if (nearby != 1)
            {
                ClearOverlays();
                AddOverlay(new OverlayPopup(new[] { "TALK - choose an adjacent person with a direction, ESC cancels" },
                    MODE_TEXTCOLOR, MODE_BORDERCOLOR, MODE_FILLCOLOR, new Point(0, 0)));
                RedrawPlayScreen();
                Direction dir = WaitDirectionOrCancel();
                ClearOverlays();
                if (dir == null || dir == Direction.NEUTRAL) return false;
                Point pos = player.Location.Position + dir;
                target = player.Location.Map.IsInBounds(pos) ? player.Location.Map.GetActorAt(pos) : null;
            }
            if (target == null || target.IsDead || target.IsSleeping || !target.Model.Abilities.IsIntelligent ||
                target.Model.Abilities.IsUndead || m_Rules.AreEnemies(player, target))
            { AddMessage(MakeErrorMessage("No one willing to talk there.")); return false; }
            NpcReaction pending = NpcConversation.Pending(player, target);
            if (pending != null)
            {
                NpcEventDefinition request = NpcContent.Event(pending.Kind);
                if (request == null || request.PlayerReply == null)
                {
                    player.Personality.Reactions.Remove(pending);
                    pending = null;
                }
            }
            bool? answer = null;
            if (pending == null && (CommunityDiplomacySystem.IsLeader(target) || m_Session.GamePreset.NpcPersonalitiesEnabled))
            {
                AddCommunitySpeech("1: News / requests" + (CommunityMembershipSystem.IsLeader(this, target) ? "   2: Membership" : "") +
                    (player.CommunityMembership != null && player.SocialGroup == target.SocialGroup ? "   3: Leave" : "") +
                    (CommunityDiplomacySystem.IsLeader(target) ? "   4: Relations / peace" : "") +
                    (m_Session.GamePreset.NpcPersonalitiesEnabled ? "   5: Supply leads   6: Trade / deliveries   7: Rob" : "") + "   Esc: cancel",
                    Color.LightSkyBlue);
                RedrawPlayScreen();
                while (true)
                {
                    Keys choice = m_UI.UI_WaitKey().KeyCode;
                    if (choice == Keys.Escape) return false;
                    if ((choice == Keys.D4 || choice == Keys.NumPad4) && CommunityDiplomacySystem.IsLeader(target)) return HandleCommunityRelations(player, target);
                    if ((choice == Keys.D5 || choice == Keys.NumPad5) && m_Session.GamePreset.NpcPersonalitiesEnabled) return HandleSupplyConversation(player, target);
                    if ((choice == Keys.D6 || choice == Keys.NumPad6) && m_Session.GamePreset.NpcPersonalitiesEnabled) return HandleTradeRoutes(player, target);
                    if ((choice == Keys.D7 || choice == Keys.NumPad7) && m_Session.GamePreset.NpcPersonalitiesEnabled) return HandlePlayerRobbery(player, target);
                    if (choice == Keys.D1 || choice == Keys.NumPad1 || choice == Keys.Enter) break;
                    if (choice == Keys.D3 || choice == Keys.NumPad3)
                    {
                        if (player.CommunityMembership != null && player.SocialGroup == target.SocialGroup)
                            return ConfirmCommunityDeparture(player);
                        continue;
                    }
                    if ((choice != Keys.D2 && choice != Keys.NumPad2) || !CommunityMembershipSystem.IsLeader(this, target)) continue;
                    string reason;
                    if (!CommunityMembershipSystem.CanJoin(this, player, target, out reason))
                    {
                        DoSay(target, player, reason, Sayflags.IS_FREE_ACTION | Sayflags.IS_BRIEF);
                        SpendActorActionPoints(player, Rules.BASE_ACTION_COST);
                        return true;
                    }
                    AddOverlay(new OverlayPopup(new[] {
                        "Invitation from " + target.UnmodifiedName + " to " + target.SocialGroup.CommunityName,
                        "Share our shelter and communal ground supplies, including weapons.",
                        "Give or trade for personal inventories.",
                        "Help with real requests. Promises keep their deadlines; there is no quota.",
                        target.Community != null ? "I remain chief. You keep your personal follower orders." : "I remain community leader. You keep your personal follower orders.",
                        "Leave with your party whenever you choose, through V or J / Tab / L.",
                        "Y: join together   N: decline   Esc: later"
                    }, MODE_TEXTCOLOR, MODE_BORDERCOLOR, MODE_FILLCOLOR, new Point(0, 0)));
                    RedrawPlayScreen();
                    while (true)
                    {
                        Keys decision = m_UI.UI_WaitKey().KeyCode;
                        if (decision == Keys.Escape) { ClearOverlays(); return false; }
                        if (decision == Keys.N)
                        { ClearOverlays(); DoSay(player, target, "Thank you, but we're staying independent.", Sayflags.IS_BRIEF); return true; }
                        if (decision != Keys.Y) continue;
                        ClearOverlays();
                        var join = new ActionJoinCommunity(player, this, target);
                        if (!join.IsLegal()) { AddMessage(MakeErrorMessage("The invitation is no longer valid.")); return false; }
                        join.Perform(); return true;
                    }
                }
            }
            if (pending != null)
            {
                AddMessage(new Message(target.TheName + (pending.Overheard ? " asked someone for " : " asked you for ") +
                    pending.Text + ". Y: agree, N: decline, 7: Rob, ESC: later.",
                    m_Session.WorldTime.TurnCounter));
                RedrawPlayScreen();
                while (true)
                {
                    Keys key = m_UI.UI_WaitKey().KeyCode;
                    if (key == Keys.D7 || key == Keys.NumPad7) return HandlePlayerRobbery(player, target);
                    if (key == Keys.Y) { answer = true; break; }
                    if (key == Keys.N) { answer = false; break; }
                    if (key == Keys.Escape) return false;
                }
            }
            var action = new ActionPlayerTalk(player, this, target, answer);
            if (!action.IsLegal()) { AddMessage(MakeErrorMessage("You cannot talk to this person now.")); return false; }
            action.Perform();
            return true;
        }
        bool ConfirmCommunityDeparture(Actor player)
        {
            var leave = new ActionLeaveCommunity(player, this);
            if (!leave.IsLegal()) return false;
            AddCommunitySpeech("Leave " + player.CommunityMembership.Name + " with your followers? Promises remain in force. Y: leave, N / Esc: stay.", Color.LightSkyBlue);
            RedrawPlayScreen();
            while (true)
            {
                Keys key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.N || key == Keys.Escape) return false;
                if (key != Keys.Y) continue;
                leave.Perform(); return true;
            }
        }
    }
}
