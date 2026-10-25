using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Engine.AI;
using djack.RogueSurvivor.Gameplay.AI.Tools;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Gameplay.AI
{
    abstract partial class OrderableAI
    {
        [OptionalField] CommunityShelterLocation m_HomeOutingPlace;
        [OptionalField] int m_HomeOutingUntil;
        [OptionalField] bool m_HomeOutingReturning;

        protected ActorAction BehaviorCommunityAlarm(RogueGame game, List<Actor> visible)
        {
            SocialGroup group = m_Actor.SocialGroup;
            if (group == null || group.Home == null || group.Home.Watcher != m_Actor.PersonalityIdentity ||
                Order != null || m_Actor.InPlayerParty || game.ResolveCommunityHome(m_Actor) == null ||
                m_Actor.Location.Map.LocalTime.TurnCounter < group.Home.NextAlarmTurn || visible == null ||
                game.Rules.IsActorStarving(m_Actor) || game.Rules.IsActorTired(m_Actor) || game.Rules.IsActorSleepy(m_Actor) ||
                m_Actor.HitPoints < game.Rules.ActorMaxHPs(m_Actor) / 2 ||
                (game.Rules.IsActorHungry(m_Actor) && NpcFoodSupply.HasFood(game, m_Actor))) return null;
            Actor enemy = null;
            foreach (Actor actor in visible)
                if (game.Rules.IsKnownEnemy(m_Actor, actor))
                {
                    if (game.Rules.GridDistance(m_Actor.Location.Position, actor.Location.Position) <= 1) return null;
                    if (enemy == null) enemy = actor;
                }
            return enemy == null ? null : new ActionCommunityAlarm(m_Actor, game, enemy);
        }
        protected ActorAction BehaviorCommunityHome(RogueGame game, bool onlyReturn = false)
        {
            if (m_Actor.InPlayerParty || Order != null) return null;
            if (m_Actor.Community != null)
            {
                if (m_Actor.ResidentCommunity.TravelHome != null) return BehaviorCommunityRelocation(game);
                var known = m_Actor.ResidentCommunity.KnownHome;
                // A shared state change is not a communicated destination.
                if (known == null || m_Actor.Community.Home == null || !known.Matches(game.ResolveCommunityPlace(m_Actor, m_Actor.Community.Home.Place)?.Map,
                    m_Actor.Community.Home.Place.Anchor)) return null;
            }
            ResolvedCommunityHome resolved = game.ResolveCommunityHome(m_Actor);
            if (resolved == null) return null;
            CommunityHomeState home = m_Actor.SocialGroup.Home;
            int turn = m_Actor.Location.Map.LocalTime.TurnCounter;
            if (turn < home.RetryTurn(m_Actor.PersonalityIdentity)) return null;
            game.ScheduleCommunityWatch(m_Actor);
            bool watch = !onlyReturn && home.Watcher == m_Actor.PersonalityIdentity;
            Location destination = resolved.Anchor;
            XpdBase currentClaim = m_Actor.Location.Map.XpdBaseAt(m_Actor.Location.Position);
            bool atHome = currentClaim != null && currentClaim.IsPartOf(resolved.Claim);
            if (watch)
            {
                m_HomeOutingUntil = 0;
                m_HomeOutingReturning = true;
            }
            else if (!onlyReturn)
            {
                int clock = CommunityAdaptationSystem.Turn(m_Actor);
                bool sameHome = m_HomeOutingPlace != null && m_HomeOutingPlace.Anchor == home.Place.Anchor &&
                    m_HomeOutingPlace.Matches(resolved.Map, home.Place.Anchor);
                if (atHome)
                {
                    if (!sameHome || m_HomeOutingReturning || clock >= m_HomeOutingUntil)
                    {
                        m_HomeOutingPlace = home.Place.Copy();
                        m_HomeOutingUntil = clock + 2 * WorldTime.TURNS_PER_HOUR;
                        m_HomeOutingReturning = false;
                    }
                    return null;
                }
                if (sameHome && !m_HomeOutingReturning && clock < m_HomeOutingUntil &&
                    m_Actor.Location.Map == resolved.Map && game.Rules.GridDistance(m_Actor.Location.Position, resolved.Anchor.Position) <= 12)
                    return null;
                // Once returning, continue all the way home instead of oscillating at the boundary.
                m_HomeOutingReturning = true;
            }
            if (watch)
            {
                Point? post = WatchPosition(game, resolved);
                if (!post.HasValue) { home.Retry(m_Actor.PersonalityIdentity, turn + 30); home.Watcher = Guid.Empty; return null; }
                destination = new Location(resolved.Map, post.Value);
            }
            else
            {
                if (atHome) return null;
                Point? free = FirstWalkableBaseCell(resolved.Map, resolved.Claim);
                if (!free.HasValue) { home.Retry(m_Actor.PersonalityIdentity, turn + 30); return null; }
                destination = new Location(resolved.Map, free.Value);
            }
            if (m_Actor.Location.Map == destination.Map && m_Actor.Location.Position == destination.Position)
                return new ActionWait(m_Actor, game);
            if (m_Actor.Community == null)
            {
                Point? routePoint = m_Actor.Location.Map == destination.Map ? (Point?)destination.Position :
                    m_Actor.Location.Map.GetExitPos(XpdSupplyRoutes.NextExit(m_Actor.Location.Map, destination.Map, null));
                if (!routePoint.HasValue || !CanReachSimple(game, routePoint.Value, RouteFinder.SpecialActions.DOORS | RouteFinder.SpecialActions.JUMP))
                { home.Retry(m_Actor.PersonalityIdentity, turn + 30); return null; }
            }
            ActorAction travel = m_Actor.Community == null ? GoTo(game, destination) : BehaviorNpcKnownRoute(game, destination, true);
            if (travel == null) home.Retry(m_Actor.PersonalityIdentity, turn + 30);
            return travel;
        }
        protected ActorAction BehaviorCommunityRelocation(RogueGame game)
        {
            if (m_Actor.Community == null || m_Actor.InPlayerParty || Order != null || m_Actor.ResidentCommunity.TravelHome == null) return null;
            var state = m_Actor.ResidentCommunity; int turn = CommunityAdaptationSystem.Turn(m_Actor);
            if (turn < state.RelocationRetryTurn) return null;
            Location? target = game.ResolveCommunityPlace(m_Actor, state.TravelHome);
            if (!target.HasValue) { state.RelocationRetryTurn = turn + 30; return null; }
            if (CommunityAdaptationSystem.Arrived(m_Actor, state.TravelHome))
            {
                XpdBase here = m_Actor.Location.Map.XpdBaseAt(m_Actor.Location.Position);
                if (here != null && here.Owns(m_Actor) && m_Actor.Community.Home.Place.Matches(m_Actor.Location.Map, m_Actor.Location.Position))
                { state.KnownHome = state.TravelHome.Copy(); state.TravelHome = null; return null; }
                return m_Actor.CommunityChief == m_Actor ? null : new ActionWait(m_Actor, game);
            }
            ActorAction action = BehaviorNpcKnownRoute(game, target.Value, true);
            if (action == null) state.RelocationRetryTurn = turn + 30;
            return action;
        }
        Point? WatchPosition(RogueGame game, ResolvedCommunityHome home)
        {
            Point? best = null; int bestDistance = Int32.MaxValue;
            foreach (Point point in home.Claim.Cells)
            {
                Map map = home.Map;
                if (!map.IsWalkable(point.X, point.Y) || map.GetExitAt(point) != null || map.GetMapObjectAt(point) is DoorWindow ||
                    (map.GetActorAt(point) != null && map.GetActorAt(point) != m_Actor)) continue;
                bool entrance = false, clear = true;
                foreach (Direction direction in Direction.COMPASS)
                {
                    Point next = point + direction;
                    if (!map.IsInBounds(next)) continue;
                    if (map.GetMapObjectAt(next) is DoorWindow || map.GetExitAt(next) != null) entrance = true;
                    // Exclude a one-tile corridor: a watcher must leave room to pass.
                }
                int neighbors = 0;
                foreach (Direction direction in new[] { Direction.N, Direction.E, Direction.S, Direction.W })
                { Point next = point + direction; if (map.IsInBounds(next) && map.IsWalkable(next.X, next.Y)) neighbors++; }
                clear = neighbors >= 3;
                if (!entrance || !clear) continue;
                if (map == m_Actor.Location.Map && !CanReachSimple(game, point, RouteFinder.SpecialActions.DOORS | RouteFinder.SpecialActions.JUMP)) continue;
                int distance = game.Rules.GridDistance(home.Anchor.Position, point);
                if (distance < bestDistance || (distance == bestDistance && (best == null || point.Y < best.Value.Y ||
                    (point.Y == best.Value.Y && point.X < best.Value.X)))) { best = point; bestDistance = distance; }
            }
            return best;
        }
    }
}
