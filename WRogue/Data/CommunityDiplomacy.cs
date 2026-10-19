using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    [Serializable] struct PoliticalIdentity : IEquatable<PoliticalIdentity>
    {
        public Guid Group;
        public int Faction;
        public string Name;
        public bool Known { get { return Group != Guid.Empty || Faction >= 0; } }
        public bool Equals(PoliticalIdentity other) { return Group == other.Group && (Group != Guid.Empty || Faction == other.Faction); }
        public override bool Equals(object other) { return other is PoliticalIdentity && Equals((PoliticalIdentity)other); }
        public override int GetHashCode() { return Group == Guid.Empty ? Faction : Group.GetHashCode(); }
        public static PoliticalIdentity Unknown { get { return new PoliticalIdentity { Faction = -1 }; } }
        public static PoliticalIdentity Of(SocialGroup group) { return new PoliticalIdentity { Group = group.Identity, Faction = group.FactionId, Name = group.CommunityName }; }
    }
    [Serializable] sealed class DiplomacyEvidence
    {
        public long Episode;
        public string Kind, Explanation;
        public PoliticalIdentity Subject, Other;
        public Guid SubjectPerson, OtherPerson;
        public int Severity, Turn;
        public bool Killing, Assault, Violence, Positive;
        [OptionalField] public bool Property, Nonviolent;
    }
    [Serializable] sealed class DiplomacyIncident
    {
        public long Episode, EventId;
        public string Kind, Explanation, Source, SubjectName, OtherName;
        public PoliticalIdentity Subject, Other;
        public Guid SubjectPerson, OtherPerson;
        public int Severity, Turn, LearnedTurn, Confidence;
        public bool Killing, Assault, Violence;
        [OptionalField] public bool Property, Nonviolent, Resolved;
        [OptionalField] public int RestitutionCredit;
        [OptionalField] public int TrustPenalty, TrustRestored;
        public DiplomacyIncident Copy() { return (DiplomacyIncident)MemberwiseClone(); }
    }
    [Serializable] sealed class CommunityRelation
    {
        public PoliticalIdentity Target;
        public int Trust, Grievance, LastViolenceLearnedTurn, LastPeaceTurn;
        public long Revision, ReviewedCause, RetiredEpisodeThrough;
        public bool Feud;
        [OptionalField] public int DeclaredTurn;
        [OptionalField] public DiplomacyMessage LeaderReport;
        public DiplomacyIncident FoundingCause;
        public CommunityPeaceOffer Pending;
        public readonly List<DiplomacyIncident> Incidents = new List<DiplomacyIncident>();
        public string Stance { get { return Feud ? "blood feud" : Trust >= 20 ? "friendly" : Trust <= -20 ? "wary" : "neutral"; } }
        public void Trim() {
            Incidents.Sort((a,b) => a.Episode.CompareTo(b.Episode));
            while (Incidents.Count > 16) {
                RetiredEpisodeThrough = Math.Max(RetiredEpisodeThrough, Incidents[0].Episode);
                Incidents.RemoveAt(0);
            }
        }
    }
    [Serializable] sealed class CommunityDiplomacyState
    {
        public int NextIntroductionTurn;
        public readonly List<CommunityRelation> Relations = new List<CommunityRelation>();
        public CommunityRelation Find(PoliticalIdentity target) { return Relations.Find(r => r.Target.Equals(target)); }
        public CommunityRelation Get(PoliticalIdentity target)
        {
            var relation = Find(target);
            if (relation == null) { relation = new CommunityRelation { Target = target }; Relations.Add(relation); }
            return relation;
        }
    }
    [Serializable] sealed class DiplomacyMessage
    {
        public Guid Community;
        public string Name, Stance, Reason;
        public PoliticalIdentity Target;
        public long Revision;
        public int Turn, PeaceTurn;
        [OptionalField] public int DeclaredTurn;
        public bool Feud;
        public DiplomacyIncident Cause;
        public DiplomacyMessage Copy() { var copy=(DiplomacyMessage)MemberwiseClone(); copy.Cause=Cause==null ? null : Cause.Copy(); return copy; }
    }
    [Serializable] sealed class KnownDiplomacy
    {
        public DiplomacyMessage Message;
        public int LearnedTurn, Confidence;
        public string Source;
    }
    [Serializable] sealed class CommunityPeaceOffer
    {
        public Guid Id, Origin, Destination, AuthorizedBy, RespondedBy;
        public string OriginName, DestinationName, Response;
        public int CreatedTurn, ExpiresTurn, ResponseTurn;
        public long OriginCause, DestinationCause;
        public long OriginRevision, DestinationRevision;
        public bool Answered, Accepted, Ratified;
        public CommunityPeaceOffer Copy() { return (CommunityPeaceOffer)MemberwiseClone(); }
        public const string Terms = "End both community blood feuds. Personal enemies and revenge remain; new violence may provoke another declaration. No payment, supplies, skills or safe passage are granted.";
    }
    [Serializable] sealed class CommunityCombatEpisode
    {
        public Guid Victim;
        public long Id;
        public int LastTurn;
        public bool InjuryReported;
    }
    partial class Actor
    {
        [OptionalField] internal List<CommunityCombatEpisode> CommunityCombatEpisodes;
    }
    sealed partial class PersonalityState
    {
        [OptionalField] List<KnownDiplomacy> m_Diplomacy;
        [OptionalField] List<CommunityPeaceOffer> m_PeaceOffers;
        public IList<KnownDiplomacy> KnownDiplomacy { get { return m_Diplomacy ?? (m_Diplomacy = new List<KnownDiplomacy>()); } }
        public IList<CommunityPeaceOffer> PeaceOffers { get { return m_PeaceOffers ?? (m_PeaceOffers = new List<CommunityPeaceOffer>()); } }
        public KnownDiplomacy FindDiplomacy(Guid group, PoliticalIdentity target)
        { return m_Diplomacy == null ? null : m_Diplomacy.Find(d => d.Message.Community == group && d.Message.Target.Equals(target)); }
        public void LearnDiplomacy(DiplomacyMessage message, int turn, string source, int confidence)
        {
            KnownDiplomacy old = FindDiplomacy(message.Community, message.Target);
            if (!CanLearnDiplomacy(message, confidence)) return;
            if (old != null) m_Diplomacy.Remove(old);
            KnownDiplomacy.Add(new KnownDiplomacy { Message = message.Copy(), LearnedTurn = turn, Source = source, Confidence = confidence });
            if (KnownDiplomacy.Count > 64) KnownDiplomacy.RemoveAt(0);
        }
        public bool CanLearnDiplomacy(DiplomacyMessage message, int confidence)
        {
            KnownDiplomacy old = FindDiplomacy(message.Community, message.Target);
            if (old == null) return true;
            // A later retelling of an old belief cannot undo a signed peace or a new declaration.
            if (message.PeaceTurn < old.Message.PeaceTurn && message.DeclaredTurn <= old.Message.PeaceTurn ||
                message.DeclaredTurn < old.Message.DeclaredTurn && message.PeaceTurn <= old.Message.DeclaredTurn) return false;
            if (message.PeaceTurn > old.Message.PeaceTurn || message.DeclaredTurn > old.Message.DeclaredTurn) return true;
            return old.Message.Revision < message.Revision || old.Message.Revision == message.Revision && old.Confidence < confidence;
        }
    }
}
