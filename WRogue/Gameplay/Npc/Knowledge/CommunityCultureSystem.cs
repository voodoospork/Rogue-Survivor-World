using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static partial class CommunityCultureSystem
    {
        static int Clamp(int n) { return Math.Max(0, Math.Min(100, n)); }
        internal static int Clock(Actor a) { return a.ResidentCommunity.Clock(a); }
        static bool Eligible(Actor a) { return a != null && CommunityDiplomacySystem.PoliticalGroup(a) &&
            (a.Faction.ID == (int)GameFactions.IDs.TheCivilians || a.Faction.ID == (int)GameFactions.IDs.ThePolice); }
        static int Trait(Actor a, string id, int amount) { return PersonalitySystem.HasTrait(a, id) ? amount : 0; }
        internal static CommunityPolicy Personal(Actor a)
        {
            int compassion = PersonalitySystem.Bias(a, DecisionKind.Compassion), courage = PersonalitySystem.Bias(a, DecisionKind.Courage);
            return new CommunityPolicy {
                Territoriality = Clamp(50 + PersonalitySystem.Bias(a, DecisionKind.Supplies) / 2 - PersonalitySystem.Bias(a, DecisionKind.Group) / 2 -
                    compassion / 2 - PersonalitySystem.Bias(a, DecisionKind.Explore) / 4 + Trait(a,"suspicious",15) + Trait(a,"hoarder",15) + Trait(a,"homebody",10) - Trait(a,"trusting",10)),
                Enforcement = Clamp(50 + courage / 2 - compassion / 2 + Trait(a,"cruel",15) + Trait(a,"hotheaded",15) - Trait(a,"peacemaker",10)),
                Grudge = Clamp(50 - compassion / 2 + courage / 4 + Trait(a,"vindictive",20) - Trait(a,"forgiving",20) - Trait(a,"peacemaker",10)) };
        }
        public static void Initialize(RogueGame game, SocialGroup group, IEnumerable<Actor> founders)
        {
            if (group == null || group.Culture != null || !game.Session.GamePreset.NpcPersonalitiesEnabled) return;
            var people = founders.Where(a => a != null && !a.IsDead && !a.IsPlayer).ToList();
            var votes = people.Select(Personal).ToList();
            group.Culture = new CommunityCulture {
                Territoriality = votes.Count == 0 ? 50 : (int)votes.Average(v => v.Territoriality),
                Enforcement = votes.Count == 0 ? 50 : (int)votes.Average(v => v.Enforcement),
                Grudge = votes.Count == 0 ? 50 : (int)votes.Average(v => v.Grudge),
                LastRevisionClock = game.Session.WorldTime.TurnCounter };
            InitializeAggression(group, people.Any(a => a.Faction.ID == (int)GameFactions.IDs.ThePolice));
            foreach (Actor a in people) if(!a.IsSleeping) LearnPolicy(a, Snapshot(group, game.Session.WorldTime.TurnCounter), "founding agreement", false);
        }
        public static void Ensure(RogueGame game, Actor actor)
        {
            if (!Eligible(actor)) return;
            if (actor.SocialGroup.Culture != null) {
                if (!actor.SocialGroup.Culture.AggressionInitialized) {
                    InitializeAggression(actor.SocialGroup, actor.Faction.ID == (int)GameFactions.IDs.ThePolice);
                    actor.SocialGroup.Culture.Revision++;
                }
                // Old saves lack this fixed founding policy. Upgrade only the resident's own
                // policy; no foreign policy, person, location or current revision is revealed.
                var old = KnownPolicy(actor);
                if (old != null && !old.AggressionKnown) {
                    if (actor.PersonalityIdentity == actor.SocialGroup.LeaderId) {
                        LearnPolicy(actor, Snapshot(actor.SocialGroup, game.Session.WorldTime.TurnCounter), "community policy initialized");
                        return;
                    }
                    old.Aggression = actor.SocialGroup.Culture.Aggression; old.AggressionKnown = true;
                    old.Rules = AggressionText(old.Aggression) + RulesText(old.Territoriality, old.Enforcement, old.Grudge);
                }
                return;
            }
            // Compatibility initialization creates no group, claim, actors or provisions.
            Initialize(game, actor.SocialGroup, actor.CommunityResidents.Where(a => !a.IsPlayer &&
                a.Location.Map == actor.Location.Map && (a == actor || NpcKnowledgeSystem.Visible(game, actor, a.Location))));
        }
        static CommunityPolicy Snapshot(SocialGroup group, int turn)
        { var c = group.Culture; return new CommunityPolicy { Community = group.Identity, Name = group.CommunityName,
            Territoriality = c.Territoriality, Enforcement = c.Enforcement, Grudge = c.Grudge, Revision = c.Revision, Turn = turn,
            Aggression = c.Aggression, AggressionKnown = true,
            Rules = AggressionText(c.Aggression) + RulesText(c.Territoriality,c.Enforcement,c.Grudge), Complete = true }; }
        static void InitializeAggression(SocialGroup group, bool police)
        {
            var c = group.Culture; if (c.AggressionInitialized) return;
            // Stable identity sampling uses no gameplay RNG draws. Compassionate founding
            // cultures and police stay guarded; forceful cultures can become predators.
            var bytes = group.Identity.ToByteArray(); int roll = (bytes[0] * 256 + bytes[1]) % 100;
            c.Aggression = police || c.Enforcement < 50 ? CommunityAggression.Guarded :
                c.Enforcement >= 80 || roll < 15 ? CommunityAggression.AttackOutsiders :
                c.Enforcement >= 65 || roll < 40 ? CommunityAggression.Raiders : CommunityAggression.Guarded;
            c.AggressionInitialized = true;
        }
        internal static string AggressionText(CommunityAggression aggression)
        { return aggression == CommunityAggression.AttackOutsiders ? "We attack all outsiders on sight. " :
            aggression == CommunityAggression.Raiders ? "When supplies run short, we attack vulnerable outsiders for what they may carry. " : ""; }
        static int Band(int n) { return n < 35 ? 20 : n >= 70 ? 80 : 50; }
        static CommunityPolicy Spoken(CommunityPolicy p)
        { var copy = p.Copy(); copy.Territoriality = Band(p.Territoriality); copy.Enforcement = Band(p.Enforcement); copy.Grudge = Band(p.Grudge); return copy; }
        internal static string RulesText(int territory, int force, int grudge)
        {
            return (territory < 35 ? "Visitors are welcome; ask before taking supplies." : territory < 70 ?
                "Ask permission before entering our home. Supplies remain ours." : "Our home is private. Uninvited visitors must leave.") +
                (force >= 70 ? " We defend stolen supplies and tampered defenses with force." : " Replace anything you take; we will ask for restitution first.") +
                (grudge >= 70 ? " One property offence can start a blood feud." : grudge >= 35 ?
                " Repeated offences can start a blood feud." : " We prefer amends, but repeated property offences can exhaust our patience.");
        }
        static void LearnPolicy(Actor actor, CommunityPolicy policy, string source, bool note = true)
        {
            if (actor.Personality == null) actor.Personality = new PersonalityState();
            var known = actor.Personality.LearnCommunity(policy.Community,policy.Name,policy.Turn,source);
            if (known.Policy != null && known.Policy.Revision > policy.Revision) return;
            if (known.Policy != null && known.Policy.Revision == policy.Revision && known.Policy.Complete && !policy.Complete) return;
            bool changed = known.Policy == null || known.Policy.Revision != policy.Revision || !known.Policy.Complete && policy.Complete;
            known.Policy = policy.Copy(); known.Policy.Source = source;
            if (note && changed) known.Note(new CommunityNote { EventId = -policy.Revision, Kind = policy.Complete?"community_policy":"community_territory_statement", EventTurn = policy.Turn,
                LearnedTurn = policy.Turn, Source = source, Text = policy.Rules });
        }
        internal static CommunityPolicy KnownPolicy(Actor a)
        { var k = a == null || a.Personality == null || a.SocialGroup == null ? null : a.Personality.FindCommunity(a.SocialGroup.Identity); return k == null ? null : k.Policy; }
        public static int SocialModifier(Actor a)
        { var p = KnownPolicy(a); return p == null ? 0 : Math.Max(-15,Math.Min(15,(50-p.Territoriality)/5+(50-p.Grudge)/10)); }
        public static int GreetingModifier(Actor a) { var p = KnownPolicy(a); return p == null ? 0 : (50-p.Territoriality)/2; }
        public static int PeaceModifier(Actor a) { var p = KnownPolicy(a); return p == null ? 0 : (50-p.Grudge)/5; }
        public static string SpokenRules(Actor speaker)
        { var p = KnownPolicy(speaker); return p == null ? "" : p.Rules.Substring(0,p.Rules.IndexOf('.')+1); }
        static bool Hears(RogueGame game, Actor listener, Actor speaker)
        { return !listener.IsDead && !listener.IsSleeping && listener.Model.Abilities.IsIntelligent && !listener.Model.Abilities.IsUndead &&
            listener.Location.Map == speaker.Location.Map && game.Rules.StdDistance(listener.Location.Position,speaker.Location.Position) <= listener.AudioRange; }
        static string Source(RogueGame game, Actor listener, Actor speaker)
        { return game.IdentifiesDiplomacySpeaker(listener,speaker) ? "reported by " + speaker.UnmodifiedName : "heard an unseen voice"; }
        internal static void SayPolicy(RogueGame game, Actor speaker, Actor target, bool free)
        {
            Ensure(game,speaker); if (speaker.SocialGroup == null || speaker.SocialGroup.Culture == null) return;
            var p = speaker.PersonalityIdentity == speaker.SocialGroup.LeaderId ? Snapshot(speaker.SocialGroup,game.Session.WorldTime.TurnCounter) : KnownPolicy(speaker);
            if (p == null) return;
            game.DoSay(speaker,target,speaker.SocialGroup.CommunityName + ": " + p.Rules,
                RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | (free ? RogueGame.Sayflags.IS_FREE_ACTION : RogueGame.Sayflags.NONE));
            foreach (Actor listener in speaker.Location.Map.Actors) if (Hears(game,listener,speaker)) {
                LearnPolicy(listener,listener==speaker?p:Spoken(p),Source(game,listener,speaker));LearnSpeaker(game,listener,speaker,p.Community,p.Name,p.Turn); }
        }
        public static void HearCallRules(RogueGame game,Actor speaker)
        {
            var known=KnownPolicy(speaker);if(known==null)return;var p=known.Copy();p.Complete=false;p.Enforcement=p.Grudge=50;
            p.Territoriality=p.Aggression==CommunityAggression.Guarded?Band(p.Territoriality):50;p.Rules=SpokenRules(speaker);
            foreach(Actor listener in speaker.Location.Map.Actors)if(Hears(game,listener,speaker))LearnPolicy(listener,p,Source(game,listener,speaker));
        }
        static KnownCommunityCase Report(SocialGroup group, CommunityCase c, int date, string source)
        { return new KnownCommunityCase { Community = group.Identity, Name = group.CommunityName, Case = c.Copy(), Turn = date, Source = source }; }
        internal static void LearnReport(Actor observer, KnownCommunityCase report, string source)
        {
            if (observer == null || observer.IsSleeping || observer.IsDead) return;
            if (observer.Personality == null) observer.Personality = new PersonalityState();
            var copy = report.Copy(); copy.Source = source; observer.Personality.LearnCase(copy);
            if (observer.SocialGroup != null && observer.SocialGroup.Identity == report.Community &&
                observer.PersonalityIdentity == observer.SocialGroup.LeaderId && observer.SocialGroup.Culture != null)
            {
                var stored = observer.SocialGroup.Culture.Cases.Find(c => c.Episode == report.Case.Episode);
                if (stored == null||stored.UpdateId<report.Case.UpdateId) {
                    if(stored!=null)observer.SocialGroup.Culture.Cases.Remove(stored);
                    observer.SocialGroup.Culture.Cases.Add(report.Case.Copy()); observer.SocialGroup.Culture.Bound(); }
            }
        }
        static void SpeakCase(RogueGame game, Actor speaker, Actor target, CommunityCase c, string text, bool loud = false)
        {
            if(target!=null&&!target.IsPlayer&&!String.IsNullOrEmpty(c.PersonName))text=c.PersonName+": "+text;
            c.UpdateId=game.Session.NextPersonalityEventId();
            game.DoSay(speaker,target,text,RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FREE_ACTION |
                (loud ? RogueGame.Sayflags.IS_SHOUT | RogueGame.Sayflags.IS_DANGER : RogueGame.Sayflags.NONE));
            int date = game.Session.WorldTime.TurnCounter;
            var report = Report(speaker.SocialGroup,c,date,"participant");
            foreach (Actor listener in speaker.Location.Map.Actors) if (Hears(game,listener,speaker))
            {
                var heard=report.Copy();
                if(listener!=speaker&&listener!=target&&
                    (target==null||!NpcKnowledgeSystem.Visible(game,listener,target.Location))&&
                    (listener.Personality==null||listener.Personality.Knowledge.Person(c.Person)==null))
                { heard.Case.Person=Guid.Empty;heard.Case.PersonName=null;heard.Case.Guests.Clear();heard.Case.Attribution=null; }
                LearnReport(listener,heard,Source(game,listener,speaker));
                LearnSpeaker(game,listener,speaker,report.Community,report.Name,date);
                listener.Personality.LearnCommunity(report.Community,report.Name,date,Source(game,listener,speaker))
                    .Note(new CommunityNote { EventId = c.Episode, Kind = "community_" + c.Kind + "_" +
                        (c.Resolved ? "resolved" : c.Refused ? "refused" : c.Expired ? "expired" : c.Defied ? "defied" : "warning")+
                        "_"+c.Losses.Sum(l=>l.Repaid+(l.Repaired?1:0)),
                        EventTurn = date, LearnedTurn = date, Source = Source(game,listener,speaker), Text = text });
            }
        }
        static void LearnSpeaker(RogueGame game,Actor listener,Actor speaker,Guid group,string name,int turn)
        {
            if(!game.IdentifiesDiplomacySpeaker(listener,speaker))return;
            listener.Personality.LearnCommunity(group,name,turn,Source(game,listener,speaker)).Member(new CommunityMember {
                Id=speaker.PersonalityIdentity,Name=speaker.UnmodifiedName,Status="spoke for community",Turn=turn,Source=Source(game,listener,speaker) });
        }
        static CommunityCase LocalCase(Actor actor, Guid community, Guid person)
        {
            if (actor.Personality == null) return null;
            var reports = actor.Personality.CaseReports;
            for (int i = reports.Count - 1; i >= 0; i--) {
                var report = reports[i];
                if (report.Community == community && !report.Case.Resolved && (report.Case.Person == person || report.Case.Guests.Contains(person))) return report.Case;
            }
            return null;
        }
        static bool Inside(Actor a, Guid group, CommunityCase c)
        { XpdBase claim = a.Location.Map.XpdBaseAt(a.Location.Position); return c.Home != null && c.Home.Matches(a.Location.Map,a.Location.Position) &&
            claim != null && claim.CommunityIdentity == group && a.Location.Map.GetExitAt(a.Location.Position) == null &&
            !(a.Location.Map.GetMapObjectAt(a.Location.Position) is djack.RogueSurvivor.Engine.MapObjects.DoorWindow); }
        public static bool Enforcing(Actor observer, Actor target)
        {
            if (!Eligible(observer) || observer.IsDead || observer.IsSleeping || observer.Location.Map != target.Location.Map || target.SocialGroup == observer.SocialGroup) return false;
            var c = LocalCase(observer,observer.SocialGroup.Identity,target.PersonalityIdentity);
            if(c==null||!c.Enforce||c.Resolved)return false;
            return c.Kind=="trespass" ? Inside(target,observer.SocialGroup.Identity,c) :
                Math.Max(Math.Abs(observer.Location.Position.X-target.Location.Position.X),Math.Abs(observer.Location.Position.Y-target.Location.Position.Y))<=6 &&
                LOS.CanTraceViewLine(observer.Location,target.Location.Position);
        }
        public static bool BoundaryOnly(Actor a,Actor b)
        { var c = a.SocialGroup==null?null:LocalCase(a,a.SocialGroup.Identity,b.PersonalityIdentity);
            var reverse=b.SocialGroup==null?null:LocalCase(b,b.SocialGroup.Identity,a.PersonalityIdentity);
            return c!=null&&c.Kind=="trespass"&&Enforcing(a,b)||reverse!=null&&reverse.Kind=="trespass"&&Enforcing(b,a); }
        public static bool LocalEnemies(Actor a, Actor b) { return a != null && b != null && (Enforcing(a,b) || Enforcing(b,a)); }
        public static bool LocalSelfDefense(Actor killer, Actor victim)
        { if (!Enforcing(victim,killer)) return false; var c = LocalCase(killer,victim.SocialGroup.Identity,killer.PersonalityIdentity); return c != null && c.Attacked; }
        public static void Attack(RogueGame game, Actor attacker, Actor target)
        {
            if (!Enforcing(attacker,target)) return;
            var c = LocalCase(attacker,attacker.SocialGroup.Identity,target.PersonalityIdentity); c.Attacked = true;
            target.Personality = target.Personality ?? new PersonalityState();
            target.Personality.LearnCase(Report(attacker.SocialGroup,c,game.Session.WorldTime.TurnCounter,"attacked here"));
        }
        static CommunityCase StoredCase(SocialGroup group, Guid person, CommunityShelterLocation home)
        { return group.Culture.Cases.LastOrDefault(c => (c.Person == person || c.Guests.Contains(person)) && !c.Resolved && c.Home != null && c.Home.MapSeed == home.MapSeed &&
            c.Home.MapName == home.MapName && c.Home.District == home.District && c.Home.Anchor == home.Anchor); }
        public static void Observe(RogueGame game, Actor owner, IList<Actor> visible)
        {
            if (!Eligible(owner) || owner.IsPlayer || owner.IsDead || owner.IsSleeping) return;
            Ensure(game,owner); var policy = KnownPolicy(owner); if (visible == null || policy == null) return;
            var group = owner.SocialGroup;
            foreach (Actor other in visible)
            {
                if (other.IsDead || other.IsSleeping || other.Model.Abilities.IsUndead || other.SocialGroup == group) continue;
                XpdBase claim = other.Location.Map.XpdBaseAt(other.Location.Position);
                var local = LocalCase(owner,group.Identity,other.PersonalityIdentity);
                if (local != null && local.Kind=="trespass" && !Inside(other,group.Identity,local) && !local.Departed)
                { local.Departed = true; local.Enforce = false; local.ObservedGrace = 0;local.UpdateId=game.Session.NextPersonalityEventId(); }
                if (claim == null || claim.CommunityIdentity != group.Identity || !Inside(other,group.Identity,
                    new CommunityCase { Home = CommunityShelterLocation.At(new Location(other.Location.Map,claim.Cells.First())) })) continue;
                if (policy.Territoriality < 35) continue;
                int clock = owner.Location.Map.LocalTime.TurnCounter; var home = CommunityShelterLocation.At(new Location(other.Location.Map,claim.Cells.First()));
                if(local!=null&&local.PermissionUntil>=owner.Location.Map.LocalTime.TurnCounter)continue;
                var c = StoredCase(group,other.PersonalityIdentity,home);
                if (c == null||c.Kind!="trespass"&&local==null) { c = new CommunityCase { Episode = game.Session.NextPersonalityEventId(), Person = other.PersonalityIdentity,
                    PersonName = other.UnmodifiedName, Kind = "trespass", Home = home, LastObservedClock = clock };
                    group.Culture.Cases.Add(c); group.Culture.Bound(); }
                if (local == null) {
                    // The shared record provides only an opaque deduplication key, never an unwitnessed debt or invitation.
                    var observed=new CommunityCase { Episode=c.Episode,Person=other.PersonalityIdentity,PersonName=other.UnmodifiedName,
                        Kind="trespass",Home=home,LastObservedClock=clock,UpdateId=game.Session.NextPersonalityEventId() };
                    owner.Personality.LearnCase(Report(group,observed,game.Session.WorldTime.TurnCounter,"observed entrant"));local=LocalCase(owner,group.Identity,other.PersonalityIdentity);
                }
                if (local.Kind != "trespass") continue;
                if (local.Departed) { local.Departed = false; local.Enforce = false; local.WarnedClock = -1; local.ObservedGrace = 0; }
                if (local.WarnedClock >= 0 && local.LastObservedClock < clock && DeparturePossible(game,other,claim))
                { local.ObservedGrace++; local.LastObservedClock = clock;local.UpdateId=game.Session.NextPersonalityEventId(); }
            }
        }
        sealed class DepartureMemo
        {
            internal Actor Person; internal XpdBase Claim; internal Point Position;
            internal long Geometry, Occupancy; internal bool Possible;
        }
        sealed class DepartureCache { internal readonly List<DepartureMemo> Entries = new List<DepartureMemo>(); }
        static WeakReference<Session> departureSession;
        static System.Runtime.CompilerServices.ConditionalWeakTable<Map,DepartureCache> departureMaps =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Map,DepartureCache>();
        static readonly object departureLock = new object();
        static bool DeparturePossible(RogueGame game, Actor other, XpdBase claim)
        {
            var map = other.Location.Map;
            DepartureCache cache;
            lock (departureLock) {
                Session cachedSession;
                if (departureSession == null || !departureSession.TryGetTarget(out cachedSession) || cachedSession != game.Session) {
                    departureSession = new WeakReference<Session>(game.Session);
                    departureMaps = new System.Runtime.CompilerServices.ConditionalWeakTable<Map,DepartureCache>();
                }
                cache = departureMaps.GetValue(map, ignored => new DepartureCache());
            }
            lock(cache) {
                var memo = cache.Entries.Find(m => m.Person == other && m.Claim == claim);
                if(memo != null && memo.Position == other.Location.Position && memo.Geometry == map.GeometryRevision && memo.Occupancy == map.OccupancyRevision)
                { SimulationPerformance.Cache(SimulationPerformance.Reuse.DepartureHit);return memo.Possible; }
                SimulationPerformance.Cache(SimulationPerformance.Reuse.DepartureMiss);
                bool answer = DeparturePossibleCore(game,other,claim);
                if(memo == null) {
                    if(cache.Entries.Count >= 32)cache.Entries.RemoveAt(0);
                    memo = new DepartureMemo { Person = other, Claim = claim };cache.Entries.Add(memo);
                }
                memo.Position = other.Location.Position;memo.Geometry = map.GeometryRevision;memo.Occupancy = map.OccupancyRevision;memo.Possible = answer;
                return answer;
            }
        }
        static bool DeparturePossibleCore(RogueGame game, Actor other, XpdBase claim)
        {
            // Bounded flood through legal, non-destructive steps. Occupancy can temporarily block a queue.
            var seen = new HashSet<Point>(djack.RogueSurvivor.Data.PointComparer.Instance); var queue = new Queue<Point>(); queue.Enqueue(other.Location.Position); seen.Add(other.Location.Position);
            int budget = 256;
            while (queue.Count > 0 && budget-- > 0)
            {
                Point p = queue.Dequeue(); if (!claim.Contains(p)) return true;
                foreach (Direction d in Direction.COMPASS)
                {
                    Point n = p + d; if (!other.Location.Map.IsInBounds(n) || seen.Contains(n)) continue;
                    var map = other.Location.Map; var obj = map.GetMapObjectAt(n);
                    var door = obj as djack.RogueSurvivor.Engine.MapObjects.DoorWindow;
                    if (!map.GetTileAt(n).Model.IsWalkable || map.GetActorAt(n) != null && map.GetActorAt(n) != other ||
                        obj != null && !obj.IsWalkable && (door == null || door.IsBarricaded)) continue;
                    seen.Add(n); queue.Enqueue(n);
                }
            }
            return false;
        }
        public static ActorAction Select(RogueGame game, Actor owner, IList<Actor> visible)
        {
            if (!Eligible(owner) || owner.IsPlayer || owner.InPlayerParty || owner.IsSleeping || visible == null ||
                game.Rules.IsActorHungry(owner) || game.Rules.IsActorTired(owner) || game.Rules.IsActorSleepy(owner) ||
                owner.HitPoints < game.Rules.ActorMaxHPs(owner)/2 || owner.Controller is OrderableAI && ((OrderableAI)owner.Controller).Order != null) return null;
            foreach (Actor danger in visible) if (game.Rules.IsKnownEnemy(owner,danger)) return null;
            Ensure(game,owner); var group = owner.SocialGroup; var policy = KnownPolicy(owner);
            foreach(var known in owner.Personality.CaseReports)
                if(known.Community==group.Identity&&known.Case.HasLoss&&!known.Case.Resolved&&!known.Case.Expired&&!known.Case.Refused&&
                    DemandExpired(owner,known.Case))
                    return new ActionCommunityCulture(owner,game,null,known.Case,"expire");
            foreach (Actor other in visible)
            {
                if (other == owner || other.IsDead || other.IsSleeping || other.SocialGroup == group ||
                    !Hears(game,other,owner) || game.Rules.GridDistance(owner.Location.Position,other.Location.Position) > 6) continue;
                var c = LocalCase(owner,group.Identity,other.PersonalityIdentity);
                if (c == null || c.Resolved) continue;
                if (c.HasLoss && c.DemandClock < 0) return new ActionCommunityCulture(owner,game,other,c,"demand");
                if (c.HasLoss && !c.Expired && DemandExpired(owner,c)) return new ActionCommunityCulture(owner,game,other,c,"expire");
                if (c.Kind == "trespass" && Inside(other,group.Identity,c) && c.PermissionUntil < owner.Location.Map.LocalTime.TurnCounter)
                {
                    if (c.WarnedClock < 0) return new ActionCommunityCulture(owner,game,other,c,"warn");
                    if (c.ObservedGrace >= 30 && (!c.Defied||!c.Enforce)) return new ActionCommunityCulture(owner,game,other,c,"defy");
                }
            }
            if (owner.PersonalityIdentity == group.LeaderId)
            {
                var culture = group.Culture;
                if (game.Session.WorldTime.TurnCounter-culture.LastRevisionClock >= WorldTime.TURNS_PER_DAY && (culture.Experiences.Count > 0 || culture.Views.Exists(v=>v.Turn>=culture.LastRevisionClock)))
                    return new ActionCommunityCulture(owner,game,null,null,"revise");
                foreach (Actor member in visible) if (member.SocialGroup == group && member != owner && Hears(game,member,owner) &&
                    (KnownPolicy(member) == null || !KnownPolicy(member).Complete || KnownPolicy(member).Revision < culture.Revision))
                    return new ActionCommunityCulture(owner,game,member,null,"policy");
            }
            else if (policy != null) foreach (Actor chief in visible) if (chief.SocialGroup == group && chief.PersonalityIdentity == group.LeaderId && Hears(game,chief,owner))
            {
                var view = group.Culture.Views.Find(v => v.Person == owner.PersonalityIdentity);
                if (view == null || game.Session.WorldTime.TurnCounter-view.Turn >= WorldTime.TURNS_PER_DAY)
                    return new ActionCommunityCulture(owner,game,chief,null,"view");
            }
            return null;
        }
        internal static void Perform(RogueGame game, Actor owner, Actor other, CommunityCase c, string kind)
        {
            int clock = owner.Location.Map.LocalTime.TurnCounter; var group = owner.SocialGroup; var culture = group.Culture;
            if (kind == "policy") SayPolicy(game,owner,other,true);
            else if (kind == "view")
            {
                var p = Personal(owner); culture.Views.RemoveAll(v => v.Person == owner.PersonalityIdentity);
                culture.Views.Add(new CommunityView { Person=owner.PersonalityIdentity,Territoriality=p.Territoriality,Enforcement=p.Enforcement,Grudge=p.Grudge,Turn=game.Session.WorldTime.TurnCounter });
                culture.Bound(); game.DoSay(owner,other,"My view: " + RulesText(p.Territoriality,p.Enforcement,p.Grudge),RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_BRIEF);
            }
            else if (kind == "revise")
            {
                var personal = Personal(owner); var views = culture.Views.Where(v => game.Session.WorldTime.TurnCounter-v.Turn <= 2*WorldTime.TURNS_PER_DAY).ToList();
                int t = (personal.Territoriality+views.Sum(v=>v.Territoriality))/(views.Count+1);
                int e = (personal.Enforcement+views.Sum(v=>v.Enforcement))/(views.Count+1);
                int g = (personal.Grudge+views.Sum(v=>v.Grudge))/(views.Count+1);
                int hardening = Math.Max(-10,Math.Min(10,culture.Loss+ culture.Violations-culture.Cooperation));
                culture.Territoriality=Toward(culture.Territoriality,Clamp(t+hardening)); culture.Enforcement=Toward(culture.Enforcement,Clamp(e+hardening));
                culture.Grudge=Toward(culture.Grudge,Clamp(g+hardening)); culture.LastRevisionClock=game.Session.WorldTime.TurnCounter; culture.Revision++;
                culture.Cooperation=culture.Loss=culture.Violations=0; if(culture.Experiences.Count>0)culture.ReviewedEvent=culture.Experiences.Max(); culture.Experiences.Clear();
                LearnPolicy(owner,Snapshot(group,game.Session.WorldTime.TurnCounter),"chief's announced revision"); SayPolicy(game,owner,null,true);
            }
            else if (kind == "warn")
            {
                c.WarnedClock=clock; c.WarnedBy=owner.PersonalityIdentity; c.LastObservedClock=clock; c.ObservedGrace=0;
                SpeakCase(game,owner,other,c,"This is " + group.CommunityName + "'s private home. Leave within 30 awake observed turns or we will enforce our boundary.",true);
            }
            else if (kind == "defy")
            {
                bool newOffence=!c.Defied;
                c.Defied=true; c.Enforce=true;
                SpeakCase(game,owner,other,c,"You ignored our warning. Leave " + group.CommunityName + "'s home now!",true);
                if(newOffence)PublishOffence(game,owner,other,c,"community_trespass_defied");
            }
            else if (kind == "demand")
            {
                c.DemandClock=clock; c.DeadlineArea=CommunityShelterLocation.At(owner.Location);c.WarnedBy=owner.PersonalityIdentity;
                Request(game,owner,other,c);
                SpeakCase(game,owner,other,c,"You owe " + DebtText(c) + " to " + group.CommunityName +
                    ". Give matching usable items to me or our chief. You have 180 local turns; promises alone do not replace supplies.",true);
            }
            else if (kind == "expire" || kind == "refuse")
            {
                c.Expired=kind=="expire"; c.Refused=kind=="refuse"; c.Enforce=true;
                SpeakCase(game,owner,other,c,kind=="expire" ? (c.PersonName??"The offender")+" did not replace our supplies in the communicated response window." : "You refused to replace our supplies.",true);
                PublishOffence(game,owner,other,c,"community_restitution_failed");
            }
            if (c != null)
            {
                var stored=culture.Cases.Find(x=>x.Episode==c.Episode); if(stored!=null)culture.Cases.Remove(stored); culture.Cases.Add(c.Copy());culture.Bound();
            }
            game.SpendActorActionPoints(owner,Rules.BASE_ACTION_COST);
        }
        static int Toward(int value,int target) { return value+Math.Max(-5,Math.Min(5,target-value)); }
        static void PublishOffence(RogueGame game,Actor owner,Actor other,CommunityCase c,string kind)
        { PersonalitySystem.Report(game,new SignificantEvent(kind,other,owner,owner.Location.Map,other==null?owner.Location.Position:other.Location.Position,game.Session.WorldTime.TurnCounter,
            causeId:c.Episode) { CombatEpisode=c.Episode, CultureCase=Report(owner.SocialGroup,c,game.Session.WorldTime.TurnCounter,"witness") }); }
        internal static string DebtText(CommunityCase c)
        { return String.Join(", ",c.Losses.Where(l=>l.Remaining>0).Select(l=>l.Tampered ? "repair of the defense you tampered with" :
            l.Remaining + " " + (Models.Items[l.ModelId]==null ? "matching item(s)" : Models.Items[l.ModelId].SingleName))); }
        public static void Talk(RogueGame game,Actor speaker,Actor listener)
        {
            if (!Eligible(speaker)) return; Ensure(game,speaker); SayPolicy(game,speaker,listener,true);
            if (listener.SocialGroup == speaker.SocialGroup) return;
            var group=speaker.SocialGroup; var c=LocalCase(speaker,group.Identity,listener.PersonalityIdentity);
            if(c!=null&&c.HasLoss)
            {
                if(c.DemandClock<0) { c.DemandClock=speaker.Location.Map.LocalTime.TurnCounter;c.DeadlineArea=CommunityShelterLocation.At(speaker.Location);c.WarnedBy=speaker.PersonalityIdentity;Request(game,speaker,listener,c);
                    var stored=group.Culture.Cases.Find(x=>x.Episode==c.Episode);if(stored!=null) { stored.DemandClock=c.DemandClock;stored.DeadlineArea=c.DeadlineArea.Copy();stored.WarnedBy=c.WarnedBy; } }
                SpeakCase(game,speaker,listener,c,"Still owed: " + DebtText(c) + ". G returns matching usable items. The response window is 180 local turns after the demand; a promise is not repayment.");return;
            }
            var p=KnownPolicy(speaker); int attitude=NpcValues.KnownAttitude(speaker,listener.PersonalityIdentity,game.NpcContent.Personalities);
            if(p==null||p.Territoriality<35 || attitude < (p.Territoriality>=70?10:0)) return;
            XpdBase home=speaker.Location.Map.XpdBaseAt(speaker.Location.Position);
            if(home==null||home.CommunityIdentity!=group.Identity) return;
            if(c==null) { c=new CommunityCase { Episode=game.Session.NextPersonalityEventId(),Person=listener.PersonalityIdentity,PersonName=listener.UnmodifiedName,
                Kind="trespass",Home=CommunityShelterLocation.At(new Location(speaker.Location.Map,home.Cells.First())) };group.Culture.Cases.Add(c);group.Culture.Bound(); }
            c.PermissionUntil=speaker.Location.Map.LocalTime.TurnCounter+180;c.Resolved=false;c.Enforce=false;c.Defied=false;c.Guests.Clear();
            foreach(Actor companion in listener.PartyTree().Take(32))c.Guests.Add(companion.PersonalityIdentity);
            var previous=group.Culture.Cases.Find(x=>x.Episode==c.Episode);if(previous!=null)group.Culture.Cases.Remove(previous);
            group.Culture.Cases.Add(c.Copy());group.Culture.Bound();
            SpeakCase(game,speaker,listener,c,"Your accompanying household may visit our home for 180 local turns. This gives no permission to take our supplies.");
        }
        static void Request(RogueGame game,Actor owner,Actor other,CommunityCase c)
        {
            var request=new SignificantEvent("community_restitution_requested",owner,other,owner.Location.Map,owner.Location.Position,
                owner.Location.Map.LocalTime.TurnCounter,causeId:c.Episode) { CultureCase=Report(owner.SocialGroup,c,game.Session.WorldTime.TurnCounter,"heard demand") };
            PersonalitySystem.Report(game,request);c.RequestId=request.Id;
            foreach(var actor in owner.Location.Map.Actors)
                if(actor.Personality!=null)foreach(var report in actor.Personality.CaseReports)
                    if(report.Community==owner.SocialGroup.Identity&&report.Case.Episode==c.Episode)report.Case.RequestId=request.Id;
        }
        static bool DemandExpired(Actor owner,CommunityCase c)
        { return c.DemandClock>=0&&c.DeadlineArea!=null&&c.DeadlineArea.Matches(owner.Location.Map,c.DeadlineArea.Anchor)&&
            (owner.PersonalityIdentity==c.WarnedBy||owner.PersonalityIdentity==owner.SocialGroup.LeaderId)&&
            owner.Location.Map.LocalTime.TurnCounter-c.DemandClock>=180; }
    }
}
