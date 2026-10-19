using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    enum CommunityRaidPhase { Outbound, Searching, Returning }

    // Each volunteer carries an independent briefing. No remote actor or enemy home pointer.
    [Serializable] sealed class CommunityRaidDuty
    {
        public Guid Id, Community, Commander, Target;
        public string TargetName, ShelterSource, Reason;
        public int ShelterTurn, Deadline, SearchUntil = -1, LastContact, Blocked;
        public CommunityShelterLocation Objective, Home;
        public CommunityRaidPhase Phase;
        public bool SawDefenders;
        public readonly List<Guid> Party = new List<Guid>();
    }
    partial class Actor
    {
        [OptionalField] internal CommunityRaidDuty CommunityRaid;
    }
    sealed partial class SocialGroup
    {
        [OptionalField] internal int NextRaidWorldTurn, RaidReviewWorldTurn;
    }
}
