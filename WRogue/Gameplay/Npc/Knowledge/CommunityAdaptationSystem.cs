using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class CommunityAdaptationSystem
    {
        public static bool Enabled(RogueGame game) { var p = game.Session.GamePreset; return p.Bases && p.NpcPersonalitiesEnabled &&
            (p.OrganicCommunities || (p.Name == "World" && p.InitialCommunities)); }
        public static int Turn(Actor a) { return a.ResidentCommunity.Clock(a); }
        public static bool Arrived(Actor actor, CommunityShelterLocation place)
        { return place != null && place.Matches(actor.Location.Map, actor.Location.Position) &&
            actor.Location.Map.GetTileAt(actor.Location.Position).IsInside &&
            !(actor.Location.Map.GetMapObjectAt(actor.Location.Position) is DoorWindow) && actor.Location.Map.GetExitAt(actor.Location.Position) == null; }
        static bool Civilian(RogueGame game, Actor a) { return a != null && !a.IsDead && !a.Model.Abilities.IsUndead &&
            a.Faction == game.GameFactions.TheCivilians && a.Model.Abilities.IsIntelligent; }
        static bool Free(RogueGame game, Actor a) { var ai = a.Controller as OrderableAI; return Civilian(game, a) && !a.IsPlayer &&
            !a.IsSleeping && !a.InPlayerParty && ai != null && ai.Order == null && !game.Rules.IsActorStarving(a) &&
            !game.Rules.IsActorTired(a) && !game.Rules.IsActorSleepy(a); }
        static bool Talkable(RogueGame game, Actor a, Actor b) { return b != null && !a.IsDead && !b.IsDead && !a.IsSleeping && !b.IsSleeping &&
            a.Location.Map == b.Location.Map && game.Rules.GridDistance(a.Location.Position, b.Location.Position) <= 6 &&
            game.Rules.StdDistance(a.Location.Position, b.Location.Position) <= b.AudioRange && NpcKnowledgeSystem.Visible(game, a, b.Location) &&
            !game.Rules.AreEnemies(a, b, false) && !game.Rules.IsKnownEnemy(a, b) && !game.Rules.IsKnownEnemy(b, a); }
        static int Bias(Actor a, DecisionKind kind) { return PersonalitySystem.Bias(a, kind); }
        static int Attitude(RogueGame game, Actor a, Actor b) { return NpcValues.KnownAttitude(a, b.PersonalityIdentity, game.NpcContent.Personalities); }
        public static void Upgrade(RogueGame game, Actor actor)
        {
            if (!Enabled(game) || !Civilian(game, actor) || actor.Community != null || actor.SocialGroup == null || actor.SocialGroup.Home == null) return;
            SocialGroup group = actor.SocialGroup;
            if (group.Tribe != null || group.FactionId != game.GameFactions.TheCivilians.ID) return;
            Actor root = actor.GroupRoot;
            if (root.IsPlayer || root.Faction != game.GameFactions.TheCivilians) return;
            var residents = new List<Actor>(root.PartyTree());
            group.Tribe = new TribeState { FoundedTurn = Turn(actor) };
            group.Members.Clear();
            // Release the old civic hierarchy, preserving every edge within a player household.
            foreach (Actor member in residents)
                if (member != root && (!member.InPlayerParty || member.IsPlayer)) member.ReleaseCommunityCommand();
            root.ReleaseCommunityCommand();
            foreach (Actor member in residents) { member.SetCommunityHousehold(group); member.ResidentCommunity.KnownHome = group.Home.Place.Copy(); }
            root.ResidentCommunity.RememberResidents(residents);
            foreach (Actor player in residents.Where(a => a.IsPlayer && a.CommunityMembership != null))
                player.RestoreCommandGroupForMembership();
            game.InvalidateCommunityHomes();
        }
        public static void Observe(RogueGame game, Actor owner, IList<Actor> visible)
        {
            if (!Enabled(game) || !Civilian(game, owner)) return;
            Upgrade(game, owner);
            var state = owner.ResidentCommunity;
            int turn = Turn(owner);
            bool hungry = game.Rules.IsActorHungry(owner) && !NpcFoodSupply.HasFood(game, owner);
            if (hungry) { state.UnmetHungerTurn = turn; if (state.HungerSince < 0) state.HungerSince = turn; }
            else state.HungerSince = -1;
            Zone refuge = Zone.BuildingAt(owner.Location);
            if (visible != null) foreach (Actor other in visible)
            {
                if (game.Rules.IsKnownEnemy(owner, other)) { state.KnownDangerTurn = turn; continue; }
                if (!Civilian(game, other) || other.IsPlayer || other.IsSleeping || other == owner || refuge == null ||
                    other.Location.Map != owner.Location.Map || !refuge.Bounds.Contains(other.Location.Position)) continue;
                TribeContact contact = state.Contact(other.PersonalityIdentity);
                if (contact == null) { contact = new TribeContact { Person = other.PersonalityIdentity }; state.Contacts.Add(contact);
                    if (state.Contacts.Count > 32) state.Contacts.RemoveAt(0); }
                if (contact.Refuge == null || !contact.Refuge.Matches(owner.Location.Map, owner.Location.Position))
                { contact.Refuge = CommunityShelterLocation.At(owner.Location); contact.FirstTurn = turn; contact.ObservedTurns = 0; }
                if (turn > contact.LastTurn) contact.ObservedTurns += Math.Min(1, turn - contact.LastTurn);
                contact.LastTurn = turn;
            }
            SocialGroup group = owner.Community;
            if (group == null) return;
            ResolvedCommunityHome home = game.ResolveCommunityHome(owner);
            if (home != null && home.Map == owner.Location.Map && owner.PersonalityIdentity == group.LeaderId)
            {
                foreach (Point cell in home.Claim.Cells)
                {
                    if (!NpcKnowledgeSystem.Visible(game, owner, new Location(home.Map, cell))) continue;
                    // Only the observer's inventory is reported. Ground observations are dated and decay-aware.
                    Inventory items = home.Map.GetItemsAt(cell);
                    int nutrition = Nutrition(game, items, owner.Location.Map.LocalTime.TurnCounter + 2 * WorldTime.TURNS_PER_DAY);
                    TribeFoodObservation stock = group.Tribe.Food.Find(f => f.Place.MapSeed == home.Map.Seed && f.Place.MapName == home.Map.Name && f.Place.Anchor == cell);
                    if (stock == null && nutrition == 0) continue;
                    if (stock == null) { stock = new TribeFoodObservation { Place = CommunityShelterLocation.At(new Location(home.Map, cell)) };
                        group.Tribe.Food.Add(stock); if (group.Tribe.Food.Count > 64) group.Tribe.Food.RemoveAt(0); }
                    stock.Nutrition = nutrition; stock.Turn = turn; stock.Observer = owner.PersonalityIdentity;
                }
            }
        }
        static int Nutrition(RogueGame game, Inventory inventory, int turn)
        { int total = 0; if (inventory != null) foreach (Item item in inventory.Items) { var food = item as ItemFood;
                if (food != null && !game.Rules.IsFoodSpoiled(food, turn)) total += game.Rules.FoodItemNutrition(food, turn) * food.Quantity; } return total; }
        static bool Cooperation(Actor observer, Actor candidate)
        {
            if (observer.Personality == null) return false;
            if (observer.Personality.Knowledge.Facts.Exists(f => f.Confidence >= 40 &&
                (f.Kind == "helped" || f.Kind == "promise_kept") &&
                ((f.SubjectId == observer.PersonalityIdentity && f.OtherId == candidate.PersonalityIdentity) ||
                    (f.OtherId == observer.PersonalityIdentity && f.SubjectId == candidate.PersonalityIdentity)))) return true;
            foreach (NpcFact defence in observer.Personality.Knowledge.Facts)
                if (defence.Kind == "defended_person" && defence.Confidence >= 40 &&
                    (defence.SubjectId == observer.PersonalityIdentity || defence.SubjectId == candidate.PersonalityIdentity))
                    if (observer.Personality.Knowledge.Facts.Exists(attack => attack.Kind == "attack" && attack.Confidence >= 40 &&
                        attack.SubjectId == (defence.SubjectId == observer.PersonalityIdentity ? candidate.PersonalityIdentity : observer.PersonalityIdentity) &&
                        attack.OtherId == defence.OtherId && defence.EventTurn - attack.EventTurn >= 0 && defence.EventTurn - attack.EventTurn <= 30)) return true;
            return false;
        }
        static bool Qualified(Actor observer, Actor candidate)
        { var c = observer.ResidentCommunity.Contact(candidate.PersonalityIdentity); return Cooperation(observer, candidate) ||
            c != null && c.LastTurn == Turn(observer) && c.ObservedTurns >= 60; }
        internal static Actor RefugePartner(RogueGame game, Actor owner, IList<Actor> visible)
        {
            if (!Enabled(game) || !Free(game, owner) || owner.Community != null || owner.HasLeader || visible == null) return null;
            foreach (Actor other in visible)
            {
                var contact = owner.ResidentCommunity.Contact(other.PersonalityIdentity);
                if (other == owner || !Free(game, other) || other.HasLeader || other.Community != null || contact == null ||
                    contact.LastTurn != Turn(owner) || Turn(owner) < contact.DeclinedUntil || contact.Refuge == null ||
                    !contact.Refuge.Matches(owner.Location.Map, owner.Location.Position) || game.Rules.IsKnownEnemy(owner, other)) continue;
                return other;
            }
            return null;
        }
        internal static bool ReadyToDiscuss(Actor a, Actor b) { return Qualified(a, b) && Qualified(b, a); }
        static bool HouseholdAvailable(Actor root, SocialGroup joining)
        { foreach (Actor member in root.PartyTree()) if (member.IsDead || member.Model.Abilities.IsUndead ||
                member.Community != null && member.Community != joining) return false; return root.Leader == null; }
        public static int SleepingSpace(RogueGame game, Actor observer, Map map, IEnumerable<Point> cells)
        {
            var allowed = new HashSet<Point>(cells, djack.RogueSurvivor.Data.PointComparer.Instance); var seen = new HashSet<Point>(djack.RogueSurvivor.Data.PointComparer.Instance); var queue = new Queue<Point>();
            Point start = observer.Location.Position;
            if (observer.Location.Map != map || !allowed.Contains(start)) return 0;
            queue.Enqueue(start); seen.Add(start); int count = 0;
            while (queue.Count > 0)
            {
                Point p = queue.Dequeue(); XpdBase claim = map.XpdBaseAt(p);
                bool storage = claim != null && ((claim.FoodRoom.HasValue && claim.FoodRoom.Value.Contains(p)) ||
                    (claim.WeaponRoom.HasValue && claim.WeaponRoom.Value.Contains(p)));
                bool entrance = map.GetExitAt(p) != null || map.GetMapObjectAt(p) is DoorWindow;
                foreach (Direction d in Direction.COMPASS) { Point n = p + d;
                    if (map.IsInBounds(n) && (map.GetExitAt(n) != null || map.GetMapObjectAt(n) is DoorWindow)) entrance = true; }
                bool safe = true; Actor occupant = map.GetActorAt(p); Inventory items = map.GetItemsAt(p);
                if (NpcKnowledgeSystem.Visible(game, observer, new Location(map, p)))
                { if (occupant != null && game.Rules.IsKnownEnemy(observer, occupant)) safe = false;
                    if (items != null && items.HasItemMatching(i => i is ItemTrap && ((ItemTrap)i).IsActivated)) safe = false; }
                if (map.GetTileAt(p).IsInside && map.IsWalkable(p.X, p.Y) && !storage && !entrance && safe) count++;
                foreach (Direction d in new[] { Direction.N, Direction.E, Direction.S, Direction.W })
                {
                    Point n = p + d; if (!allowed.Contains(n) || seen.Contains(n)) continue;
                    MapObject obj = map.GetMapObjectAt(n);
                    if (!map.IsWalkable(n.X, n.Y) && !(obj is DoorWindow && !((DoorWindow)obj).IsBarricaded)) continue;
                    seen.Add(n); queue.Enqueue(n);
                }
            }
            return count;
        }
        public static int FoodShortfall(RogueGame game, Actor chief, Actor candidate = null)
        {
            int turn = Turn(chief), demand = chief.ResidentCommunity.UntrackedResidents * 2 * WorldTime.TURNS_PER_DAY;
            var ids = new HashSet<Guid>(chief.ResidentCommunity.KnownResidents); ids.Add(chief.PersonalityIdentity);
            if (candidate != null) foreach (Actor person in candidate.PartyTree()) ids.Add(person.PersonalityIdentity);
            foreach (Guid id in ids)
            {
                NpcKnownPerson person = chief.Personality == null ? null : chief.Personality.Knowledge.Person(id);
                KnownCommunity community = chief.Personality == null || chief.Community == null ? null : chief.Personality.FindCommunity(chief.Community.Identity);
                var membership = community == null ? null : community.Members.Find(m => m.Id == id);
                if (person != null && person.Dead && person.Confidence >= 40 || membership != null && (membership.Status == "died" || membership.Status == "dead" || (membership.Status ?? "").StartsWith("left"))) continue;
                Actor resident = chief.CommunityResidents.FirstOrDefault(a => a.PersonalityIdentity == id) ??
                    (candidate == null ? null : candidate.PartyTree().FirstOrDefault(a => a.PersonalityIdentity == id));
                if (resident == null) { demand += 2 * WorldTime.TURNS_PER_DAY; continue; } // Missing contact is not a known death or departure.
                if (!resident.Model.Abilities.HasToEat) continue;
                var state = resident.ResidentCommunity;
                // The chief's own needs are known; others only use what they actually reported.
                bool reported = state.FoodReportedTo == chief.PersonalityIdentity &&
                    (state.FoodReportDate >= 0 ? game.Session.WorldTime.TurnCounter - state.FoodReportDate : turn - state.FoodReportTurn) <= 180;
                int own = resident == chief ? Nutrition(game, resident.Inventory, chief.Location.Map.LocalTime.TurnCounter + 2 * WorldTime.TURNS_PER_DAY) : reported ? state.ReportedFood : 0;
                int need = resident == chief ? Math.Max(0, game.Rules.ActorMaxFood(chief) - chief.FoodPoints) + 2 * WorldTime.TURNS_PER_DAY :
                    reported ? state.ReportedDemand : 2 * WorldTime.TURNS_PER_DAY;
                demand += Math.Max(0, need - own);
            }
            int known = chief.Community == null ? 0 : chief.Community.Tribe.Food.Where(f => f.Observer == chief.PersonalityIdentity && turn - f.Turn <= 30).Sum(f => f.Nutrition);
            return Math.Max(0, demand - known);
        }
        static int KnownResidentCount(Actor chief)
        {
            KnownCommunity knowledge = chief.Personality == null || chief.Community == null ? null : chief.Personality.FindCommunity(chief.Community.Identity);
            var known = chief.ResidentCommunity.KnownResidents;
            known.RemoveAll(id => { NpcKnownPerson person = chief.Personality == null ? null : chief.Personality.Knowledge.Person(id);
                CommunityMember member = knowledge == null ? null : knowledge.Members.Find(m => m.Id == id);
                return person != null && person.Dead && person.Confidence >= 40 || member != null &&
                    (member.Status == "died" || member.Status == "dead" || (member.Status ?? "").StartsWith("left")); });
            return Math.Max(chief.CommunityResidents.Count(a => !a.IsDead), known.Count + chief.ResidentCommunity.UntrackedResidents);
        }
        public static bool Admission(RogueGame game, Actor chief, Actor candidate, out string reason)
        {
            reason = null;
            if (!Talkable(game, chief, candidate) || !HouseholdAvailable(candidate, chief.Community)) reason = "We cannot admit a hostile or divided household.";
            ResolvedCommunityHome home = game.ResolveCommunityHome(chief);
            if (home == null || home.Map != chief.Location.Map) reason = "We need a secure home before admitting residents.";
            int count = KnownResidentCount(chief) + candidate.PartyTree().Count(a => !a.IsDead);
            int space = home == null ? 0 : SleepingSpace(game, chief, home.Map, home.Claim.Cells);
            if (space < count) reason = "There is no safe sleeping space for your whole household.";
            foreach (Actor member in chief.CommunityResidents) foreach (Actor guest in candidate.PartyTree())
                if (game.Rules.IsKnownEnemy(member, guest) || game.Rules.IsKnownEnemy(guest, member)) reason = "A resident has an unresolved hostile dispute with your household.";
            if (reason != null) return false;
            int shortfall = FoodShortfall(game, chief, candidate), horizon = 2 * WorldTime.TURNS_PER_DAY;
            int pressure = Math.Min(40, shortfall * 40 / Math.Max(1, horizon * count));
            int score = 12 + Attitude(game, chief, candidate) + Bias(chief, DecisionKind.Group) / 2 + Bias(chief, DecisionKind.Compassion) +
                (Cooperation(chief, candidate) ? 20 : 0) + (chief.Personality.Knowledge.Person(candidate.PersonalityIdentity) != null &&
                    chief.Personality.Knowledge.Person(candidate.PersonalityIdentity).Danger > 0 &&
                    Turn(chief) - chief.Personality.Knowledge.Person(candidate.PersonalityIdentity).ThreatTurn < 60 ? 15 : 0) -
                pressure - (space < 3 * count ? 15 : 0) + CommunityCultureSystem.SocialModifier(chief);
            if (score < 0) reason = shortfall > 0 ? "We are short of food for the next two days; I cannot risk another household." : "Our home is too crowded and trust is too weak.";
            return reason == null;
        }
        static bool CandidateConsent(RogueGame game, Actor candidate, Actor proposer)
        {
            if (!Talkable(game, candidate, proposer) || !HouseholdAvailable(candidate, proposer.Community)) return false;
            int score = 15 + Attitude(game, candidate, proposer) + Bias(candidate, DecisionKind.Group) + Bias(candidate, DecisionKind.Compassion) / 2 +
                (Cooperation(candidate, proposer) ? 20 : 0) + (Turn(candidate) - candidate.ResidentCommunity.KnownDangerTurn < 60 ? 15 : 0);
            if (proposer.Community != null) { ResolvedCommunityHome home = game.ResolveCommunityHome(proposer);
                var offer = candidate.ResidentCommunity.Proposal;
                if (home == null || offer == null || offer.ReportedSpace < offer.ReportedResidents) return false;
                score -= Math.Min(30, offer.ReportedShortfall * 30 / Math.Max(1, 2 * WorldTime.TURNS_PER_DAY * offer.ReportedResidents)); }
            return score >= 0;
        }
        static Actor FindVisible(IList<Actor> visible, Guid id) { return visible == null ? null : visible.FirstOrDefault(a => a.PersonalityIdentity == id); }
        public static ActorAction Select(RogueGame game, Actor owner, IList<Actor> visible)
        {
            if (!Enabled(game) || !Free(game, owner) || visible == null) return null;
            foreach (Actor seen in visible) if (game.Rules.IsKnownEnemy(owner, seen)) return null;
            int turn = Turn(owner); var state = owner.ResidentCommunity; var offer = state.Proposal;
            if (offer != null && turn > (offer.Proposer == owner.PersonalityIdentity || offer.CandidateExpiresTurn == 0 ?
                offer.ExpiresTurn : offer.CandidateExpiresTurn)) state.Proposal = offer = null;
            if (offer != null)
            {
                Actor other = FindVisible(visible, offer.Proposer == owner.PersonalityIdentity ? offer.Candidate : offer.Proposer);
                if (Talkable(game, owner, other))
                {
                    if (!offer.Answered && offer.Candidate == owner.PersonalityIdentity) return new ActionCommunityDecision(owner, game, other, "answer");
                    if (offer.Answered && offer.Accepted && offer.Proposer == owner.PersonalityIdentity) return new ActionCommunityDecision(owner, game, other, offer.Admission ? "admit" : "found");
                }
            }
            SocialGroup group = owner.Community;
            if (group != null)
            {
                if (owner.CommunityChief == null && !group.Tribe.Dissolved) return new ActionCommunityDecision(owner, game, null, "succession");
                if (state.ProposedMove != null) return new ActionCommunityDecision(owner, game, owner.CommunityChief, "move_answer");
                if (state.TravelHome != null && owner.CommunityChief == owner && Arrived(owner, state.TravelHome))
                    return turn >= state.RelocationRetryTurn ? new ActionCommunityDecision(owner, game, null, "secure") : null;
                if (state.TravelHome != null) return null;
                if (state.RemainAtOldHome) return new ActionCommunityDecision(owner, game, null, "depart");
                if (owner.CommunityChief != owner && owner.Leader == null && owner.CommunityChief != null &&
                    Talkable(game, owner, owner.CommunityChief) && (state.FoodReportedTo != owner.CommunityChief.PersonalityIdentity || turn - state.FoodReportTurn >= 180))
                    return new ActionCommunityDecision(owner, game, owner.CommunityChief, "food_report");
                if (state.HungerSince >= 0 && turn - state.HungerSince >= 180 && turn >= state.NextReviewTurn &&
                    (Bias(owner, DecisionKind.Group) + Bias(owner, DecisionKind.Compassion) < -10 ||
                     owner.CommunityChief != null && Attitude(game, owner, owner.CommunityChief) < -30))
                    return new ActionCommunityDecision(owner, game, null, "depart");
                if (owner.PersonalityIdentity != group.LeaderId)
                {
                    int attachment = Attitude(game, owner, owner.CommunityChief ?? owner) + Bias(owner, DecisionKind.Group) + Bias(owner, DecisionKind.Compassion);
                    bool hardship = state.HungerSince >= 0 && turn - state.HungerSince >= 180 ||
                        turn - state.KnownDangerTurn <= 60 || owner.CommunityChief != null && Attitude(game, owner, owner.CommunityChief) < -30;
                    if (hardship && turn >= state.NextReviewTurn && attachment < -10)
                        return new ActionCommunityDecision(owner, game, null, "depart");
                    return null;
                }
                if (turn < group.Tribe.NextReviewTurn) return null;
                group.Tribe.NextReviewTurn = turn + 30;
                bool observedHunger = visible.Any(a => a.Community == group &&
                    owner.Personality.Knowledge.Person(a.PersonalityIdentity) != null &&
                    owner.Personality.Knowledge.Person(a.PersonalityIdentity).FoodNeed > 0 &&
                    owner.Location.Map.LocalTime.TurnCounter - owner.Personality.Knowledge.Person(a.PersonalityIdentity).FoodNeedTurn <= 60);
                observedHunger |= game.Rules.IsActorHungry(owner) && !NpcFoodSupply.HasFood(game, owner);
                bool shortage = observedHunger && FoodShortfall(game, owner) > 0;
                if (shortage) { if (group.Tribe.ShortageReviews++ == 0) group.Tribe.ShortageSince = turn; }
                else { group.Tribe.ShortageReviews = 0; group.Tribe.WarningTurn = -1; }
                if (shortage && group.Tribe.ShortageReviews >= 3 && turn - group.Tribe.ShortageSince >= 60)
                {
                    Actor target = visible.Where(a => a.Community == group && a != owner && a.Leader == null && Talkable(game, owner, a))
                        .OrderBy(a => Attitude(game, owner, a) + (Cooperation(owner, a) ? 30 : 0) +
                            (game.Rules.IsActorStarving(a) || a.HitPoints < game.Rules.ActorMaxHPs(a) / 2 ? Bias(owner, DecisionKind.Compassion) * 2 : 0))
                        .ThenBy(a => a.PersonalityIdentity).FirstOrDefault();
                    if (target != null && group.Tribe.WarningTurn < 0) return new ActionCommunityDecision(owner, game, target, "warning");
                    if (target != null && target.ResidentCommunity.EvictionWarningFrom == owner.PersonalityIdentity &&
                        target.ResidentCommunity.EvictionWarningTurn >= 0 && turn - target.ResidentCommunity.EvictionWarningTurn >= 180 &&
                        turn - group.Tribe.LastEvictionTurn >= 180 && EvictionScore(game, owner, target) >= 0)
                        return new ActionCommunityDecision(owner, game, target, "evict");
                }
                if (game.ResolveCommunityHome(owner) == null || group.Tribe.ShortageReviews >= 3 || turn - state.KnownDangerTurn < 60)
                {
                    var alternative = KnownAlternative(game, owner);
                    if (alternative != null && group.Tribe.Relocation == null) return new ActionCommunityDecision(owner, game, null, "relocate");
                }
            }
            if (turn < state.NextReviewTurn || owner.HasLeader) return null;
            state.NextReviewTurn = turn + 30;
            if (state.Proposal != null) return null;
            foreach (Actor candidate in visible.OrderBy(a => a.PersonalityIdentity))
            {
                if (candidate == owner || !Free(game, candidate) || candidate.Community != null || !Talkable(game, owner, candidate) ||
                    !HouseholdAvailable(candidate, group) || candidate.ResidentCommunity.Proposal != null) continue;
                var contact = state.Contact(candidate.PersonalityIdentity);
                if (contact != null && turn < contact.DeclinedUntil) continue;
                if (group != null) { if (owner.PersonalityIdentity != group.LeaderId) continue; }
                else if (!Qualified(owner, candidate) || !Qualified(candidate, owner)) continue;
                string reason; var cells = group == null ? XpdBasePlanner.Preview(owner.Location.Map, owner, game.Rules, out reason) : null;
                if (group == null && (cells == null || SleepingSpace(game, owner, owner.Location.Map, cells) < owner.PartyTree().Count() + candidate.PartyTree().Count())) continue;
                return new ActionCommunityDecision(owner, game, candidate, "propose");
            }
            return null;
        }
        static int EvictionScore(RogueGame game, Actor chief, Actor target)
        {
            int score = 20 + Math.Min(40, FoodShortfall(game, chief) * 40 / Math.Max(1, 2 * WorldTime.TURNS_PER_DAY * chief.CommunityResidents.Count())) +
                Bias(chief, DecisionKind.Group) / 2 - Bias(chief, DecisionKind.Compassion) * 2 - Attitude(game, chief, target) - (Cooperation(chief, target) ? 30 : 0);
            if (game.Rules.IsActorStarving(target) || target.HitPoints < game.Rules.ActorMaxHPs(target) / 2) score -= Math.Max(0, Bias(chief, DecisionKind.Compassion));
            return score;
        }
        static bool HungryEvidence(RogueGame game, Actor chief)
        {
            int turn = Turn(chief);
            foreach (Actor member in chief.CommunityResidents)
            {
                if (member.IsDead || !NpcKnowledgeSystem.Visible(game, chief, member.Location)) continue;
                if (member == chief) { if (game.Rules.IsActorHungry(chief) && !NpcFoodSupply.HasFood(game, chief)) return true; continue; }
                var person = chief.Personality.Knowledge.Person(member.PersonalityIdentity);
                if (person != null && person.FoodNeed > 0 && person.FoodConfidence >= 40 && chief.Location.Map.LocalTime.TurnCounter - person.FoodNeedTurn <= 60) return true;
            }
            return false;
        }
        static bool EvictionEligible(RogueGame game, Actor chief, Actor target)
        {
            if (chief.Community == null || target == null) return false;
            int turn = Turn(chief); var tribe = chief.Community.Tribe; var state = target.ResidentCommunity;
            return state.EvictionWarningFrom == chief.PersonalityIdentity && state.EvictionWarningTurn >= 0 &&
                turn - state.EvictionWarningTurn >= 180 && tribe.ShortageReviews >= 3 && turn - tribe.ShortageSince >= 60 &&
                turn - tribe.LastEvictionTurn >= 180 && HungryEvidence(game, chief) && FoodShortfall(game, chief) > 0 && EvictionScore(game, chief, target) >= 0;
        }
        static CommunityShelterLocation KnownAlternative(RogueGame game, Actor owner)
        {
            if (owner.Personality == null) return null;
            foreach (var place in owner.Personality.Knowledge.Places.OrderByDescending(p => p.SeenTurn))
            {
                if (place.Kind != "shelter" || place.Risk > 0 || Turn(owner) - place.SeenTurn > 2 * WorldTime.TURNS_PER_DAY ||
                    place.Place.Map == null || owner.Community != null && owner.Community.Home != null && owner.Community.Home.Place.Matches(place.Place.Map, place.Place.Position)) continue;
                return CommunityShelterLocation.At(place.Place);
            }
            return null;
        }
        public static bool Legal(RogueGame game, Actor owner, Actor other, string kind)
        {
            if (!Free(game, owner)) return false;
            if (kind == "succession") return owner.Community != null && owner.CommunityChief == null;
            if (kind == "move_answer") return owner.Community != null && owner.ResidentCommunity.ProposedMove != null;
            if (kind == "secure") return owner.CommunityChief == owner && owner.ResidentCommunity.TravelHome != null &&
                Arrived(owner, owner.ResidentCommunity.TravelHome);
            if (kind == "depart") return owner.Community != null;
            if (kind == "relocate") return owner.CommunityChief == owner && KnownAlternative(game, owner) != null;
            if (!Talkable(game, owner, other)) return false;
            if (kind == "food_report") return owner.Community != null && owner.CommunityChief == other;
            if (kind == "propose") return other.Community == null && other.ResidentCommunity.Proposal == null && HouseholdAvailable(other, owner.Community) &&
                (owner.Community != null ? owner.CommunityChief == owner : Qualified(owner, other) && Qualified(other, owner));
            if (kind == "answer") return owner.ResidentCommunity.Proposal != null && !owner.ResidentCommunity.Proposal.Answered;
            if (kind == "found" || kind == "admit") return owner.ResidentCommunity.Proposal != null && owner.ResidentCommunity.Proposal.Accepted && HouseholdAvailable(other, owner.Community);
            if (kind == "evict") return owner.CommunityChief == owner && other.Community == owner.Community && EvictionEligible(game, owner, other);
            if (kind == "warning") return owner.CommunityChief == owner && other.Community == owner.Community &&
                owner.Community.Tribe.ShortageReviews >= 3 && Turn(owner) - owner.Community.Tribe.ShortageSince >= 60 && HungryEvidence(game, owner);
            return owner.CommunityChief == owner && other.Community == owner.Community;
        }
        static void Speak(RogueGame game, Actor owner, Actor other, string text)
        { XpdBase here = owner.Location.Map.XpdBaseAt(owner.Location.Position);
            if (owner.Community != null && here != null && here.Owns(owner)) text += " This is our shelter; its ground supplies are communal.";
            game.DoSay(owner, other, owner.Community == null ? text : owner.Community.CommunityName + ": " + text,
            RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF |
                (owner.Community == null ? RogueGame.Sayflags.NONE : RogueGame.Sayflags.IS_COMMUNITY_CALL)); }
        public static void Perform(RogueGame game, Actor owner, Actor other, string kind)
        {
            if (!Legal(game, owner, other, kind)) return;
            int turn = Turn(owner); bool charged = false;
            if (kind == "succession") ElectChief(game, owner, true);
            else if (kind == "move_answer")
            {
                var state = owner.ResidentCommunity;
                bool come = NpcValues.KnownAttitude(owner, state.MoveProposer, game.NpcContent.Personalities) + Bias(owner, DecisionKind.Group) >= 0;
                if (come) { state.TravelHome = state.ProposedMove.Copy(); Speak(game, owner, other, "I agree to move to " + state.TravelHome.MapName + " at " + state.TravelHome.Anchor + ". My personal party keeps its orders."); }
                else { state.RemainAtOldHome = true; Speak(game, owner, other, "I refuse the move. I will remain here and leave the tribe."); }
                state.ProposedMove = null;
                Record(game, owner, come ? "community_move_accepted" : "community_move_refused", come ? "Accepted a communicated relocation proposal." : "Refused the proposed relocation and chose to stay behind.");
            }
            else if (kind == "food_report")
            {
                var state = owner.ResidentCommunity;
                state.ReportedFood = Nutrition(game, owner.Inventory, owner.Location.Map.LocalTime.TurnCounter + 2 * WorldTime.TURNS_PER_DAY);
                state.ReportedDemand = Math.Max(0, game.Rules.ActorMaxFood(owner) - owner.FoodPoints) + 2 * WorldTime.TURNS_PER_DAY;
                state.FoodReportTurn = turn; state.FoodReportDate = game.Session.WorldTime.TurnCounter; state.FoodReportedTo = other.PersonalityIdentity;
                Speak(game, owner, other, state.ReportedFood == 0 ? "My personal provisions will not cover the next two days. I need to look for food." :
                    "I have about " + (state.ReportedFood / WorldTime.TURNS_PER_HOUR) + " hours of personal food at the two-day horizon. It stays private until donated.");
                if (game.Rules.IsActorHungry(owner) && !NpcFoodSupply.HasFood(game, owner)) NpcEvents.Publish(game, "requested_food", owner, other);
            }
            else if (kind == "secure")
            {
                var group = owner.Community; string reason;
                var cells = XpdBasePlanner.Preview(owner.Location.Map, owner, game.Rules, out reason);
                if (cells == null || SleepingSpace(game, owner, owner.Location.Map, cells) < group.Tribe.Residents.Count(a => !a.IsDead) ||
                    !game.TryClaimXpdBase(owner, out reason))
                { owner.ResidentCommunity.RelocationRetryTurn = turn + 30; Speak(game, owner, null, "The proposed refuge cannot be secured. We'll retry after 30 turns."); }
                else
                {
                    charged = true;
                    XpdBase replacement = owner.Location.Map.XpdBaseAt(owner.Location.Position);
                    game.ReleaseTribeClaims(owner, group, replacement);
                    group.Home = new CommunityHomeState { Place = CommunityShelterLocation.At(owner.Location) };
                    group.Tribe.Food.Clear(); group.Tribe.Relocation = null; owner.ResidentCommunity.KnownHome = group.Home.Place.Copy();
                    owner.ResidentCommunity.TravelHome = null; group.Tribe.ShortageReviews = 0; group.Tribe.WarningTurn = -1;
                    Speak(game, owner, null, "We have secured our replacement home. The old claims are released; ground supplies stayed behind.");
                    Record(game, owner, "community_relocated", "Secured a replacement home through ordinary travel and claim actions; the former home was released.");
                    CommunityKnowledgeSystem.RefreshKnownSurroundings(game, owner);
                }
            }
            else if (kind == "propose")
            {
                var proposal = new TribeProposal { Proposer = owner.PersonalityIdentity, Candidate = other.PersonalityIdentity,
                    Turn = turn, ExpiresTurn = turn + 180, CandidateExpiresTurn = Turn(other) + 180,
                    Admission = owner.Community != null, Home = CommunityShelterLocation.At(owner.Location) };
                if (proposal.Admission)
                { ResolvedCommunityHome home = game.ResolveCommunityHome(owner);
                    if (home == null) return;
                    proposal.ReportedResidents = KnownResidentCount(owner) + other.PartyTree().Count();
                    proposal.ReportedSpace = SleepingSpace(game, owner, home.Map, home.Claim.Cells);
                    proposal.ReportedShortfall = FoodShortfall(game, owner, other); }
                owner.ResidentCommunity.Proposal = other.ResidentCommunity.Proposal = proposal;
                Speak(game, owner, other, proposal.Admission ? "Would your household live with " + owner.Community.CommunityName + "? We share ground supplies and help with real requests. Food and space may be tight." :
                    "We've helped each other or shared this refuge. Shall we make a shared home here? You would remain independent.");
            }
            else if (kind == "answer")
            {
                var p = owner.ResidentCommunity.Proposal;
                owner.ResidentCommunity.ReportedFood = Nutrition(game, owner.Inventory, owner.Location.Map.LocalTime.TurnCounter + 2 * WorldTime.TURNS_PER_DAY);
                owner.ResidentCommunity.FoodReportTurn = turn; owner.ResidentCommunity.FoodReportDate = game.Session.WorldTime.TurnCounter; owner.ResidentCommunity.FoodReportedTo = other.PersonalityIdentity;
                owner.ResidentCommunity.ReportedDemand = Math.Max(0, game.Rules.ActorMaxFood(owner) - owner.FoodPoints) + 2 * WorldTime.TURNS_PER_DAY;
                string reason = null;
                p.Accepted = CandidateConsent(game, owner, other) && (!p.Admission || Admission(game, other, owner, out reason)); p.Answered = true;
                Speak(game, owner, other, p.Accepted ? "Yes. My household agrees. I can report only our own provisions; they remain ours until donated." : reason ?? "No. I don't trust this arrangement enough.");
                if (!p.Accepted) Decline(owner, other, turn);
            }
            else if (kind == "found")
            {
                var p = owner.ResidentCommunity.Proposal; string reason;
                var cells = XpdBasePlanner.Preview(owner.Location.Map, owner, game.Rules, out reason);
                if (p.Home.Matches(owner.Location.Map, owner.Location.Position) && Qualified(owner, other) && Qualified(other, owner) &&
                    owner.Community == null && cells != null && SleepingSpace(game, owner, owner.Location.Map, cells) >= owner.PartyTree().Count() + other.PartyTree().Count() &&
                    game.TryClaimXpdBase(owner, out reason))
                {
                    charged = true;
                    var group = new SocialGroup(owner, djack.RogueSurvivor.Engine.SimulationClock.NewIdentity()) { Tribe = new TribeState { FoundedTurn = turn },
                        CommunityName = owner.UnmodifiedName + "'s refuge" };
                    owner.SetCommunityHousehold(group); other.SetCommunityHousehold(group);
                    CommunityCultureSystem.Initialize(game,group,new[] { owner,other });
                    owner.ResidentCommunity.RememberResidents(group.Tribe.Residents);
                    owner.Location.Map.XpdBaseAt(owner.Location.Position).EstablishCommunity(owner, group);
                    foreach (Actor resident in group.Tribe.Residents) resident.ResidentCommunity.KnownHome = p.Home.Copy();
                    owner.ResidentCommunity.Proposal = other.ResidentCommunity.Proposal = null;
                    game.InitializeCommunityHome(owner);
                    CommunityKnowledgeSystem.RefreshKnownSurroundings(game, owner);
                    Speak(game, owner, other, "We have founded " + group.CommunityName + ". I am its chief; residents keep their own parties.");
                    Record(game, owner, "community_founded", "Founded a shared home through a spoken agreement.", other);
                }
                else { Speak(game, owner, other, "We cannot legally secure that home now."); Decline(owner, other, turn); }
            }
            else if (kind == "admit")
            {
                string reason; if (Admission(game, owner, other, out reason))
                { owner.ResidentCommunity.RememberResidents(other.PartyTree()); other.SetCommunityHousehold(owner.Community);
                    foreach (Actor resident in other.PartyTree()) resident.ResidentCommunity.KnownHome = owner.Community.Home.Place.Copy();
                    owner.ResidentCommunity.Proposal = other.ResidentCommunity.Proposal = null;
                    Speak(game, owner, other, "Your household is admitted to " + owner.Community.CommunityName + ". You remain independent; please help with real needs.");
                    Record(game, owner, "community_admitted", other.UnmodifiedName + "'s household was admitted; food and sleeping space were considered.", other); }
                else { Speak(game, owner, other, reason); Decline(owner, other, turn); }
            }
            else if (kind == "warning")
            {
                owner.Community.Tribe.WarningTurn = turn;
                other.ResidentCommunity.EvictionWarningFrom = owner.PersonalityIdentity;
                other.ResidentCommunity.EvictionWarningTurn = turn;
                Speak(game, owner, other, "Residents are going hungry. Please help find or donate food within 180 turns. If this continues, a household may have to leave.");
                Record(game, owner, "community_shortage", "Observed hunger led to a food warning with a 180-turn response window.", other);
            }
            else if (kind == "evict")
            {
                var group = owner.Community;
                if (other.ResidentCommunity.EvictionWarningFrom != owner.PersonalityIdentity || other.ResidentCommunity.EvictionWarningTurn < 0 ||
                    turn - other.ResidentCommunity.EvictionWarningTurn < 180 || group.Tribe.ShortageReviews < 3 ||
                    turn - group.Tribe.LastEvictionTurn < 180 || FoodShortfall(game, owner) == 0 ||
                    !group.Tribe.Residents.Any(a => !a.IsDead && NpcKnowledgeSystem.Visible(game, owner, a.Location) &&
                        (a == owner ? game.Rules.IsActorHungry(a) && !NpcFoodSupply.HasFood(game, a) :
                        owner.Personality.Knowledge.Person(a.PersonalityIdentity) != null && owner.Personality.Knowledge.Person(a.PersonalityIdentity).FoodNeed > 0 &&
                        owner.Location.Map.LocalTime.TurnCounter - owner.Personality.Knowledge.Person(a.PersonalityIdentity).FoodNeedTurn <= 60)) || EvictionScore(game, owner, other) < 0) return;
                Speak(game, owner, other, "We are still going hungry after the warning. Your household must leave. Your possessions and promises remain yours.");
                Record(game, owner, "community_eviction", other.UnmodifiedName + "'s household received notice of scarcity eviction.", other);
                if (other.Personality != null) other.Personality.Opinion(owner.PersonalityIdentity, owner.UnmodifiedName).Grievance += 15;
                other.SetTrustIn(owner, other.GetTrustIn(owner) - 10);
                LeaveHousehold(game, other, "Scarcity eviction by " + owner.UnmodifiedName + ". Promises remain binding.");
                group.Tribe.LastEvictionTurn = turn; group.Tribe.WarningTurn = -1; group.Tribe.ShortageReviews = 0;
            }
            else if (kind == "relocate")
            {
                var group = owner.Community; group.Tribe.Relocation = KnownAlternative(game, owner);
                group.Tribe.RelocationReason = game.ResolveCommunityHome(owner) == null ? "home loss" : "observed hunger or danger";
                owner.ResidentCommunity.TravelHome = group.Tribe.Relocation.Copy();
                Speak(game, owner, null, "Our home is under pressure. I propose moving to " + group.Tribe.Relocation.MapName + " at " +
                    group.Tribe.Relocation.Anchor + ". Come if you agree; supplies left on the ground stay here.");
                foreach (Actor resident in group.Tribe.Residents.ToArray())
                {
                    if (resident == owner || resident.IsDead || resident.IsSleeping || resident.IsPlayer || resident.InPlayerParty || resident.Leader != null ||
                        resident.Location.Map != owner.Location.Map || game.Rules.StdDistance(resident.Location.Position, owner.Location.Position) > resident.AudioRange) continue;
                    resident.ResidentCommunity.ProposedMove = group.Tribe.Relocation.Copy();
                    resident.ResidentCommunity.MoveProposer = owner.PersonalityIdentity;
                }
                Record(game, owner, "community_displacement", "Proposed relocation due to " + group.Tribe.RelocationReason + ". Residents may refuse.");
            }
            else if (kind == "depart")
            {
                Speak(game, owner, null, "I'm leaving this community after hunger, mistreatment or disagreement. I will make my own decisions.");
                Record(game, owner, "community_fragmentation", owner.UnmodifiedName + "'s household left; no new tribe exists until another founding agreement.");
                LeaveHousehold(game, owner, "Voluntary departure after experienced hardship or disagreement.");
            }
            if (!charged) game.SpendActorActionPoints(owner, Rules.BASE_ACTION_COST);
        }
        static void Decline(Actor a, Actor b, int turn)
        {
            foreach (Actor owner in new[] { a, b })
            { Actor other = owner == a ? b : a; var c = owner.ResidentCommunity.Contact(other.PersonalityIdentity);
                if (c == null) { c = new TribeContact { Person = other.PersonalityIdentity }; owner.ResidentCommunity.Contacts.Add(c); }
                c.DeclinedUntil = Turn(owner) + 180; owner.ResidentCommunity.Proposal = null;
                while (owner.ResidentCommunity.Contacts.Count > 32) owner.ResidentCommunity.Contacts.RemoveAt(0); }
        }
        public static void Record(RogueGame game, Actor source, string kind, string text, Actor participant = null)
        {
            SocialGroup group = source.Community; if (group == null) return; int turn = game.Session.WorldTime.TurnCounter;
            var note = new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = kind, Text = text,
                EventTurn = turn, LearnedTurn = turn, Source = source.UnmodifiedName };
            group.Tribe.Note(note);
            foreach (Actor listener in source.Location.Map.Actors)
            {
                if (listener.IsDead || listener.IsSleeping || listener.Model.Abilities.IsUndead || listener.Personality == null ||
                    game.Rules.StdDistance(listener.Location.Position, source.Location.Position) > listener.AudioRange) continue;
                bool identified = listener == source || listener == participant || NpcKnowledgeSystem.Visible(game, listener, source.Location);
                if (kind == "community_departure" && !identified) continue;
                if (source.IsDead && !identified) continue;
                var known = listener.Personality.FindCommunity(group.Identity);
                if (source.IsDead && known == null) continue;
                if (known == null) known = listener.Personality.LearnCommunity(group.Identity, group.CommunityName, turn,
                    identified ? "heard " + source.UnmodifiedName : "heard an unidentified speaker");
                var learned = note.Copy();
                learned.Source = source.IsDead ? "witness" : kind == "community_departure" ? "witnessed household departure" :
                    identified ? "heard " + source.UnmodifiedName : "heard an unidentified speaker";
                if (kind == "community_dissolution") learned.Text = source.IsDead ?
                    "An observed resident died. Whether the tribe has other committed survivors is unknown." :
                    "An observed household left. The tribe's continued existence is uncertain.";
                else if (!identified) learned.Text = "Heard a community announcement concerning " + kind.Replace("community_", "").Replace('_', ' ') + ".";
                known.Note(learned);
                if (!identified) continue;
                known.Member(new CommunityMember { Id = source.PersonalityIdentity, Name = source.UnmodifiedName,
                    Status = source.IsDead ? "died" : kind == "community_fragmentation" || kind == "community_departure" ? "left" : "seen alive", Turn = turn, Source = "witnessed community speech" });
                if (source.PersonalityIdentity == group.LeaderId) { known.Leader = source.UnmodifiedName; known.LeaderTurn = turn; known.LeaderSource = "reported chief"; known.IsTribe = true; }
                if (participant != null && NpcKnowledgeSystem.Visible(game, listener, participant.Location))
                    known.Member(new CommunityMember { Id = participant.PersonalityIdentity, Name = participant.UnmodifiedName,
                        Status = kind == "community_eviction" ? "left after eviction notice" : "seen alive", Turn = turn, Source = "witnessed community speech" });
            }
        }
        public static void LeaveHousehold(RogueGame game, Actor root, string reason)
        {
            SocialGroup group = root.Community; if (group == null) return;
            int date = game.Session.WorldTime.TurnCounter;
            Record(game, root, "community_departure", root.UnmodifiedName + "'s household left. " + reason);
            if (group.Tribe.Residents.Where(a => !a.IsDead).All(a => root.PartyTree().Contains(a)))
                Record(game, root, "community_dissolution", "The final committed household left; the tribe dissolved.");
            root.ResidentCommunity.LastTribe = group.Identity;
            root.SetCommunityHousehold(null);
            foreach (Actor member in root.PartyTree()) { member.ResidentCommunity.TravelHome = null; member.ResidentCommunity.ProposedMove = null; member.ResidentCommunity.RemainAtOldHome = false;
                var known = member.Personality == null ? null : member.Personality.FindCommunity(group.Identity);
                if (known != null) { known.Note(new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = "community_departure",
                        Text = reason, EventTurn = date, LearnedTurn = date, Source = "participant" });
                    known.Member(new CommunityMember { Id = member.PersonalityIdentity, Name = member.UnmodifiedName, Status = "left", Turn = date, Source = "participant" }); } }
            if (root.CommunityMembership != null) { root.CommunityDepartureReason = reason; root.RestorePersonalParty(); }
            if (group.LeaderId == root.PersonalityIdentity) group.LeaderId = Guid.Empty;
            if (!group.Tribe.Residents.Any(a => !a.IsDead)) { group.Tribe.Dissolved = true; game.ReleaseTribeClaims(root, group); }
            game.InvalidateCommunityHomes();
        }
        public static void ElectChief(RogueGame game, Actor source, bool death)
        {
            SocialGroup group = source.Community; if (group == null) return;
            Actor former = group.Tribe.Residents.Find(a => a.PersonalityIdentity == group.LeaderId);
            Actor successor = group.Tribe.Residents.Where(a => !a.IsDead && !a.IsSleeping && !a.IsPlayer && !a.InPlayerParty &&
                a.Location.Map == source.Location.Map && NpcKnowledgeSystem.Visible(game, source, a.Location) &&
                (Attitude(game, a, source) + Bias(a, DecisionKind.Group) + Bias(a, DecisionKind.Compassion) >= 0))
                .OrderByDescending(a => Bias(a, DecisionKind.Group) + Bias(a, DecisionKind.Compassion)).ThenBy(a => a.PersonalityIdentity).FirstOrDefault();
            if (successor == null) { group.LeaderId = Guid.Empty; return; }
            group.LeaderId = successor.PersonalityIdentity; group.LeaderName = successor.UnmodifiedName;
            // A former chief's warning is retained in the dated history. The successor
            // must communicate their own warning and give a fresh response window.
            group.Tribe.WarningTurn = -1;
            KnownCommunity known = successor.Personality == null ? null : successor.Personality.FindCommunity(group.Identity);
            if (known != null) foreach (CommunityMember member in known.Members)
                if (!successor.ResidentCommunity.KnownResidents.Contains(member.Id)) successor.ResidentCommunity.KnownResidents.Add(member.Id);
            successor.ResidentCommunity.RememberResidents(new[] { successor });
            if (former != null) game.TransferCommunityBases(former, successor, group.Identity);
            else game.RebindTribeClaims(group, successor);
            game.InvalidateCommunityHomes();
            if (death) { Speak(game, successor, null, "I will serve as chief of " + group.CommunityName + ". Our identity and disputes endure.");
                Record(game, successor, "community_succession", successor.UnmodifiedName + " agreed to serve as chief after a vacancy.");
                if (former != null) NpcEvents.Publish(game, "group_succession", successor, former); }
        }
        public static void Death(RogueGame game, Actor dead, Actor killer = null, string reason = null)
        {
            SocialGroup group = dead.Community; if (group == null) return;
            group.Tribe.Note(new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = "community_resident_loss",
                EventTurn = game.Session.WorldTime.TurnCounter, LearnedTurn = game.Session.WorldTime.TurnCounter, Source = "actual death",
                Text = dead.UnmodifiedName + " died: " + (reason ?? "known death") + (killer == null ? "" : " (killer: " + killer.UnmodifiedName + ")") });
            if (group.LeaderId == dead.PersonalityIdentity) ElectChief(game, dead, true);
            if (!group.Tribe.Residents.Any(a => !a.IsDead))
            { Record(game, dead, "community_dissolution", "The last committed living resident died; the tribe dissolved."); group.Tribe.Dissolved = true; game.ReleaseTribeClaims(dead, group); }
            dead.ClearCommunityMember(); game.InvalidateCommunityHomes();
        }
    }
}
