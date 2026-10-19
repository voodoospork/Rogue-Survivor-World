using System;
using System.Collections.Generic;

namespace djack.RogueSurvivor.Data
{
    [Serializable]
    sealed partial class SocialGroup
    {
        // The founder's ID initializes the group namespace; later leaders never replace it.
        public readonly Guid Identity;
        public Guid LeaderId;
        public string LeaderName;
        [System.Runtime.Serialization.OptionalField]
        string m_CommunityName;
        public string CommunityName { get {
            return m_CommunityName ?? (m_CommunityName = LeaderName + "'s circle");
        } set { if (String.IsNullOrWhiteSpace(value)) throw new ArgumentException("A community needs a name."); m_CommunityName = value; } }
        public readonly int FactionId;
        public readonly List<Guid> Members = new List<Guid>();
        public long PlanSequence;
        public int NextPlanTurn;
        public NpcGroupPlan Plan;
        [System.Runtime.Serialization.OptionalField] public CommunityContactState CommunityContact;
        [System.Runtime.Serialization.OptionalField] public CommunityHomeState Home;
        [System.Runtime.Serialization.OptionalField] public CommunityDiplomacyState Diplomacy;
        [System.Runtime.Serialization.OptionalField] public TribeState Tribe;
        public SocialGroup(Actor founder, Guid identity)
        { Identity = identity; LeaderId = founder.PersonalityIdentity; LeaderName = founder.UnmodifiedName;
            FactionId = founder.Faction == null ? -1 : founder.Faction.ID; Members.Add(founder.PersonalityIdentity); }
    }
    [Serializable]
    sealed class CommunityContactState
    {
        public Guid PlayerId;
        public int LastSeenTurn, LastCallTurn;
        public bool Called, NeedsGreeting = true, LastCallHostile, HostileWarningIssued;
    }
    [Serializable]
    sealed class NpcGroupPlan
    {
        public string StoryId, Kind, Stage;
        public Guid CollectorId, BeneficiaryId;
        public long CauseId;
        public Location Destination;
        public int Deadline;
        public bool Finished { get { return Stage == "completed" || Stage == "failed" || Stage == "declined"; } }
    }
    partial class Actor
    {
        SocialGroup m_SocialGroup;
        [System.Runtime.Serialization.OptionalField] SocialGroup m_CommunityGroup;
        long m_GroupSequence;
        public SocialGroup SocialGroup { get { return m_CommunityGroup ?? CommandGroup; } }
        public SocialGroup Community { get { return m_CommunityGroup; } }
        public Actor CommunityChief { get { return m_CommunityGroup == null || m_CommunityGroup.Tribe == null ? null :
            m_CommunityGroup.Tribe.Residents.Find(a => !a.IsDead && a.PersonalityIdentity == m_CommunityGroup.LeaderId); } }
        public IEnumerable<Actor> CommunityResidents { get { return Community != null && Community.Tribe != null ?
            (IEnumerable<Actor>)Community.Tribe.Residents : GroupRoot.PartyTree(); } }
        public SocialGroup CommandGroup { get {
            if (m_SocialGroup == null)
            {
                if (m_Leader != null) return m_Leader.CommandGroup;
                if (CountFollowers > 0) InitializeSocialGroup();
            }
            return m_SocialGroup; } }
        void InitializeSocialGroup()
        {
            lock (this)
            {
                if (m_SocialGroup != null) return;
                m_SocialGroup = new SocialGroup(this, m_GroupSequence++ == 0 ? PersonalityIdentity : djack.RogueSurvivor.Engine.SimulationClock.NewIdentity());
                AssignSocialGroup(this, m_SocialGroup);
            }
        }
        static void AssignSocialGroup(Actor actor, SocialGroup group)
        {
            actor.m_SocialGroup = group;
            if (!group.Members.Contains(actor.PersonalityIdentity)) group.Members.Add(actor.PersonalityIdentity);
            if (actor.m_Followers != null) foreach (Actor child in actor.m_Followers) AssignSocialGroup(child, group);
        }
        static void DetachSocialGroup(Actor actor, SocialGroup group)
        {
            if (group != null) group.Members.Remove(actor.PersonalityIdentity);
            actor.m_SocialGroup = null;
            if (actor.m_Followers != null) foreach (Actor child in actor.m_Followers) DetachSocialGroup(child, group);
        }
        void JoinSocialGroup(Actor follower)
        {
            if (m_SocialGroup == null && m_Leader != null) m_SocialGroup = m_Leader.CommandGroup;
            if (m_SocialGroup == null) InitializeSocialGroup();
            if (follower.m_SocialGroup != m_SocialGroup) DetachSocialGroup(follower, follower.m_SocialGroup);
            AssignSocialGroup(follower, m_SocialGroup);
            if (Community != null && follower.Community == null) follower.SetCommunityHousehold(Community);
        }
        void LeaveSocialGroup(Actor follower)
        {
            DetachSocialGroup(follower, m_SocialGroup);
            if (follower.CommunityMembership != null && !follower.PreserveCommunityMembership)
                follower.RestorePersonalParty();
        }
        internal void SetCommunityHousehold(SocialGroup community)
        {
            foreach (Actor member in PartyTree())
            {
                if (member.m_CommunityGroup != null && member.m_CommunityGroup.Tribe != null)
                { member.m_CommunityGroup.Tribe.Residents.Remove(member); member.m_CommunityGroup.Members.Remove(member.PersonalityIdentity); }
                member.m_CommunityGroup = community;
                if (community != null)
                {
                    if (!community.Tribe.Residents.Contains(member)) community.Tribe.Residents.Add(member);
                    if (!community.Members.Contains(member.PersonalityIdentity)) community.Members.Add(member.PersonalityIdentity);
                }
            }
        }
        internal void ClearCommunityMember()
        {
            if (m_CommunityGroup == null) return;
            m_CommunityGroup.Tribe.Residents.Remove(this); m_CommunityGroup.Members.Remove(PersonalityIdentity); m_CommunityGroup = null;
        }
        // Compatibility conversion changes command links, never the resident's assignment.
        internal void ReleaseCommunityCommand()
        {
            if (m_Leader == null) { m_SocialGroup = null; return; }
            Actor parent = m_Leader;
            parent.m_Followers.Remove(this); if (parent.m_Followers.Count == 0) parent.m_Followers = null;
            m_Leader = null; m_SocialGroup = null;
            Gameplay.AI.BaseAI.ResetPartyTravel(this);
        }
        public void TransferSocialGroupTo(Actor successor)
        {
            if (successor == null || successor.IsDead || successor.Leader != this) throw new ArgumentException("Successor must be a living follower.");
            SocialGroup group = m_SocialGroup ?? new SocialGroup(this, m_GroupSequence++ == 0 ? PersonalityIdentity : djack.RogueSurvivor.Engine.SimulationClock.NewIdentity());
            var survivors = new List<Actor>(m_Followers);
            foreach (Actor follower in survivors) follower.PreserveCommunityMembership = true;
            try { RemoveAllFollowers(); }
            finally { foreach (Actor follower in survivors) follower.PreserveCommunityMembership = false; }
            m_SocialGroup = null;
            successor.m_SocialGroup = group; group.Members.Clear(); group.Members.Add(successor.PersonalityIdentity);
            AssignSocialGroup(successor, group);
            group.LeaderId = successor.PersonalityIdentity; group.LeaderName = successor.UnmodifiedName;
            foreach (Actor follower in survivors)
                if (follower != successor && !follower.IsDead)
                { successor.AddFollower(follower); follower.TrustInLeader = follower.GetTrustIn(successor); }
        }
    }
}
