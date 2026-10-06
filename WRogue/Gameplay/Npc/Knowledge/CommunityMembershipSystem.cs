using System;
using System.Collections.Generic;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class CommunityMembershipSystem
    {
        public static bool IsLeader(RogueGame game, Actor actor)
        { return game.Session.GamePreset.NpcPersonalitiesEnabled && game.Session.GamePreset.Bases && actor != null &&
            !actor.IsPlayer && !actor.IsDead && !actor.Model.Abilities.IsUndead && actor.Faction == game.GameFactions.TheCivilians &&
            actor.SocialGroup != null && actor.SocialGroup.LeaderId == actor.PersonalityIdentity; }
        public static bool CanJoin(RogueGame game, Actor player, Actor leader, out string reason)
        {
            reason = null;
            if (player == null || !player.IsPlayer || player.IsDead || player.Model.Abilities.IsUndead || player.IsSleeping || !IsLeader(game, leader) || leader.IsSleeping)
                reason = "A living, awake civilian leader must invite you.";
            else if (player.CommunityMembership != null || player.Leader != null)
                reason = "Leave your current community first.";
            else
            {
                game.InitializeCommunityHome(leader);
                if (game.ResolveCommunityHome(leader) == null) reason = "We need a valid home before inviting you.";
                var ids = new HashSet<Guid>();
                foreach (Actor member in leader.PartyTree())
                {
                    if (member.IsPlayer) reason = "A player party already belongs here.";
                    if (!ids.Add(member.PersonalityIdentity)) reason = "The community contains duplicate identities.";
                }
                foreach (Actor personal in player.PartyTree())
                {
                    if (personal.IsDead || personal.Model.Abilities.IsUndead || ids.Contains(personal.PersonalityIdentity)) reason = "Your party cannot safely join this group.";
                    foreach (Actor member in leader.PartyTree())
                        if (!member.IsDead && (game.Rules.AreEnemies(personal, member, false) ||
                            game.Rules.IsKnownEnemy(personal, member) || game.Rules.IsKnownEnemy(member, personal))) reason = "We have an unresolved hostile dispute with your party.";
                }
                var personalIds = new HashSet<Guid>();
                foreach (Actor personal in player.PartyTree()) if (!personalIds.Add(personal.PersonalityIdentity)) reason = "Your party contains duplicate identities.";
                if (reason == null && CommunityEncounterSystem.Tone(game, leader, player) != CommunityTone.Neutral &&
                    CommunityEncounterSystem.Tone(game, leader, player) != CommunityTone.Welcoming)
                    reason = "I don't trust your party enough to invite you.";
                bool aid = false;
                if (leader.Personality != null) foreach (NpcFact fact in leader.Personality.Knowledge.Facts)
                    if (fact.Confidence >= 40 && ((fact.Kind == "helped" && fact.OtherId == player.PersonalityIdentity && ids.Contains(fact.SubjectId)) ||
                        (fact.Kind == "promise_kept" && fact.SubjectId == player.PersonalityIdentity && ids.Contains(fact.OtherId)))) { aid = true; break; }
                if (reason == null && !aid) reason = "Help one of us with a real need first. I must know about that aid.";
            }
            return reason == null;
        }
        public static bool Join(RogueGame game, Actor player, Actor leader)
        {
            string reason;
            if (!CanJoin(game, player, leader, out reason)) return false;
            SocialGroup original = player.EnsurePersonalGroup();
            SocialGroup community = leader.SocialGroup;
            var membership = new PlayerCommunityMembership { CommunityId = community.Identity, Name = community.CommunityName,
                AcceptedBy = leader.UnmodifiedName, JoinedTurn = player.Location.Map.LocalTime.TurnCounter, PersonalGroup = original };
            leader.AddFollower(player);
            // Rebuild from the validated physical tree, discarding obsolete IDs from older group state.
            community.Members.Clear();
            foreach (Actor member in leader.PartyTree()) community.Members.Add(member.PersonalityIdentity);
            player.CommunityMembership = membership;
            if (player.Personality == null) player.Personality = new PersonalityState();
            KnownCommunity known = player.Personality.LearnCommunity(community.Identity, membership.Name, membership.JoinedTurn,
                "invited by " + membership.AcceptedBy);
            known.Note(new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = "community_membership",
                EventTurn = membership.JoinedTurn, LearnedTurn = membership.JoinedTurn, Source = "participant",
                Text = "Your party joined by invitation from " + membership.AcceptedBy + ". " + PlayerCommunityMembership.Terms });
            foreach (Actor personal in player.PartyTree()) known.Member(new CommunityMember { Id = personal.PersonalityIdentity,
                Name = personal.UnmodifiedName, Status = "joined with your party", Turn = membership.JoinedTurn, Source = "participant" });
            NpcEvents.Publish(game, "community_joined", player, leader);
            game.SpendActorActionPoints(player, Rules.BASE_ACTION_COST);
            game.AddCommunitySpeech("Your party joined " + membership.Name + ". J, then Tab: terms and L to leave. Your personal followers keep their orders.",
                System.Drawing.Color.LightSkyBlue);
            return true;
        }
        public static bool Leave(RogueGame game, Actor player)
        {
            if (player == null || player.IsDead || player.IsSleeping || player.CommunityMembership == null || player.Leader == null) return false;
            Actor leader = player.Leader;
            string name = player.CommunityMembership.Name;
            player.CommunityDepartureReason = "Your party voluntarily left " + name + ". Existing promises remain in force.";
            leader.RemoveFollower(player);
            NpcEvents.Publish(game, "community_left", player, leader);
            game.SpendActorActionPoints(player, Rules.BASE_ACTION_COST);
            game.AddCommunitySpeech("Your party left " + name + ". Your followers, orders and carried supplies stay with you; promises still apply.",
                System.Drawing.Color.LightSkyBlue);
            return true;
        }
    }
}
