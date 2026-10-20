using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Drawing;

namespace djack.RogueSurvivor.Data
{
    enum CommunityAggression { Guarded, Raiders, AttackOutsiders }
    [Serializable] sealed class CommunityPolicy
    {
        public Guid Community;
        public string Name, Rules, Source;
        public int Territoriality, Enforcement, Grudge, Turn;
        public long Revision;
        public bool Complete;
        [OptionalField] public CommunityAggression Aggression;
        [OptionalField] public bool AggressionKnown;
        public CommunityPolicy Copy() { return (CommunityPolicy)MemberwiseClone(); }
    }
    [Serializable] sealed class CommunityCulture
    {
        public int Territoriality = 50, Enforcement = 50, Grudge = 50, LastRevisionClock;
        public long Revision = 1, ReviewedEvent;
        public readonly List<CommunityCase> Cases = new List<CommunityCase>();
        public readonly List<CommunityView> Views = new List<CommunityView>();
        public readonly List<long> Experiences = new List<long>();
        public int Cooperation, Loss, Violations;
        [OptionalField] public CommunityAggression Aggression;
        [OptionalField] public bool AggressionInitialized;
        public void Bound()
        {
            while (Cases.Count > 32) Cases.RemoveAt(0);
            while (Views.Count > 32) Views.RemoveAt(0);
            while (Experiences.Count > 64) Experiences.RemoveAt(0);
        }
    }
    [Serializable] sealed class CommunityView
    {
        public Guid Person;
        public int Territoriality, Enforcement, Grudge, Turn;
    }
    [Serializable] sealed class CommunityLoss
    {
        public int ModelId, Units, Repaid;
        public string Resource;
        public bool Tampered, Repaired;
        public Point Position;
        public int Remaining { get { return Tampered ? (Repaired ? 0 : 1) : Math.Max(0, Units - Repaid); } }
        public CommunityLoss Copy() { return (CommunityLoss)MemberwiseClone(); }
    }
    [Serializable] sealed class CommunityCase
    {
        public long Episode;
        public long UpdateId;
        public long RequestId;
        public DiplomacyEvidence Attribution;
        public Guid Person, WarnedBy;
        public string PersonName, Kind;
        public CommunityShelterLocation Home;
        public CommunityShelterLocation DeadlineArea;
        public int LastObservedClock, ObservedGrace, WarnedClock = -1, DemandClock = -1, LastIncidentClock;
        public int PermissionUntil = -1;
        public bool Defied, Enforce, Departed, Refused, Expired, Resolved, Attacked, Promised;
        public readonly List<CommunityLoss> Losses = new List<CommunityLoss>();
        public readonly List<Guid> Listeners = new List<Guid>();
        public readonly List<Guid> Guests = new List<Guid>();
        public CommunityCase Copy()
        {
            var copy = new CommunityCase { Episode = Episode, UpdateId=UpdateId, RequestId = RequestId, Attribution = Attribution, Promised = Promised, Person = Person, PersonName = PersonName, Kind = Kind,
                WarnedBy = WarnedBy, Home = Home == null ? null : Home.Copy(), DeadlineArea=DeadlineArea==null?null:DeadlineArea.Copy(), LastObservedClock = LastObservedClock,
                ObservedGrace = ObservedGrace, WarnedClock = WarnedClock, DemandClock = DemandClock,
                LastIncidentClock = LastIncidentClock, PermissionUntil = PermissionUntil, Defied = Defied,
                Enforce = Enforce, Departed = Departed, Refused = Refused, Expired = Expired, Resolved = Resolved, Attacked = Attacked };
            foreach (var loss in Losses) copy.Losses.Add(loss.Copy());
            copy.Guests.AddRange(Guests); copy.Listeners.AddRange(Listeners); return copy;
        }
        public bool HasLoss { get { return Losses.Exists(l => l.Remaining > 0); } }
    }
    [Serializable] sealed class KnownCommunityCase
    {
        public Guid Community;
        public string Name, Source;
        public int Turn;
        public CommunityCase Case;
        public KnownCommunityCase Copy() { return new KnownCommunityCase { Community = Community, Name = Name,
            Source = Source, Turn = Turn, Case = Case.Copy() }; }
    }
    partial class SocialGroup
    {
        [OptionalField] public CommunityCulture Culture;
    }
    [Serializable] sealed class CommunityPredation
    {
        public Guid Target;
        public Location Departure, LastSeen;
        public int Deadline, InitialFood, InitialMedicine, Blocked;
        public bool Returning;
    }
    partial class Actor
    {
        [OptionalField] internal CommunityPredation Predation;
        [OptionalField] internal int NextPredationClock;
    }
    sealed partial class PersonalityState
    {
        [OptionalField] List<long> m_CommunityCases;
        [OptionalField] List<KnownCommunityCase> m_CaseReports;
        public List<KnownCommunityCase> CaseReports { get { return m_CaseReports ?? (m_CaseReports = new List<KnownCommunityCase>()); } }
        public List<long> CommunityCases { get { return m_CommunityCases ?? (m_CommunityCases = new List<long>()); } }
        public void LearnCase(long episode)
        { if (!CommunityCases.Contains(episode)) CommunityCases.Add(episode); while (CommunityCases.Count > 32) CommunityCases.RemoveAt(0); }
        public void LearnCase(KnownCommunityCase report)
        {
            var old = CaseReports.Find(c => c.Community == report.Community && c.Case.Episode == report.Case.Episode);
            if (old != null && (old.Turn > report.Turn || old.Case.UpdateId>report.Case.UpdateId)) return;
            if (old != null) CaseReports.Remove(old);
            CaseReports.Add(report.Copy()); LearnCase(report.Case.Episode);
            while (CaseReports.Count > 32) CaseReports.RemoveAt(0);
        }
    }
    partial class KnownCommunity
    {
        [OptionalField] public CommunityPolicy Policy;
    }
}
