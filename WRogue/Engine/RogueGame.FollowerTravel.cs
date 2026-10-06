using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;
using Message = djack.RogueSurvivor.Data.Message;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        internal bool FollowerPhoneLink(Actor a, Actor b)
        {
            if (a == null || b == null || a.IsDead || b.IsDead || a.IsSleeping || b.IsSleeping || !AreLinkedByPhone(a, b)) return false;
            return ((Items.ItemTracker)a.GetEquippedItem(DollPart.LEFT_HAND)).Batteries > 0 &&
                ((Items.ItemTracker)b.GetEquippedItem(DollPart.LEFT_HAND)).Batteries > 0;
        }
        internal List<Actor> PersonalFollowers()
        { return BaseAI.TravelParty(m_Player).Where(a => a != m_Player).ToList(); }
        internal bool FollowerVisible(Actor actor)
        { return m_Player != null && !m_Player.IsSleeping && actor.Location.Map == m_Player.Location.Map && IsVisibleToPlayer(actor); }
        internal bool FollowerOrderLink(Actor actor)
        {
            if (m_Player == null || actor == null || actor.IsDead || actor.IsSleeping) return false;
            if (FollowerPhoneLink(m_Player, actor)) return true;
            return actor.Location.Map == m_Player.Location.Map && FollowerVisible(actor) &&
                LOS.ComputeFOVFor(m_Rules, actor, m_Session.WorldTime, m_Session.World.Weather).Contains(m_Player.Location.Position);
        }
        string FollowerCurrentStatus(Actor actor)
        {
            var ai = actor.Controller as BaseAI; var orderable = ai as OrderableAI;
            if (actor.IsDead) return "seen dead";
            if (actor.IsSleeping) return "sleeping";
            if (ai == null) return "not available for orders";
            if (ai.Order != null) return "assigned: " + ai.Order;
            if (orderable != null && orderable.DontFollowLeader) return "following disabled";
            if (m_Rules.IsActorTired(actor)) return "resting: exhausted";
            if (m_Rules.IsActorSleepy(actor)) return "needs sleep";
            return ai.Travel == null ? "following" : ai.Travel.Status;
        }
        internal void RecordFollowerContact(Actor actor, string source, bool approximate = false, string reportedStatus = null)
        {
            if (m_Player == null || actor == m_Player || !actor.InPlayerParty || m_Player.IsSleeping) return;
            string location = approximate ? FollowerVoicePrefix(actor).TrimEnd(':', ' ') : actor.Location.Map.Name +
                " (" + actor.Location.Position.X + "," + actor.Location.Position.Y + ")";
            m_Player.RecordFollower(new FollowerReport { Identity = actor.PersonalityIdentity, Name = actor.UnmodifiedName,
                Status = reportedStatus ?? FollowerCurrentStatus(actor), Location = location, Source = source, Turn = m_Session.WorldTime.TurnCounter });
        }
        void ObservePersonalFollowers()
        {
            if (m_Player == null || m_Player.IsSleeping) return;
            foreach (Actor actor in PersonalFollowers())
                if (FollowerVisible(actor) || FollowerPhoneLink(m_Player, actor))
                    RecordFollowerContact(actor, FollowerVisible(actor) ? "seen by you" : "phone contact");
        }
        internal string DescribeKnownFollower(Actor actor, FollowerReport report)
        {
            if (actor != null && (FollowerVisible(actor) || FollowerPhoneLink(m_Player, actor)))
            { RecordFollowerContact(actor, FollowerVisible(actor) ? "seen by you" : "phone contact"); report = m_Player.FollowerReports.FirstOrDefault(r => r.Identity == actor.PersonalityIdentity); }
            if (report == null) return "last contact unknown";
            return report.Status + "; " + report.Location + "; turn " + report.Turn + " (" + report.Source + ")";
        }
        bool HandlePlayerRecall(Actor player)
        {
            string reason;
            if (!m_Rules.CanActorShout(player, out reason)) { AddMessage(MakeErrorMessage("Cannot recall: " + reason)); return false; }
            DoShout(player, "Followers, come back to me!");
            foreach (Actor follower in PersonalFollowers())
            {
                var ai = follower.Controller as BaseAI;
                if (ai == null || !BaseAI.Travelling(follower) || follower.IsSleeping || follower.IsDead ||
                    follower.Location.Map != player.Location.Map || m_Rules.StdDistance(follower.Location.Position, player.Location.Position) > follower.AudioRange ||
                    !m_Rules.IsActorTrustingLeader(follower) || m_Rules.IsKnownEnemy(follower, player)) continue;
                // The caller is a real heard source. Preserve immediate leadership and assignments.
                ai.HearPersonalRecall(this, player);
            }
            return true;
        }
        internal void FollowerSpeech(Actor speaker, Actor target, Sayflags flags, string text)
        {
            if (speaker == null) return;
            if (speaker.CountFollowers > 0)
                foreach (Actor child in speaker.Followers)
                    if (child.Location.Map == speaker.Location.Map && !child.IsSleeping &&
                        m_Rules.StdDistance(child.Location.Position, speaker.Location.Position) <= child.AudioRange)
                        (child.Controller as BaseAI)?.ContactFollower(this, speaker, "heard leader");
            if ((flags & (Sayflags.IS_FOLLOWER_CALL | Sayflags.IS_FOLLOWER_STATUS)) != 0 && speaker.InPlayerParty && m_Player != null &&
                !m_Player.IsSleeping && speaker.Location.Map == m_Player.Location.Map &&
                m_Rules.StdDistance(speaker.Location.Position, m_Player.Location.Position) <= m_Player.AudioRange)
            {
                string reported = (flags & Sayflags.IS_FOLLOWER_STATUS) != 0 ? "reported: " + (text.Contains("breath") ? "resting from exhaustion" : text.Contains("sleep") ? "needs sleep" : "route blocked") :
                    text == "Coming back!" ? "reported: returning after recall" : "answered location check";
                RecordFollowerContact(speaker, "heard from " + speaker.UnmodifiedName, !FollowerVisible(speaker), FollowerVisible(speaker) ? null : reported);
                if ((flags & Sayflags.IS_FOLLOWER_STATUS) != 0) StopMouseMove();
            }
        }
        List<Actor> UnreadyTravellers(Actor leader, Map fromMap, Point fromPos, Map destination, Point landing)
        {
            var unready = new List<Actor>(); var oldPoints = new Dictionary<Actor, Point> { { leader, fromPos } };
            var landings = new Dictionary<Actor, Point> { { leader, landing } }; var reserved = new HashSet<Point> { landing };
            foreach (Actor actor in BaseAI.TravelParty(leader).Skip(1))
            {
                if (!BaseAI.Travelling(actor)) continue;
                oldPoints[actor] = actor.Location.Position;
                Point parentOld, parentNew;
                if (actor.IsSleeping || actor.Location.Map != fromMap || !oldPoints.TryGetValue(actor.Leader, out parentOld) ||
                    !landings.TryGetValue(actor.Leader, out parentNew) || !m_Rules.IsAdjacent(parentOld, actor.Location.Position))
                { unready.Add(actor); continue; }
                Point? point = FreeFollowerLanding(actor, destination, parentNew, reserved);
                if (!point.HasValue) unready.Add(actor);
                else { landings[actor] = point.Value; reserved.Add(point.Value); }
            }
            return unready;
        }
        Point? FreeFollowerLanding(Actor actor, Map map, Point anchor, HashSet<Point> reserved)
        {
            foreach (Direction direction in Direction.COMPASS)
            {
                Point point = anchor + direction;
                if (map.IsInBounds(point) && !reserved.Contains(point) && m_Rules.IsWalkableFor(actor, map, point.X, point.Y)) return point;
            }
            return null;
        }
        internal Point? PersonalExitLanding(Actor actor, Exit exit)
        {
            if (m_Rules.IsWalkableFor(actor, exit.ToMap, exit.ToPosition.X, exit.ToPosition.Y)) return exit.ToPosition;
            Actor occupant = exit.ToMap.GetActorAt(exit.ToPosition);
            return occupant != null && occupant.InPlayerParty ? FreeFollowerLanding(actor, exit.ToMap, exit.ToPosition, new HashSet<Point>()) : null;
        }
        bool ConfirmFollowerDeparture(Actor player, Map fromMap, Point fromPos, Exit exit, out bool prompted)
        {
            prompted = false;
            var unready = UnreadyTravellers(player, fromMap, fromPos, exit.ToMap, exit.ToPosition);
            if (unready.Count == 0) return true;
            prompted = true; StopMouseMove(); ClearMessages();
            AddMessage(new Message("Following companions are not ready to cross.", m_Session.WorldTime.TurnCounter, Color.Yellow));
            foreach (Actor actor in unready.Take(3))
            {
                bool observed = FollowerVisible(actor) || FollowerPhoneLink(player, actor);
                string problem = !observed ? "" : actor.IsSleeping ? "sleeping; " : actor.Location.Map != fromMap ? "in another area; " :
                    !m_Rules.IsAdjacent(actor.Location.Position, actor.Leader.Location.Position) ? "not beside immediate leader; " : "no clear party landing; ";
                AddMessage(new Message(actor.UnmodifiedName + ": " + problem + DescribeKnownFollower(actor,
                    player.FollowerReports.FirstOrDefault(r => r.Identity == actor.PersonalityIdentity)), m_Session.WorldTime.TurnCounter, Color.Yellow));
            }
            AddMessage(new Message("Y: leave anyway. N/Esc: stay and regroup.", m_Session.WorldTime.TurnCounter, Color.LightGreen));
            RedrawPlayScreen();
            while (true)
            {
                var key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.Y) return true;
                if (key == Keys.N || key == Keys.Escape) return false;
            }
        }
        void InformFollowerTransition(Actor leader, Point exitPoint, Location to)
        {
            if (!leader.InPlayerParty || leader.CountFollowers == 0) return;
            Location from = new Location(leader.Location.Map, exitPoint);
            // Communicate at the source before moving: a hidden exit is not a silent report.
            DoSay(leader, null, "I'm taking this exit to " + to.Map.Name + ".", Sayflags.IS_FREE_ACTION | Sayflags.IS_STORY | Sayflags.IS_BRIEF);
            foreach (Actor child in leader.Followers)
            {
                bool heard = !child.IsSleeping && child.Location.Map == from.Map && m_Rules.StdDistance(child.Location.Position, leader.Location.Position) <= child.AudioRange;
                bool phone = FollowerPhoneLink(child, leader);
                if (heard || phone) (child.Controller as BaseAI)?.LearnFollowerTransition(this, leader, from, to, phone ? "phone exit report" : "heard departure through exit");
            }
        }
        void TransferPersonalFollowers(Actor leader, Map fromMap, Point fromPos, Point exitPoint, Map toMap, Point toPos)
        {
            var original = BaseAI.TravelParty(leader).ToDictionary(a => a, a => a == leader ? new Location(fromMap, fromPos) : a.Location);
            var crossed = new HashSet<Actor> { leader }; var reserved = new HashSet<Point> { toPos };
            foreach (Actor actor in original.Keys.Where(a => a != leader).ToArray())
            {
                if (!BaseAI.Travelling(actor) || actor.IsSleeping || actor.Location.Map != fromMap || !crossed.Contains(actor.Leader) ||
                    !m_Rules.IsAdjacent(original[actor.Leader].Position, original[actor].Position)) continue;
                Point? point = FreeFollowerLanding(actor, toMap, actor.Leader.Location.Position, reserved);
                if (!point.HasValue || !TryActorLeaveTile(actor)) continue;
                InformFollowerTransition(actor, exitPoint, new Location(toMap, point.Value));
                fromMap.RemoveActor(actor); toMap.PlaceActorAt(actor, point.Value); toMap.MoveActorToFirstPosition(actor);
                if (actor.DraggedCorpse != null) { fromMap.RemoveCorpse(actor.DraggedCorpse); toMap.AddCorpseAt(actor.DraggedCorpse, point.Value); }
                crossed.Add(actor); reserved.Add(point.Value); OnActorEnterTile(actor);
                (actor.Controller as BaseAI)?.ContactFollower(this, actor.Leader, "crossed together");
            }
        }
    }
}
