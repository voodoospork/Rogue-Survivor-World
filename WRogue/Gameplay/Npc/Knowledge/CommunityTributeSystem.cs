using System;
using System.Linq;
using System.Collections.Generic;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class CommunityTributeSystem
    {
        internal const int ResponseTurns = 12, CollectionGrace = 180, MaxAccounts = 4;
        static bool Free(Actor a)
        { return RobberySystem.Human(a) && !a.InPlayerParty && CommunityDiplomacySystem.IsLeader(a) &&
            a.Faction.ID == (int)GameFactions.IDs.TheCivilians && a.Controller is CivilianAI &&
            ((CivilianAI)a.Controller).Order == null && a.SocialGroup.Home != null &&
            a.ResidentCommunity.TravelHome == null && a.CommunityRaid == null && a.Predation == null && !RobberySystem.HasState(a); }
        internal static bool Active(Actor a)
        { return a.SocialGroup != null && a.SocialGroup.HasTributeAccounts && a.SocialGroup.Tributes.Any(t => !t.Ended); }
        static bool RecognizesChief(Actor observer, Actor other)
        {
            if (other.SocialGroup == null || observer.Personality == null) return false;
            var known = observer.Personality.FindCommunity(other.SocialGroup.Identity);
            return known != null && known.Leader == other.UnmodifiedName &&
                CommunityDiplomacySystem.Recognizes(observer, other).Group == other.SocialGroup.Identity;
        }
        // Only awake people the chief sees, whose affiliations they actually know,
        // contribute. Neither side reads a remote roster, inventory or equipment.
        internal static int Strength(RogueGame game, Actor observer, Actor chief)
        {
            int result = RobberySystem.VisibleStrength(game, chief);
            foreach (var a in observer.Location.Map.Actors)
                if (a != chief && RobberySystem.Human(a) && !a.InPlayerParty &&
                    game.Rules.GridDistance(a.Location.Position, chief.Location.Position) <= 5 &&
                    NpcKnowledgeSystem.Visible(game, observer, a.Location) &&
                    CommunityDiplomacySystem.Recognizes(observer, a).Group == chief.SocialGroup.Identity)
                    result += RobberySystem.VisibleStrength(game, a);
            return result;
        }
        static CommunityTribute Account(Actor a, Guid id) { return a.SocialGroup.Tributes.Find(t => t.Id == id); }
        internal static CommunityTribute Between(Actor a, Actor b)
        { return a.SocialGroup.Tributes.Find(t => !t.Ended && (t.Collector == b.SocialGroup.Identity || t.Payer == b.SocialGroup.Identity)); }
        static bool Room(Actor a) { return a.SocialGroup.Tributes.Count(t => !t.Ended) < MaxAccounts; }
        static void Store(Actor a, CommunityTribute t)
        {
            var list = a.SocialGroup.Tributes; var old = list.Find(v => v.Id == t.Id); if (old != null) list.Remove(old);
            list.Add(t.Copy());
            while (list.Count > MaxAccounts) list.Remove(list.First(v => v.Ended));
        }
        internal static bool Opportunity(RogueGame game, Actor collector, Actor payer)
        {
            if (!game.Session.GamePreset.Bases || !Free(collector) || !Free(payer) ||
                collector.SocialGroup == payer.SocialGroup || collector.Location.Map != payer.Location.Map ||
                game.Rules.IsKnownEnemy(collector, payer) || game.Rules.IsKnownEnemy(payer, collector) ||
                !NpcKnowledgeSystem.Visible(game, collector, payer.Location) || !RecognizesChief(collector, payer) ||
                RobberySystem.Now(game) < collector.SocialGroup.NextTributeWorldTurn ||
                Between(collector, payer) != null || !Room(collector) ||
                game.Rules.IsActorSleepy(collector) || game.Rules.IsActorTired(collector) ||
                collector.HitPoints * 2 < game.Rules.ActorMaxHPs(collector) || !RobberySystem.ReadyWeapon(collector)) return false;
            var policy = CommunityCultureSystem.KnownPolicy(collector);
            if (policy == null || !policy.AggressionKnown || policy.Aggression != CommunityAggression.Raiders) return false;
            int own = Strength(game, collector, collector), rival = Strength(game, collector, payer);
            // A lone bandit can rob a person, but needs observed community backing for tribute.
            return own > RobberySystem.VisibleStrength(game, collector) &&
                own * (100 + RobberySystem.Desperation(game, collector)) >= rival * 175;
        }
        public static bool CanDemand(RogueGame game, Actor collector, Actor payer)
        { return Opportunity(game, collector, payer) && RobberySystem.Near(game, collector, payer) &&
            Between(payer, collector) == null && Room(payer); }
        static void Say(RogueGame game, Actor speaker, Actor listener, CommunityTribute t, string text)
        {
            CommunityRaidSystem.Announce(game, speaker, "community_tribute", text);
            // Speaking names both communities. Nearby observers learn the claim through
            // existing notes, never by receiving a live ledger or remote membership list.
            if (listener != null && listener.Personality != null)
            {
                int now = RobberySystem.Now(game);
                var known = listener.Personality.LearnCommunity(speaker.SocialGroup.Identity, speaker.SocialGroup.CommunityName, now,
                    "heard " + speaker.UnmodifiedName);
                known.Note(new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = "community_tribute",
                    EventTurn = now, LearnedTurn = now, Source = "heard " + speaker.UnmodifiedName, Text = text });
            }
        }
        public static void Demand(RogueGame game, Actor collector, Actor payer)
        {
            if (!CanDemand(game, collector, payer)) return;
            int now = RobberySystem.Now(game);
            var t = new CommunityTribute { Id = SimulationClock.NewIdentity(), Collector = collector.SocialGroup.Identity,
                Payer = payer.SocialGroup.Identity, CollectorName = collector.SocialGroup.CommunityName, PayerName = payer.SocialGroup.CommunityName,
                Units = Math.Min(4, Math.Max(2, Strength(game, collector, collector) / Math.Max(1, Strength(game, collector, payer)))),
                Due = now, GraceUntil = now + ResponseTurns, Updated = now };
            t.Remaining = t.Units; Store(collector, t); Store(payer, t);
            collector.SocialGroup.NextTributeWorldTurn = now + WorldTime.TURNS_PER_DAY;
            Say(game, collector, payer, t, t.CollectorName + " demands tribute from " + t.PayerName + ": " + t.Units +
                " food unit(s) each day, collected in person. Pay within " + CollectionGrace +
                " turns after agreement. Refusal may mean a feud; payment buys a truce between our communities.");
            PersonalitySystem.Report(game, new SignificantEvent("tribute_demand", collector, payer, collector.Location.Map, collector.Location.Position, now));
            game.SpendActorActionPoints(collector, Rules.BASE_ACTION_COST);
        }
        public static bool CanAnswer(RogueGame game, Actor payer, Actor collector)
        {
            if (!Free(payer) || !Free(collector) || !RobberySystem.Near(game, payer, collector) || payer.SocialGroup == collector.SocialGroup) return false;
            var t = Between(payer, collector);
            return t != null && t.Payer == payer.SocialGroup.Identity && Account(collector, t.Id) != null &&
                !Account(collector, t.Id).Ended && (!t.Accepted ? RobberySystem.Now(game) < t.GraceUntil : RobberySystem.Now(game) >= t.Due);
        }
        internal static bool WillAccept(RogueGame game, Actor payer, Actor collector)
        {
            int own = Strength(game, payer, payer), threat = Strength(game, payer, collector);
            // A chief protects a starving household and may resist even with worse equipment.
            bool starving = payer.Location.Map.Actors.Any(a => a.SocialGroup == payer.SocialGroup &&
                RobberySystem.Human(a) && NpcKnowledgeSystem.Visible(game, payer, a.Location) && game.Rules.IsActorStarving(a));
            return !starving && threat * 100 > own * (120 + PersonalitySystem.Bias(payer, DecisionKind.Courage)) &&
                CommunityAggressionSystem.Food(game, payer) > 1;
        }
        static void End(RogueGame game, Actor collector, Actor payer, CommunityTribute t, string reason, bool enforce)
        {
            t.Ended = true; t.Updated = RobberySystem.Now(game); Store(collector, t); Store(payer, t);
            Say(game, collector, payer, t, t.CollectorName + "'s tribute demand on " + t.PayerName + " ended: " + reason + ".");
            var policy = CommunityCultureSystem.KnownPolicy(collector);
            if (!enforce || policy == null || policy.Enforcement < 65 ||
                Strength(game, collector, collector) <= Strength(game, collector, payer)) return;
            if (collector.SocialGroup.Diplomacy == null) collector.SocialGroup.Diplomacy = new CommunityDiplomacyState();
            var relation = collector.SocialGroup.Diplomacy.Get(PoliticalIdentity.Of(payer.SocialGroup));
            relation.Feud = true; relation.Revision++; relation.DeclaredTurn = RobberySystem.Now(game);
            relation.FoundingCause = new DiplomacyIncident { Kind = "tribute_refused", Explanation = reason,
                Turn = RobberySystem.Now(game), Source = "chief's own extortion demand", Confidence = 100 };
            game.DoSay(collector, payer, t.CollectorName + " declares a feud with " + t.PayerName + " over refused tribute!",
                RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_DANGER);
            CommunityDiplomacySystem.Publish(game, collector, CommunityDiplomacySystem.Message(collector.SocialGroup, relation,
                RobberySystem.Now(game)), "community_feud");
        }
        public static void Answer(RogueGame game, Actor payer, Actor collector, bool accept)
        {
            if (!CanAnswer(game, payer, collector)) return;
            var t = Between(payer, collector).Copy(); int now = RobberySystem.Now(game);
            if (!accept)
            {
                game.DoSay(payer, collector, "We, " + t.PayerName + ", refuse tribute.", RogueGame.Sayflags.IS_FREE_ACTION);
                End(game, collector, payer, t, t.PayerName + " refused to pay", true);
            }
            else
            {
                if (!t.Accepted)
                { t.Accepted = true; t.GraceUntil = now + CollectionGrace;
                    Say(game, payer, collector, t, t.PayerName + " agreed to pay " + t.CollectorName + " daily tribute of " + t.Units + " food unit(s)."); }
                // Keep one real usable food unit for the chief. No resident backpack or
                // unobserved communal stock is drained on behalf of the agreement.
                int spare = Math.Max(0, CommunityAggressionSystem.Food(game, payer) - 1);
                int received = RobberySystem.Transfer(game, payer, collector, RobberyGoods.Food, Math.Min(spare, t.Remaining));
                t.Remaining -= received; t.Updated = now;
                Say(game, payer, collector, t, t.PayerName + " handed " + received + " food unit(s) to " + t.CollectorName +
                    "; " + t.Remaining + " remain for this collection.");
                if (t.Remaining == 0)
                { t.Due = now + WorldTime.TURNS_PER_DAY; t.Remaining = t.Units; t.GraceUntil = t.Due + CollectionGrace; }
                Store(collector, t); Store(payer, t);
            }
            game.SpendActorActionPoints(payer, Rules.BASE_ACTION_COST);
        }
        internal static ActorAction Select(RogueGame game, Actor actor, IList<Actor> visible)
        {
            if (!Free(actor) || visible == null) return null;
            foreach (var other in visible)
            {
                if (!Free(other) || other.SocialGroup == actor.SocialGroup) continue;
                var t = Between(actor, other);
                if (t != null && RobberySystem.Near(game, actor, other))
                {
                    if (t.Payer == actor.SocialGroup.Identity && CanAnswer(game, actor, other))
                    {
                        bool accept = WillAccept(game, actor, other);
                        // During the stated grace period a short household can seek food;
                        // a full collector cannot consume the payer's whole day in zero transfers.
                        if (t.Accepted && RobberySystem.Now(game) < t.GraceUntil &&
                            (CommunityAggressionSystem.Food(game, actor) <= 1 || !actor.Inventory.Items.Any(i =>
                                RobberySystem.Usable(game, actor, i, RobberyGoods.Food) && other.Inventory.ReleasedCargoCapacity(i) > 0))) continue;
                        return new ActionTribute(actor, game, other, true, accept);
                    }
                    if (CanExpire(game, actor, other)) return new ActionTribute(actor, game, other, expire: true);
                }
                if (CanDemand(game, actor, other)) return new ActionTribute(actor, game, other);
            }
            return null;
        }
        internal static Actor Approach(RogueGame game, Actor actor, IList<Actor> visible)
        {
            if (!Free(actor) || visible == null || game.Rules.IsActorHungry(actor) || game.Rules.IsActorSleepy(actor)) return null;
            foreach (var other in visible)
            {
                if (!Free(other) || other.SocialGroup == actor.SocialGroup || !RecognizesChief(actor, other) ||
                    game.Rules.IsKnownEnemy(actor, other)) continue;
                var t = Between(actor, other);
                if (game.Rules.GridDistance(actor.Location.Position, other.Location.Position) > 1 &&
                    ((t != null && t.Collector == actor.SocialGroup.Identity && RobberySystem.Now(game) >= t.Due) || Opportunity(game, actor, other)))
                    return other;
            }
            return null;
        }
        internal static bool CanExpire(RogueGame game, Actor collector, Actor payer)
        {
            if (!Free(collector) || !Free(payer) || !RobberySystem.Near(game, collector, payer)) return false;
            var t = Between(collector, payer);
            return t != null && t.Collector == collector.SocialGroup.Identity && RobberySystem.Now(game) >= t.GraceUntil;
        }
        internal static void Expire(RogueGame game, Actor collector, Actor payer)
        {
            if (!CanExpire(game, collector, payer)) return;
            var t = Between(collector, payer).Copy();
            End(game, collector, payer, t, t.Accepted ? "the collection remained unpaid" : "no answer was received", t.Accepted);
            game.SpendActorActionPoints(collector, Rules.BASE_ACTION_COST);
        }
        internal static void Report(RogueGame game, Actor speaker, Actor listener)
        {
            int now = RobberySystem.Now(game);
            if (speaker.SocialGroup.Tributes.Count == 0)
            { game.DoSay(speaker, listener, "We have no tribute accounts.", RogueGame.Sayflags.IS_FREE_ACTION); return; }
            foreach (var t in speaker.SocialGroup.Tributes)
            {
                string text = t.CollectorName + " / " + t.PayerName + ": " + (t.Ended ? "tribute ended" : !t.Accepted ?
                    "demand awaiting an answer" : t.Units + " food unit(s) each day; next due turn " + t.Due +
                    "; " + t.Remaining + " unit(s) recorded for that installment") + ". Account updated turn " + t.Updated + ".";
                game.DoSay(speaker, listener, text, RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_BRIEF);
                var known = listener.Personality.LearnCommunity(speaker.SocialGroup.Identity, speaker.SocialGroup.CommunityName, now,
                    "reported by " + speaker.UnmodifiedName);
                known.Note(new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = "community_tribute",
                    EventTurn = t.Updated, LearnedTurn = now, Reported = true, Source = "reported by " + speaker.UnmodifiedName, Text = text });
            }
        }
        internal static bool Truce(Actor a, Actor b)
        { return a.SocialGroup != null && b.SocialGroup != null && a.SocialGroup.HasTributeAccounts && a.SocialGroup.Tributes.Any(t => !t.Ended &&
            (t.Collector == b.SocialGroup.Identity || t.Payer == b.SocialGroup.Identity)); }
    }
}
