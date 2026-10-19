using System;
using System.Collections.Generic;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static partial class CommunityDiplomacySystem
    {
        public static int PeaceScore(Actor leader, CommunityRelation relation)
        { return 30 + PersonalitySystem.Bias(leader, DecisionKind.Compassion) + PersonalitySystem.Bias(leader, DecisionKind.Law) / 2 -
            PersonalitySystem.Bias(leader, DecisionKind.Group) / 2 - relation.Grievance / 2 + CommunityCultureSystem.PeaceModifier(leader); }
        public static bool WillTalk(RogueGame game, Actor player, Actor leader)
        {
            return IsLeader(leader) && player != null && !player.IsDead && !player.IsSleeping && !leader.IsSleeping &&
                player.Location.Map == leader.Location.Map && game.Rules.GridDistance(player.Location.Position, leader.Location.Position) <= 1 &&
                !game.Rules.IsKnownEnemy(player, leader) && !game.Rules.IsKnownEnemy(leader, player);
        }
        public static string PeaceReason(RogueGame game, Actor leader, CommunityRelation relation)
        {
            if (relation.Target.Group == Guid.Empty) return "A named community must authorize an agreement.";
            if (game.Session.WorldTime.TurnCounter - relation.LastViolenceLearnedTurn < 180) return "We need 180 turns without newly reported violence.";
            if (PeaceScore(leader, relation) < 0) return "Our grievance is too strong. Useful aid or real restitution may help.";
            return null;
        }
        static long LastCause(CommunityRelation relation)
        { long last = relation.ReviewedCause; foreach (var incident in relation.Incidents) last = Math.Max(last, incident.EventId); return last; }
        public static bool InDispute(Actor leader, CommunityRelation relation)
        {
            if (ReportedMessage(leader, relation, 0).Feud) return true;
            KnownDiplomacy incoming = leader.Personality.FindDiplomacy(relation.Target.Group, PoliticalIdentity.Of(leader.SocialGroup));
            return incoming != null && incoming.Message.Feud;
        }
        public static CommunityPeaceOffer Authorize(RogueGame game, Actor player, Actor leader, CommunityRelation relation, out string reason)
        {
            reason = !WillTalk(game, player, leader) ? "Both parties must be willing to talk nearby." :
                !InDispute(leader, relation) ? "We have no known blood feud with that group." : PeaceReason(game, leader, relation);
            if (reason != null) return null;
            game.RememberDiplomacyGroup(leader.SocialGroup);
            if (player.SocialGroup != null && (player.SocialGroup.Identity == relation.Target.Group || player.SocialGroup.Identity == leader.SocialGroup.Identity))
            { reason = "We need a neutral messenger outside both communities."; return null; }
            var offer = new CommunityPeaceOffer { Id = djack.RogueSurvivor.Engine.SimulationClock.NewIdentity(), Origin = leader.SocialGroup.Identity, Destination = relation.Target.Group,
                OriginName = leader.SocialGroup.CommunityName, DestinationName = relation.Target.Name, AuthorizedBy = leader.PersonalityIdentity,
                CreatedTurn = game.Session.WorldTime.TurnCounter, ExpiresTurn = game.Session.WorldTime.TurnCounter + 2 * WorldTime.TURNS_PER_DAY,
                OriginRevision = relation.Revision, OriginCause = LastCause(relation) };
            relation.Pending = offer.Copy();
            if (player.Personality == null) player.Personality = new PersonalityState();
            player.Personality.PeaceOffers.Add(offer);
            if (player.Personality.PeaceOffers.Count > 8) player.Personality.PeaceOffers.RemoveAt(0);
            RecordExchange(game, player, leader, "Authorized a peace offer to " + offer.DestinationName + "; expires turn " + offer.ExpiresTurn + ". " + CommunityPeaceOffer.Terms);
            return offer;
        }
        public static bool Deliver(RogueGame game, Actor player, Actor leader, CommunityPeaceOffer offer, out string reason)
        {
            reason = !WillTalk(game, player, leader) ? "Both parties must be willing to talk nearby." :
                offer.Answered || offer.Ratified ? "That offer already has a response." :
                offer.ExpiresTurn < game.Session.WorldTime.TurnCounter ? "The offer has expired." :
                offer.Destination != leader.SocialGroup.Identity ? "These terms are addressed to a different group." :
                player.SocialGroup != null && (player.SocialGroup.Identity == offer.Origin || player.SocialGroup.Identity == offer.Destination) ? "We need a neutral messenger." : null;
            if (reason != null) return false;
            game.RememberDiplomacyGroup(leader.SocialGroup);
            if (leader.SocialGroup.Diplomacy == null) leader.SocialGroup.Diplomacy = new CommunityDiplomacyState();
            CommunityRelation relation = leader.SocialGroup.Diplomacy.Get(new PoliticalIdentity { Group = offer.Origin, Faction = -1, Name = offer.OriginName });
            offer.Answered = true; offer.ResponseTurn = game.Session.WorldTime.TurnCounter;
            offer.Accepted = PeaceReason(game, leader, relation) == null;
            offer.Response = offer.Accepted ? "Accepted, pending ratification by the originating leader." : PeaceReason(game, leader, relation);
            offer.RespondedBy = leader.PersonalityIdentity; offer.DestinationRevision = relation.Revision; offer.DestinationCause = LastCause(relation);
            relation.Pending = offer.Copy();
            RecordExchange(game, player, leader, "Response to " + offer.OriginName + ": " + offer.Response + " Return before turn " + offer.ExpiresTurn + ".");
            return true;
        }
        public static bool Ratify(RogueGame game, Actor player, Actor leader, CommunityPeaceOffer offer, out string reason)
        {
            reason = !WillTalk(game, player, leader) ? "Both parties must be willing to talk nearby." :
                offer.Origin != leader.SocialGroup.Identity ? "Return this response to its originating community." :
                !offer.Answered || !offer.Accepted ? "There is no accepted response to ratify." :
                offer.Ratified ? "This peace has already been ratified." :
                offer.ExpiresTurn < game.Session.WorldTime.TurnCounter ? "The terms have expired." :
                player.SocialGroup != null && (player.SocialGroup.Identity == offer.Origin || player.SocialGroup.Identity == offer.Destination) ? "We need a neutral messenger outside both communities." : null;
            if (reason != null) return false;
            var relation = leader.SocialGroup.Diplomacy == null ? null : leader.SocialGroup.Diplomacy.Find(new PoliticalIdentity { Group = offer.Destination });
            if (relation == null || relation.Pending == null || relation.Pending.Id != offer.Id)
            { reason = "We have no matching authorized offer."; return false; }
            if (relation.LastViolenceLearnedTurn > offer.CreatedTurn)
            { reason = "We learned of fresh violence after authorizing those terms. Bring a new offer."; return false; }
            reason = PeaceReason(game, leader, relation); if (reason != null) return false;
            offer.Ratified = true;
            EndFeud(relation, LastCause(relation), game.Session.WorldTime.TurnCounter); relation.Pending = offer.Copy();
            SocialGroup other = game.FindDiplomacyGroup(offer.Destination);
            if (other != null && other.Diplomacy != null)
            {
                CommunityRelation reverse = other.Diplomacy.Find(PoliticalIdentity.Of(leader.SocialGroup));
                if (reverse != null && reverse.Pending != null && reverse.Pending.Id == offer.Id)
                {
                    EndFeud(reverse, offer.DestinationCause, game.Session.WorldTime.TurnCounter); reverse.Pending = offer.Copy();
                    // End the signed declaration, preserving later incidents for another review.
                }
            }
            RecordExchange(game, player, leader, "Peace ratified with " + offer.DestinationName + ". " + CommunityPeaceOffer.Terms);
            game.DoSay(leader, null, "We, " + leader.SocialGroup.CommunityName + ", have ratified peace with " + offer.DestinationName +
                ". Both declarations end. " + CommunityPeaceOffer.Terms, RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FREE_ACTION);
            Publish(game, leader, Message(leader.SocialGroup, relation, game.Session.WorldTime.TurnCounter), "community_peace");
            if (other != null && other.Diplomacy != null)
            {
                var reverse = other.Diplomacy.Find(PoliticalIdentity.Of(leader.SocialGroup));
                if (reverse != null) Publish(game, leader, Message(other, reverse, game.Session.WorldTime.TurnCounter), "community_peace");
            }
            return true;
        }
        static void EndFeud(CommunityRelation relation, long reviewed, int turn)
        { relation.Feud = false; relation.LastPeaceTurn = turn; relation.Revision++; relation.ReviewedCause = Math.Max(relation.ReviewedCause, reviewed); }
        static void RecordExchange(RogueGame game, Actor player, Actor leader, string text)
        {
            int turn = game.Session.WorldTime.TurnCounter;
            player.Personality.LearnCommunity(leader.SocialGroup.Identity, leader.SocialGroup.CommunityName, turn, "conversation with " + leader.UnmodifiedName)
                .Note(new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = "community_negotiation", EventTurn = turn,
                    LearnedTurn = turn, Source = "participant", Text = text });
            game.DoSay(leader, player, text, RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FREE_ACTION);
        }
        public static bool ShareNews(RogueGame game, Actor speaker, Actor listener)
        {
            if (speaker.Personality == null || speaker.SocialGroup == null) return false;
            DiplomacyMessage news = null;
            if (IsLeader(speaker) && speaker.SocialGroup.Diplomacy != null)
                foreach (var relation in speaker.SocialGroup.Diplomacy.Relations)
                {
                    var message = ReportedMessage(speaker, relation, game.Session.WorldTime.TurnCounter);
                    if (listener.Personality.CanLearnDiplomacy(message, 80)) { news = message; break; }
                }
            if (news == null) foreach (KnownDiplomacy report in speaker.Personality.KnownDiplomacy)
            {
                if (listener.Personality.CanLearnDiplomacy(report.Message, 80)) { news = report.Message.Copy(); break; }
            }
            if (news == null) return false;
            game.DoSay(speaker, listener, news.Name + ": " + news.Stance + " with " + news.Target.Name + ". " + news.Reason +
                "; report turn " + news.Turn + (news.PeaceTurn > 0 ? "; peace ratified turn " + news.PeaceTurn : "") + ".", RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FREE_ACTION);
            Hear(listener, speaker, new NpcFact { DiplomacyMessage = news, Confidence = 80,
                LearnedTurn = game.Session.WorldTime.TurnCounter }, "reported by " + speaker.UnmodifiedName);
            return true;
        }
    }
}
