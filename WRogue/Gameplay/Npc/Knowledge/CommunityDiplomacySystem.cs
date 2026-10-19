using System;
using System.Collections.Generic;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static partial class CommunityDiplomacySystem
    {
        public static bool Enabled { get { return Session.Get.GamePreset.NpcPersonalitiesEnabled; } }
        public static bool IsLeader(Actor actor)
        {
            return Enabled && actor != null && !actor.IsPlayer && !actor.IsDead && !actor.Model.Abilities.IsUndead &&
                actor.SocialGroup != null && actor.SocialGroup.LeaderId == actor.PersonalityIdentity &&
                (actor.Faction.ID == (int)GameFactions.IDs.TheCivilians || actor.Faction.ID == (int)GameFactions.IDs.ThePolice);
        }
        public static bool PoliticalGroup(Actor actor)
        {
            if (!Enabled || actor == null || (actor.GroupRoot.IsPlayer && actor.Community == null) || actor.SocialGroup == null) return false;
            return actor.SocialGroup.Diplomacy != null || actor.SocialGroup.Home != null || actor.Faction.ID == (int)GameFactions.IDs.ThePolice;
        }
        // Affiliation is a learned statement, not an inference from a live follower pointer.
        public static PoliticalIdentity Recognizes(Actor observer, Actor target)
        {
            if (observer == null || target == null) return PoliticalIdentity.Unknown;
            if (observer.SocialGroup != null && target == observer)
                return PoliticalIdentity.Of(observer.SocialGroup);
            KnownCommunity latest = null; CommunityMember member = null;
            if (observer.Personality != null) foreach (KnownCommunity group in observer.Personality.Communities)
                foreach (CommunityMember candidate in group.Members)
                    if (candidate.Id == target.PersonalityIdentity && (member == null || candidate.Turn > member.Turn ||
                        candidate.Turn == member.Turn && !(candidate.Status ?? "").StartsWith("left")))
                    { latest = group; member = candidate; }
            if (member != null && !(member.Status ?? "").StartsWith("left"))
                return new PoliticalIdentity { Group = latest.Identity, Faction = -1, Name = latest.Name };
            return PoliticalIdentity.Unknown;
        }
        static PoliticalIdentity Endpoint(NpcObservation observation, Actor person, bool visible)
        {
            if (!visible || person == null) return PoliticalIdentity.Unknown;
            PoliticalIdentity known = Recognizes(observation.Owner, person);
            if (known.Known) return known;
            // A visible uniform identifies the institution, not its local unit.
            return new PoliticalIdentity { Faction = person.Faction.ID, Name = person.Faction.Name };
        }
        public static DiplomacyEvidence Capture(NpcObservation o)
        {
            SignificantEvent e = o.Source;
            bool killing = e.Kind == "community_killing", assault = e.Kind == "community_injury";
            bool property = e.Kind == "base_theft" || e.Kind == "community_defense_tampered";
            bool boundary = e.Kind == "community_trespass_defied";
            bool restitution = e.Kind == "community_restitution_failed" || e.Kind == "community_restitution_refused" || e.Kind == "restitution_refused";
            bool harm = killing || assault || property || boundary || e.Kind == "promise_broken" || restitution;
            bool help = e.Kind == "helped" || e.Kind == "promise_kept" || e.Kind == "traded" || e.Kind == "bartered_food" || e.Kind == "restitution_given";
            if (!harm && !help) return null;
            int severity = assault ? 25 : killing ? 40 : property ? 15 : e.Kind == "promise_broken" ? 5 :
                boundary || restitution ? 10 : e.Kind == "traded" || e.Kind == "bartered_food" ? 2 : 10;
            string explanation = e.Kind.Replace('_', ' ');
            if (killing)
            {
                bool unprovoked = false, defense = false;
                if (o.Direct) { unprovoked = e.KnownMurder; defense = e.KnownSelfDefense; }
                if (o.Owner.Personality != null) foreach (NpcFact prior in o.Owner.Personality.Knowledge.Facts)
                {
                    if (prior.Kind != "attack" || prior.Confidence < 60 || e.Turn - prior.EventTurn > 180) continue;
                    if (prior.SubjectId == e.Subject.PersonalityIdentity && prior.OtherId == e.Other.PersonalityIdentity) unprovoked = true;
                    if (prior.OtherId == e.Subject.PersonalityIdentity && prior.SubjectId == e.Other.PersonalityIdentity) defense = true;
                }
                severity = defense ? 15 : unprovoked ? 60 : 40;
                explanation = defense ? "known self-defense killing" : unprovoked ? "unprovoked killing" : "killing; circumstances unclear";
            }
            PoliticalIdentity beneficiary = Endpoint(o, e.Other, o.SeesOther);
            // A resident recognizes theft from their own home even when its leader is away.
            XpdBase stolenFrom = property || boundary || restitution ? e.Map.XpdBaseAt(e.Position) : null;
            if (stolenFrom != null && o.Owner.SocialGroup != null && stolenFrom.Owns(o.Owner)) beneficiary = PoliticalIdentity.Of(o.Owner.SocialGroup);
            var attribution = e.CultureCase == null ? null : e.CultureCase.Case.Attribution;
            return new DiplomacyEvidence { Episode = e.CombatEpisode == 0 ? e.Id : e.CombatEpisode, Kind = e.Kind,
                Subject = attribution == null ? Endpoint(o, e.Subject, o.SeesSubject) : attribution.Subject,
                Other = attribution == null ? beneficiary : attribution.Other,
                SubjectPerson = attribution == null ? (o.SeesSubject ? e.Subject.PersonalityIdentity : Guid.Empty) : attribution.SubjectPerson,
                OtherPerson = attribution == null ? (o.SeesOther ? e.Other.PersonalityIdentity : Guid.Empty) : attribution.OtherPerson,
                Severity = severity, Explanation = explanation, Killing = killing, Assault = assault,
                Violence = killing || assault, Positive = help, Turn = e.Turn, Property = property || boundary, Nonviolent = property || boundary || restitution || e.Kind == "promise_broken" };
        }
        public static void Observe(NpcObservation observation)
        {
            var e = observation.Source;
            if (e.Kind == "group_succession" && observation.Owner == e.Subject && IsLeader(observation.Owner) && observation.Owner.SocialGroup.Diplomacy != null)
                foreach (var relation in observation.Owner.SocialGroup.Diplomacy.Relations)
                    observation.Owner.Personality.LearnDiplomacy(ReportedMessage(observation.Owner, relation, e.Turn), e.Turn, "assumed leadership; community record", 100);
            var fact = observation.Owner.Personality.Knowledge.Facts.Find(f => f.EventId == e.Id && f.Kind == e.Kind);
            if (fact == null && observation.DiplomacyEvidence != null)
            {
                fact = new NpcFact { Kind = e.Kind, EventId = e.Id, EventTurn = e.Turn, LearnedTurn = e.Turn,
                    Source = observation.Direct ? NpcKnowledgeSource.Participant : NpcKnowledgeSource.Witness,
                    SourceId = observation.Owner.PersonalityIdentity, Confidence = observation.Direct ? 100 : 90,
                    Place = new Location(e.Map, e.Position), SubjectId = observation.SeesSubject ? e.Subject.PersonalityIdentity : Guid.Empty,
                    OtherId = observation.SeesOther ? e.Other.PersonalityIdentity : Guid.Empty,
                    SubjectName = observation.SeesSubject ? e.Subject.UnmodifiedName : null,
                    OtherName = observation.SeesOther ? e.Other.UnmodifiedName : null, DiplomacyEvidence = observation.DiplomacyEvidence };
                observation.Owner.Personality.Knowledge.Learn(fact);
            }
            if (fact != null) Learn(observation.Owner, fact, observation.Direct ? "participant" : "witness");
        }
        public static void Hear(Actor listener, Actor speaker, NpcFact fact, string source = null)
        { Learn(listener, fact, source ?? "reported by " + speaker.UnmodifiedName); }
        static void Learn(Actor observer, NpcFact fact, string source)
        {
            if (!Enabled || observer.IsSleeping || observer.IsDead || observer.Personality == null) return;
            if (fact.DiplomacyMessage != null && fact.Confidence >= 40)
            {
                observer.Personality.LearnDiplomacy(fact.DiplomacyMessage, fact.LearnedTurn, source, fact.Confidence);
                if (IsLeader(observer) && fact.DiplomacyMessage.Community == observer.SocialGroup.Identity)
                {
                    if (observer.SocialGroup.Diplomacy == null) observer.SocialGroup.Diplomacy = new CommunityDiplomacyState();
                    var ownReport = observer.Personality.FindDiplomacy(fact.DiplomacyMessage.Community, fact.DiplomacyMessage.Target);
                    observer.SocialGroup.Diplomacy.Get(fact.DiplomacyMessage.Target).LeaderReport = ownReport.Message.Copy();
                }
                observer.Personality.LearnCommunity(fact.DiplomacyMessage.Community, fact.DiplomacyMessage.Name, fact.LearnedTurn, source);
                if (fact.DiplomacyMessage.Target.Group != Guid.Empty)
                    observer.Personality.LearnCommunity(fact.DiplomacyMessage.Target.Group, fact.DiplomacyMessage.Target.Name, fact.LearnedTurn, source);
                if (IsLeader(observer) && fact.DiplomacyMessage.Target.Group == observer.SocialGroup.Identity)
                {
                    if (observer.SocialGroup.Diplomacy == null) observer.SocialGroup.Diplomacy = new CommunityDiplomacyState();
                    observer.SocialGroup.Diplomacy.Get(new PoliticalIdentity { Group = fact.DiplomacyMessage.Community, Faction = -1, Name = fact.DiplomacyMessage.Name });
                    var cause=fact.DiplomacyMessage.Cause;
                    if(fact.Confidence>=60 && fact.DiplomacyMessage.Feud && cause!=null && cause.Violence)
                        Learn(observer,new NpcFact { EventId=cause.EventId, EventTurn=cause.Turn, LearnedTurn=fact.LearnedTurn,
                            Source=NpcKnowledgeSource.Told, Confidence=fact.Confidence, SubjectName=cause.SubjectName, OtherName=cause.OtherName,
                            DiplomacyEvidence=new DiplomacyEvidence { Episode=cause.Episode, Kind=cause.Kind, Explanation=cause.Explanation,
                                Subject=cause.Subject, Other=cause.Other, SubjectPerson=cause.SubjectPerson, OtherPerson=cause.OtherPerson,
                                Severity=cause.Severity, Killing=cause.Killing, Assault=cause.Assault, Violence=true, Turn=cause.Turn } },source);
                }
            }
            DiplomacyEvidence evidence = fact.DiplomacyEvidence;
            if (!IsLeader(observer) || !PoliticalGroup(observer) || evidence == null || fact.Confidence < 60) return;
            if (evidence.Killing && evidence.SubjectPerson == observer.PersonalityIdentity) return;
            PoliticalIdentity own = PoliticalIdentity.Of(observer.SocialGroup), target;
            bool ownViolence=evidence.Violence && own.Equals(evidence.Other);
            bool reverse = evidence.Kind == "community_injury" || evidence.Kind == "community_killing" || evidence.Kind == "helped";
            if (evidence.Kind == "traded" || evidence.Kind == "bartered_food")
                target = own.Equals(evidence.Subject) ? evidence.Other : own.Equals(evidence.Other) ? evidence.Subject : PoliticalIdentity.Unknown;
            else if (ownViolence) target=evidence.Subject;
            else if (reverse && own.Equals(evidence.Subject)) target = evidence.Other;
            else if (!reverse && own.Equals(evidence.Other)) target = evidence.Subject;
            else return;
            if (!target.Known || own.Equals(target)) return;
            int severity=ownViolence ? 0 : evidence.Severity;
            SocialGroup group = observer.SocialGroup;
            if (group.Diplomacy == null) group.Diplomacy = new CommunityDiplomacyState();
            CommunityRelation relation = group.Diplomacy.Get(target);
            DiplomacyIncident prior = relation.Incidents.Find(i => i.Episode == evidence.Episode);
            if (prior == null && evidence.Episode <= relation.RetiredEpisodeThrough) return;
            if (prior == null && relation.Incidents.Count >= 16 && evidence.Turn < relation.Incidents[0].Turn) return;
            if (prior != null && prior.EventId == fact.EventId && prior.Confidence >= fact.Confidence) return;
            int delta = severity - (prior == null ? 0 : prior.Severity);
            if ((evidence.Kind == "community_restitution_failed" || evidence.Kind == "community_restitution_refused") && prior != null)
            { severity = prior.Severity + (prior.Kind == "community_restitution_failed" || prior.Kind == "community_restitution_refused" ? 0 : 10); delta = severity-prior.Severity; }
            if (prior != null && delta < 0 && (!evidence.Killing || prior.Killing && prior.Confidence >= fact.Confidence)) return;
            if (prior == null)
            {
                prior = new DiplomacyIncident { Episode = evidence.Episode };
                relation.Incidents.Add(prior);
            }
            bool newIncident=prior.EventId==0;
            prior.EventId = fact.EventId; prior.Kind = evidence.Kind; prior.Explanation = (ownViolence ? "our member used violence: " : "") + evidence.Explanation;
            prior.Subject = evidence.Subject; prior.Other = evidence.Other; prior.SubjectPerson = evidence.SubjectPerson; prior.OtherPerson = evidence.OtherPerson;
            prior.SubjectName = fact.SubjectName; prior.OtherName = fact.OtherName;
            prior.Severity = severity; prior.Turn = evidence.Turn; prior.LearnedTurn = fact.LearnedTurn;
            prior.Source = source; prior.Confidence = fact.Confidence; prior.Killing = evidence.Killing && !ownViolence;
            prior.Assault = evidence.Assault && !ownViolence; prior.Violence = evidence.Violence;
            prior.Property |= evidence.Property; prior.Nonviolent |= evidence.Nonviolent;
            relation.Trim();
            relation.Grievance = Math.Max(0, Math.Min(100, relation.Grievance + (evidence.Positive ? -delta : delta)));
            int oldTrust=relation.Trust;
            relation.Trust = Math.Max(-100, Math.Min(100, relation.Trust + (evidence.Positive ? delta : -delta)));
            if(!evidence.Positive&&evidence.Nonviolent)prior.TrustPenalty+=Math.Max(0,oldTrust-relation.Trust);
            if (evidence.Violence) relation.LastViolenceLearnedTurn = Math.Max(relation.LastViolenceLearnedTurn, fact.LearnedTurn);
            if (delta != 0 || newIncident || evidence.Violence) relation.Revision++;
            observer.Personality.LearnDiplomacy(ReportedMessage(observer, relation, fact.LearnedTurn), fact.LearnedTurn, source, fact.Confidence);
            relation.LeaderReport = observer.Personality.FindDiplomacy(group.Identity, target).Message.Copy();
        }
        public static bool KnowsFeud(Actor observer, Actor target)
        {
            if (!Enabled || observer.Personality == null || observer.SocialGroup == null) return false;
            PoliticalIdentity recognized = Recognizes(observer, target);
            if (recognized.Group == Guid.Empty || recognized.Group == observer.SocialGroup.Identity) return false;
            KnownDiplomacy outgoing = observer.Personality.FindDiplomacy(observer.SocialGroup.Identity, recognized);
            KnownDiplomacy incoming = observer.Personality.FindDiplomacy(recognized.Group, PoliticalIdentity.Of(observer.SocialGroup));
            return outgoing != null && outgoing.Confidence >= 40 && outgoing.Message.Feud || incoming != null && incoming.Confidence >= 40 && incoming.Message.Feud;
        }
        public static int KnownTrust(Actor observer, Actor target)
        {
            if (observer.Personality == null || observer.SocialGroup == null) return 0;
            var known = observer.Personality.FindDiplomacy(observer.SocialGroup.Identity, Recognizes(observer, target));
            return known == null ? 0 : known.Message.Stance == "friendly" ? 10 : known.Message.Stance == "wary" ? -10 : 0;
        }
        public static void Introduce(RogueGame game, Actor speaker)
        {
            SocialGroup group = speaker.SocialGroup;
            if (group == null) return;
            game.RememberDiplomacyGroup(group);
            int turn = game.Session.WorldTime.TurnCounter;
            foreach (Actor listener in speaker.Location.Map.Actors)
            {
                if (listener.IsDead || listener.IsSleeping || listener.Model.Abilities.IsUndead || !listener.Model.Abilities.IsIntelligent ||
                    game.Rules.StdDistance(speaker.Location.Position, listener.Location.Position) > listener.AudioRange) continue;
                if (listener.Personality == null) listener.Personality = new PersonalityState();
                bool identified = listener == speaker || game.IdentifiesDiplomacySpeaker(listener, speaker);
                var learned = listener.Personality.LearnCommunity(group.Identity, group.CommunityName, turn, identified ? "introduced by " + speaker.UnmodifiedName : "heard an unseen voice");
                if (identified)
                    learned.Member(new CommunityMember { Id = speaker.PersonalityIdentity, Name = speaker.UnmodifiedName,
                        Status = "introduced", Turn = turn, Source = "introduced by " + speaker.UnmodifiedName });
            }
        }
        public static int KnownTrust(Actor observer, Guid person)
        {
            if (observer.Personality == null || observer.SocialGroup == null) return 0;
            KnownCommunity latest = null; CommunityMember member = null;
            foreach (KnownCommunity group in observer.Personality.Communities) foreach (CommunityMember candidate in group.Members)
                if (candidate.Id == person && (member == null || candidate.Turn >= member.Turn)) { latest = group; member = candidate; }
            if (member == null || (member.Status ?? "").StartsWith("left")) return 0;
            var known = observer.Personality.FindDiplomacy(observer.SocialGroup.Identity, new PoliticalIdentity { Group = latest.Identity });
            return known == null ? 0 : known.Message.Stance == "friendly" ? 10 : known.Message.Stance == "wary" ? -10 : 0;
        }
        public static CommunityRelation Declaration(Actor leader)
        {
            if (!IsLeader(leader) || leader.SocialGroup.Diplomacy == null) return null;
            int bias = PersonalitySystem.Bias(leader, DecisionKind.Group) + PersonalitySystem.Bias(leader, DecisionKind.Courage) / 2 - PersonalitySystem.Bias(leader, DecisionKind.Compassion) / 2;
            bias = Math.Max(-20, Math.Min(20, bias));
            foreach (var relation in leader.SocialGroup.Diplomacy.Relations)
            {
                if (relation.Feud || relation.Target.Group == Guid.Empty) continue;
                int assaults = 0; bool killing = false; DiplomacyIncident cause = null;
                foreach (var incident in relation.Incidents)
                {
                    if (incident.EventId <= relation.ReviewedCause) continue;
                    if (incident.Assault) assaults++;
                    if (incident.Killing) killing = true;
                    if (incident.Killing || incident.Assault) cause = incident;
                }
                if (cause != null && (killing || assaults >= 2) && relation.Grievance + bias >= 60) return relation;
                var culture = leader.SocialGroup.Culture;
                int grudge = culture == null ? 50 : culture.Grudge;
                int required = grudge >= 70 ? 1 : grudge >= 35 ? 2 : 3;
                int minor = 0; bool property = false;
                foreach (var incident in relation.Incidents)
                    if (incident.EventId > relation.ReviewedCause && incident.Nonviolent && !incident.Resolved)
                    {
                        var dispute=leader.Personality.CaseReports.Find(r=>r.Community==leader.SocialGroup.Identity&&r.Case.Episode==incident.Episode);
                        // A peaceful first demand must have its actual response window before collective escalation.
                        if(dispute!=null&&dispute.Case.HasLoss&&!dispute.Case.Enforce&&!dispute.Case.Refused&&!dispute.Case.Expired)continue;
                        minor++; property |= incident.Property;
                    }
                if (minor >= required && property && relation.Grievance + bias >= 80 - 3 * grudge / 5) return relation;
            }
            return null;
        }
        public static ActorAction Select(RogueGame game, Actor owner, IList<Actor> visible)
        {
            if (!Enabled || !PoliticalGroup(owner) || owner.IsSleeping || owner.IsDead || owner.InPlayerParty || owner.SocialGroup == null || visible == null ||
                game.Rules.IsActorHungry(owner) || game.Rules.IsActorTired(owner) || game.Rules.IsActorSleepy(owner) ||
                owner.HitPoints < game.Rules.ActorMaxHPs(owner) / 2 || owner.Controller is OrderableAI && ((OrderableAI)owner.Controller).Order != null) return null;
            foreach (Actor other in visible) if (game.Rules.IsKnownEnemy(owner, other)) return null;
            var declaration = Declaration(owner);
            if (declaration != null) return new ActionCommunityDiplomacy(owner, game, declaration);
            foreach (Actor listener in visible)
            {
                if (listener == owner || listener.IsSleeping || listener.SocialGroup != owner.SocialGroup ||
                    game.Rules.StdDistance(owner.Location.Position, listener.Location.Position) > listener.AudioRange) continue;
                foreach (NpcFact fact in owner.Personality.Knowledge.Facts)
                {
                    if (fact.DiplomacyEvidence == null && fact.DiplomacyMessage == null && fact.CultureCase == null || fact.Confidence < 60 ||
                        owner.Personality.Knowledge.WasTold(fact.EventId, listener.PersonalityIdentity)) continue;
                    bool has = listener.Personality != null && listener.Personality.Knowledge.Facts.Exists(f => f.EventId == fact.EventId && f.Confidence >= fact.Confidence - 20);
                    if (!has && (IsLeader(listener) || fact.DiplomacyMessage != null)) return new ActionCommunityDiplomacy(owner, game, listener, fact);
                }
            }
            var state = owner.SocialGroup.Diplomacy;
            if (state == null) owner.SocialGroup.Diplomacy = state = new CommunityDiplomacyState();
            if (game.Session.WorldTime.TurnCounter >= state.NextIntroductionTurn) foreach (Actor other in visible)
                if (other != owner && !other.IsPlayer && !other.IsSleeping && other.SocialGroup != owner.SocialGroup &&
                    game.Rules.GridDistance(owner.Location.Position, other.Location.Position) <= 6 &&
                    game.Rules.StdDistance(owner.Location.Position, other.Location.Position) <= other.AudioRange &&
                    Recognizes(other, owner).Group != owner.SocialGroup.Identity)
                    return new ActionCommunityDiplomacy(owner, game);
            return null;
        }
        public static DiplomacyMessage Message(SocialGroup group, CommunityRelation relation, int turn)
        {
            return new DiplomacyMessage { Community = group.Identity, Name = group.CommunityName, Target = relation.Target,
                Revision = relation.Revision, Turn = turn, Feud = relation.Feud, Stance = relation.Stance,
                PeaceTurn = relation.LastPeaceTurn, DeclaredTurn = relation.DeclaredTurn, Cause=relation.FoundingCause==null ? null : relation.FoundingCause.Copy(), Reason = relation.FoundingCause == null ? "known encounters" : CauseDescription(relation.FoundingCause) };
        }
        public static DiplomacyMessage ReportedMessage(Actor leader, CommunityRelation relation, int turn)
        {
            var known = leader.Personality.FindDiplomacy(leader.SocialGroup.Identity, relation.Target);
            var report = relation.LeaderReport ?? (known == null ? null : known.Message);
            if (report == null && relation.LastPeaceTurn == 0) return Message(leader.SocialGroup, relation, turn);
            var message = report == null ? new DiplomacyMessage { Community = leader.SocialGroup.Identity,
                Name = leader.SocialGroup.CommunityName, Target = relation.Target, Reason = "known encounters" } : report.Copy();
            message.Revision = relation.Revision; message.Turn = turn;
            message.Stance = message.Feud ? "blood feud" : relation.Trust >= 20 ? "friendly" : relation.Trust <= -20 ? "wary" : "neutral";
            return message;
        }
        static string CauseDescription(DiplomacyIncident incident)
        { return incident.Explanation + ": " + (incident.Violence ? incident.OtherName : incident.SubjectName) + " against " +
            (incident.Violence ? incident.SubjectName : incident.OtherName) + ", turn " + incident.Turn; }
        internal static void Declare(RogueGame game, Actor leader, CommunityRelation relation)
        {
            if (Declaration(leader) != relation) return;
            relation.Feud = true; relation.Revision++;
            foreach (var incident in relation.Incidents) if (incident.EventId > relation.ReviewedCause &&
                (incident.Killing || incident.Assault || incident.Nonviolent && !incident.Resolved)) relation.FoundingCause = incident.Copy();
            int turn = game.Session.WorldTime.TurnCounter;
            relation.DeclaredTurn = turn;
            game.DoSay(leader, null, "We, " + leader.SocialGroup.CommunityName + ", declare a blood feud with " + relation.Target.Name +
                ". " + CauseDescription(relation.FoundingCause) + ". We will remember.", RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FREE_ACTION);
            Publish(game, leader, Message(leader.SocialGroup, relation, turn), "community_feud");
            game.SpendActorActionPoints(leader, Rules.BASE_ACTION_COST);
        }
        internal static void Publish(RogueGame game, Actor leader, DiplomacyMessage message, string kind)
        {
            var source = new SignificantEvent(kind, leader, null, leader.Location.Map, leader.Location.Position,
                game.Session.WorldTime.TurnCounter, false) { DiplomacyMessage = message };
            PersonalitySystem.Report(game, source);
        }
    }
}
