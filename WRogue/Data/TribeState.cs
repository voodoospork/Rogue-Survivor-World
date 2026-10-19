using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    // Residents are independent actors. This roster never implies command authority.
    [Serializable] sealed class TribeState
    {
        public readonly List<Actor> Residents = new List<Actor>();
        public readonly List<CommunityNote> History = new List<CommunityNote>();
        public readonly List<TribeFoodObservation> Food = new List<TribeFoodObservation>();
        public int FoundedTurn, NextReviewTurn, ShortageReviews, ShortageSince, LastEvictionTurn = -180;
        public int WarningTurn = -1, LastDangerTurn = -180;
        public bool Dissolved;
        public CommunityShelterLocation Relocation;
        public string RelocationReason;
        public void Note(CommunityNote note) { History.Add(note); if (History.Count > 32) History.RemoveAt(0); }
    }
    [Serializable] sealed class TribeFoodObservation
    {
        public CommunityShelterLocation Place;
        public Guid Observer;
        public int Nutrition, Turn;
    }
    [Serializable] sealed class TribeContact
    {
        public Guid Person;
        public CommunityShelterLocation Refuge;
        public int FirstTurn, LastTurn, DeclinedUntil, ObservedTurns;
    }
    [Serializable] sealed class TribeProposal
    {
        public Guid Proposer, Candidate;
        public CommunityShelterLocation Home;
        public int Turn, ExpiresTurn;
        [OptionalField] public int CandidateExpiresTurn;
        public int ReportedShortfall, ReportedSpace, ReportedResidents;
        public bool Admission, Answered, Accepted;
    }
    [Serializable] sealed class ResidentCommunityState
    {
        [OptionalField] List<Guid> m_KnownResidents;
        [OptionalField] public int UntrackedResidents;
        public List<Guid> KnownResidents { get { return m_KnownResidents ?? (m_KnownResidents = new List<Guid>()); } }
        public void RememberResidents(IEnumerable<Actor> members)
        { foreach (Actor member in members) if (!KnownResidents.Contains(member.PersonalityIdentity)) KnownResidents.Add(member.PersonalityIdentity);
            while (KnownResidents.Count > 64) { KnownResidents.RemoveAt(0); UntrackedResidents++; } }
        public readonly List<TribeContact> Contacts = new List<TribeContact>();
        [OptionalField] public CommunityShelterLocation ClockMap;
        [OptionalField] public int LastMapTurn, LocalClock;
        public int Clock(Actor owner)
        {
            int now = owner.Location.Map.LocalTime.TurnCounter;
            if (ClockMap == null) LocalClock = now;
            else if (ClockMap.Matches(owner.Location.Map, ClockMap.Anchor)) { LocalClock += Math.Max(0, now - LastMapTurn); LastMapTurn = now; return LocalClock; }
            else LocalClock++;
            ClockMap = new CommunityShelterLocation { MapSeed = owner.Location.Map.Seed, MapName = owner.Location.Map.Name,
                District = owner.Location.Map.District == null ? (System.Drawing.Point?)null : owner.Location.Map.District.WorldPosition };
            LastMapTurn = now; return LocalClock;
        }
        public int NextReviewTurn, RelocationRetryTurn, FoodReportTurn = -180, ReportedFood;
        [OptionalField] public int FoodReportDate = -180;
        public int UnmetHungerTurn = -180, HungerSince = -1, KnownDangerTurn = -180;
        public int ReportedDemand, EvictionWarningTurn = -1;
        public Guid FoodReportedTo, EvictionWarningFrom;
        public CommunityShelterLocation KnownHome;
        public CommunityShelterLocation ProposedMove;
        public Guid MoveProposer;
        public TribeProposal Proposal;
        public CommunityShelterLocation TravelHome;
        public Guid LastTribe;
        public bool RemainAtOldHome;
        public TribeContact Contact(Guid id) { return Contacts.Find(c => c.Person == id); }
    }
    partial class Actor
    {
        [OptionalField] ResidentCommunityState m_ResidentCommunity;
        public ResidentCommunityState ResidentCommunity { get { return m_ResidentCommunity ?? (m_ResidentCommunity = new ResidentCommunityState()); } }
    }
}
