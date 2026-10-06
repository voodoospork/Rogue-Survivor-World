using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using djack.RogueSurvivor.Engine;

namespace djack.RogueSurvivor.Data
{
    [Serializable]
    sealed class CommunityHomeState
    {
        public CommunityShelterLocation Place;
        public Guid Watcher;
        public int NextRotationTurn, NextAlarmTurn;
        public readonly List<CommunityHomeRetry> Retries = new List<CommunityHomeRetry>();
        public int RetryTurn(Guid actor) { var retry = Retries.Find(r => r.Actor == actor); return retry == null ? 0 : retry.Turn; }
        public void Retry(Guid actor, int turn)
        {
            Retries.RemoveAll(r => r.Actor == actor); Retries.Add(new CommunityHomeRetry { Actor = actor, Turn = turn });
            if (Retries.Count > 32) Retries.RemoveAt(0);
        }
    }
    [Serializable] sealed class CommunityHomeRetry { public Guid Actor; public int Turn; }
    [Serializable]
    sealed class PlayerCommunityMembership
    {
        public Guid CommunityId;
        public string Name, AcceptedBy;
        public int JoinedTurn;
        public SocialGroup PersonalGroup;
        public const string Terms = "Share shelter and communal ground supplies. Give or trade for personal inventories. Help with real requests; promises remain binding. Your followers keep your orders. You may leave together at any time.";
    }
    partial class Actor
    {
        [OptionalField] public PlayerCommunityMembership CommunityMembership;
        [NonSerialized] internal bool PreserveCommunityMembership;
        [NonSerialized] internal string CommunityDepartureReason;
        public Actor GroupRoot { get { Actor root = this; while (root.Leader != null) root = root.Leader; return root; } }
        public IEnumerable<Actor> PartyTree()
        {
            yield return this;
            if (m_Followers != null) foreach (Actor child in m_Followers)
                foreach (Actor member in child.PartyTree()) yield return member;
        }
        public bool InPlayerParty { get { for (Actor a = this; a != null; a = a.Leader) if (a.IsPlayer) return true; return false; } }
        public SocialGroup EnsurePersonalGroup() { if (m_SocialGroup == null) InitializeSocialGroup(); return m_SocialGroup; }
        internal void RestorePersonalParty()
        {
            PlayerCommunityMembership membership = CommunityMembership;
            if (membership == null) return;
            CommunityMembership = null;
            membership.PersonalGroup.Members.Clear();
            AssignSocialGroup(this, membership.PersonalGroup);
            if (Personality != null)
            {
                int turn = Location.Map.LocalTime.TurnCounter;
                KnownCommunity known = Personality.FindCommunity(membership.CommunityId);
                if (known != null) known.Note(new CommunityNote { EventId = Session.Get.NextPersonalityEventId(),
                    Kind = "community_departure", EventTurn = turn, LearnedTurn = turn, Source = "participant",
                    Text = CommunityDepartureReason ?? "Your party left the community after expulsion or group breakup." });
                KnownCommunity personal = Personality.LearnCommunity(membership.PersonalGroup.Identity,
                    membership.PersonalGroup.CommunityName, turn, "your own party");
                foreach (Actor member in PartyTree())
                {
                    if (known != null) known.Member(new CommunityMember { Id = member.PersonalityIdentity, Name = member.UnmodifiedName,
                        Status = "left with your party", Turn = turn, Source = "participant" });
                    personal.Member(new CommunityMember { Id = member.PersonalityIdentity, Name = member.UnmodifiedName,
                        Status = "in your personal party", Turn = turn, Source = "participant" });
                }
            }
            CommunityDepartureReason = null;
        }
    }
}
