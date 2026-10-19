using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Runtime.Serialization;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Engine.AI;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Gameplay.AI
{
    abstract partial class BaseAI
    {
        [OptionalField] FollowerTravelState m_FollowerTravel;
        [NonSerialized] List<Point> m_FollowerRoute;
        [NonSerialized] Point m_FollowerRouteGoal;
        [NonSerialized] Map m_FollowerRouteMap;
        [NonSerialized] bool m_FollowerRoutine;
        [NonSerialized] int m_FollowerObstacle;
        internal FollowerTravelState Travel { get { return m_FollowerTravel; } }
        internal static bool Travelling(Actor actor)
        {
            if (actor == null || actor.IsPlayer) return false;
            int depth = 0;
            for (Actor a = actor; a != null && depth++ < 256; a = a.Leader)
            {
                if (a.IsPlayer) return true;
                var ai = a.Controller as BaseAI; var ordered = ai as OrderableAI;
                if (a.IsDead || ai == null || ai.Order != null || ordered != null && ordered.DontFollowLeader) return false;
            }
            return false;
        }
        internal static List<Actor> TravelParty(Actor root)
        {
            var result = new List<Actor>(); var seen = new HashSet<Actor>(); var pending = new Stack<Actor>();
            if (root != null) pending.Push(root);
            while (pending.Count > 0 && result.Count < 256)
            {
                Actor actor = pending.Pop(); if (!seen.Add(actor)) continue; result.Add(actor);
                if (actor.CountFollowers == 0) continue;
                var children = actor.Followers.ToArray();
                for (int i = children.Length - 1; i >= 0; i--) pending.Push(children[i]);
            }
            return result;
        }
        internal static void ResetPartyTravel(Actor root)
        { foreach (Actor actor in TravelParty(root)) (actor.Controller as BaseAI)?.ResetFollowerTravel(); }
        internal void ResetFollowerTravel()
        { m_FollowerTravel = null; ClearFollowerRoute(); }
        void ClearFollowerRoute() { m_FollowerRoute = null; m_FollowerRouteMap = null; }
        internal void ContactFollower(RogueGame game, Actor leader, string source)
        {
            if (!Travelling(m_Actor) || leader != m_Actor.Leader || leader.IsDead || m_Actor.IsSleeping || game.Rules.IsKnownEnemy(m_Actor, leader)) return;
            if (m_FollowerTravel == null || m_FollowerTravel.Leader != leader.PersonalityIdentity)
            { m_FollowerTravel = new FollowerTravelState(); ClearFollowerRoute(); }
            var old = m_FollowerTravel.Contact;
            m_FollowerTravel.Learn(leader, game.Session.WorldTime.TurnCounter, source);
            if (old == null || !old.Matches(leader.Location.Map, leader.Location.Position)) ClearFollowerRoute();
        }
        internal void LearnFollowerTransition(RogueGame game, Actor leader, Location from, Location to, string source)
        {
            if (!Travelling(m_Actor) || m_Actor.Leader != leader || m_Actor.IsSleeping) return;
            if (m_FollowerTravel == null || m_FollowerTravel.Leader != leader.PersonalityIdentity)
                m_FollowerTravel = new FollowerTravelState { Leader = leader.PersonalityIdentity };
            m_FollowerTravel.Transition(from, to, game.Session.WorldTime.TurnCounter, source); ClearFollowerRoute();
        }
        void ObserveFollowerContact(RogueGame game, List<Percept> percepts)
        {
            m_FollowerRoutine = false;
            if (!Travelling(m_Actor)) { if (m_FollowerTravel != null) ResetFollowerTravel(); return; }
            Actor leader = m_Actor.Leader;
            if (m_FollowerTravel != null && m_FollowerTravel.Leader != leader.PersonalityIdentity) ResetFollowerTravel();
            Actor contactActor = m_FollowerTravel != null && m_FollowerTravel.Recalling ? game.Player : leader;
            bool seen = false;
            if (percepts != null) foreach (Percept p in percepts)
                if (p.Percepted == contactActor && p.Location.Map == m_Actor.Location.Map &&
                    p.Turn == m_Actor.Location.Map.LocalTime.TurnCounter) { seen = true; break; }
            if (m_FollowerTravel != null && m_FollowerTravel.Recalling)
            {
                if (seen || game.FollowerPhoneLink(m_Actor, contactActor))
                { m_FollowerTravel.Learn(contactActor, game.Session.WorldTime.TurnCounter, seen ? "seen caller" : "phone contact"); m_FollowerTravel.Leader = leader.PersonalityIdentity; }
            }
            else if (seen || game.FollowerPhoneLink(m_Actor, leader)) ContactFollower(game, leader, seen ? "seen" : "phone contact");
        }
        bool FollowerUrgent(RogueGame game, List<Percept> percepts)
        {
            if (game.Rules.IsActorTired(m_Actor) || game.Rules.IsActorStarving(m_Actor) ||
                game.Rules.IsActorSleepy(m_Actor) && Directives.CanSleep ||
                game.Rules.IsActorHungry(m_Actor) && NpcFoodSupply.HasFood(game, m_Actor) ||
                m_Actor.HitPoints < game.Rules.ActorMaxHPs(m_Actor) && m_Actor.Inventory.HasItemMatching(i => i is Engine.Items.ItemMedicine && ((Engine.Items.ItemMedicine)i).Healing > 0) ||
                game.Rules.ActorInfectionPercent(m_Actor) >= game.Session.GamePreset.InfectionWeakThreshold && game.Session.GamePreset.Infection) return true;
            if (percepts != null) foreach (Percept p in percepts)
            {
                if (p.Location.Map != m_Actor.Location.Map || p.Turn != m_Actor.Location.Map.LocalTime.TurnCounter) continue;
                var actor = p.Percepted as Actor;
                if (actor != null && game.Rules.IsKnownEnemy(m_Actor, actor)) return true;
                var inv = p.Percepted as Inventory;
                if (inv != null) foreach (Item item in inv.Items) if (item is Engine.Items.ItemGrenadePrimed) return true;
            }
            return false;
        }
        void FollowerWarning(RogueGame game, string reason)
        {
            var state = m_FollowerTravel;
            if (state == null || state.Warning == reason || m_Actor.Location.Map.LocalTime.TurnCounter - state.WarningTurn < 30) return;
            state.Warning = reason; state.WarningTurn = m_Actor.Location.Map.LocalTime.TurnCounter;
            game.DoSay(m_Actor, m_Actor.Leader, reason, RogueGame.Sayflags.IS_FREE_ACTION |
                RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FOLLOWER_STATUS);
        }
        ActorAction SelectPersonalTravel(RogueGame game, List<Percept> percepts)
        {
            if (!Travelling(m_Actor)) return null;
            m_Actor.IsRunning = false;
            var state = m_FollowerTravel;
            var visible = PrepareNpcIntents(game, percepts);
            ActorAction emergency = BehaviorFleeFromExplosives(game, FilterStacks(game, percepts));
            if (emergency != null) return emergency;
            ActorAction intervention = BehaviorNpcEmergency(game, visible) ?? BehaviorNpcDeparture(game, visible);
            if (intervention != null) return intervention;
            if (FollowerUrgent(game, percepts))
            {
                if (game.Rules.IsActorTired(m_Actor)) FollowerWarning(game, "Wait up; I need to catch my breath.");
                else if (game.Rules.IsActorSleepy(m_Actor) && Directives.CanSleep) FollowerWarning(game, "Wait up; I need sleep.");
                return null;
            }
            if (state == null || state.Contact == null)
                return new ActionWait(m_Actor, game);
            int now = m_Actor.Location.Map.LocalTime.TurnCounter;
            var contact = state.Contact;
            Point goal = contact.Anchor;
            bool sameMap = contact.Matches(m_Actor.Location.Map, contact.Anchor);
            FollowerTransition transition = null;
            if (!sameMap)
            {
                transition = FollowerKnownRoute(state);
                if (transition == null) { state.Status = "waiting for contact"; return new ActionWait(m_Actor, game); }
                goal = transition.From.Anchor;
                if (m_Actor.Location.Position == goal || !m_Actor.Location.Map.IsInBounds(goal) && game.Rules.IsAdjacent(m_Actor.Location.Position, goal))
                {
                    var exit = m_Actor.Location.Map.GetExitAt(goal);
                    if (exit != null && ScavengingState.SameMap(transition.To, FollowerTravelState.PointAt(new Location(exit.ToMap, exit.ToPosition))) &&
                        game.Rules.CanActorUseExit(m_Actor, goal) && game.PersonalExitLanding(m_Actor, exit).HasValue)
                    { state.Status = "catching up through a known exit"; return new ActionUseExit(m_Actor, goal, game); }
                }
            }
            bool fresh = state.ContactTurn == game.Session.WorldTime.TurnCounter;
            int distance = game.Rules.GridDistance(m_Actor.Location.Position, goal);
            if (sameMap && fresh && distance <= 2)
            {
                state.Status = "following"; state.Recalling = false; state.Warning = null;
                ActorAction yield = FollowerYield(game, goal);
                if (yield != null) return yield;
                if (now - state.StationarySince >= 3) { m_FollowerRoutine = true; return null; }
                return new ActionWait(m_Actor, game);
            }
            if (sameMap && !fresh && distance <= 1 && state.SearchUntil < 0)
            { state.SearchUntil = now + 30; state.Searched.Add(m_Actor.Location.Position); ClearFollowerRoute(); }
            if (sameMap && state.SearchUntil >= 0)
            {
                if (now >= state.SearchUntil)
                {
                    state.Status = "waiting near last contact";
                    if (distance <= 1) return FollowerYield(game, goal) ?? new ActionWait(m_Actor, game);
                }
                else
                {
                    state.Status = "searching near last contact";
                    if (!state.SearchTarget.HasValue || state.SearchTarget == m_Actor.Location.Position)
                    {
                        state.Searched.Add(m_Actor.Location.Position); state.SearchTarget = null;
                        // A deterministic local sweep; never query the missing leader's current location.
                        for (int r = 1; r <= 6 && !state.SearchTarget.HasValue; r++)
                            for (int y = goal.Y - r; y <= goal.Y + r && !state.SearchTarget.HasValue; y++)
                                for (int x = goal.X - r; x <= goal.X + r; x++)
                                {
                                    var point = new Point(x, y);
                                    if (!m_Actor.Location.Map.IsInBounds(point) || game.Rules.GridDistance(point, goal) != r || state.Searched.Contains(point)) continue;
                                    if (game.Rules.IsWalkableFor(m_Actor, m_Actor.Location.Map, x, y)) { state.SearchTarget = point; break; }
                                }
                    }
                    if (state.SearchTarget.HasValue) goal = state.SearchTarget.Value;
                    else return new ActionWait(m_Actor, game);
                }
            }
            else state.Status = "catching up";
            int obstacle = FollowerObstacleSignature(game);
            if (now < state.RetryAt && obstacle == m_FollowerObstacle) return new ActionWait(m_Actor, game);
            ActorAction action = FollowerStep(game, goal, transition == null || !m_Actor.Location.Map.IsInBounds(goal) ? 1 : 0);
            if (action != null)
            {
                m_Actor.Activity = Activity.FOLLOWING;
                if (state.LeaderRunning && !game.Rules.IsActorTired(m_Actor)) RunIfPossible(game.Rules);
                return action;
            }
            state.Status = "waiting: route blocked"; state.RetryAt = now + 10; m_FollowerObstacle = obstacle;
            if (state.SearchTarget.HasValue) { state.Searched.Add(state.SearchTarget.Value); state.SearchTarget = null; }
            FollowerWarning(game, "Wait up; I cannot get through here.");
            return new ActionWait(m_Actor, game);
        }
        FollowerTransition FollowerKnownRoute(FollowerTravelState state)
        {
            var start = FollowerTravelState.PointAt(m_Actor.Location);
            var visited = new List<CommunityShelterLocation> { start };
            var queue = new Queue<Tuple<CommunityShelterLocation, FollowerTransition>>(); queue.Enqueue(Tuple.Create(start, (FollowerTransition)null));
            // Only the bounded remembered graph is searched. A partial chain cannot silently
            // invent an exit, or make old return trips send a follower around a cycle.
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                for (int i = state.Transitions.Count - 1; i >= 0; i--)
                {
                    var edge = state.Transitions[i];
                    if (!ScavengingState.SameMap(node.Item1, edge.From) || visited.Any(p => ScavengingState.SameMap(p, edge.To))) continue;
                    var first = node.Item2 ?? edge;
                    if (ScavengingState.SameMap(edge.To, state.Contact)) return first;
                    visited.Add(edge.To); queue.Enqueue(Tuple.Create(edge.To, first));
                }
            }
            return null;
        }
        int FollowerObstacleSignature(RogueGame game)
        {
            int value = 17; Map map = m_Actor.Location.Map;
            foreach (Direction d in Direction.COMPASS)
            {
                Point p = m_Actor.Location.Position + d; if (!map.IsInBounds(p)) continue;
                var obj = map.GetMapObjectAt(p); var door = obj as DoorWindow;
                value = unchecked(value * 31 + (map.IsWalkable(p) ? 1 : 0) + (map.GetActorAt(p) == null ? 0 : 2) +
                    (door == null ? 0 : (door.IsOpen ? 4 : 8) + (int)door.BarricadePoints));
            }
            return value;
        }
        ActorAction FollowerYield(RogueGame game, Point leaderPoint)
        {
            Map map = m_Actor.Location.Map; Point here = m_Actor.Location.Position;
            if (map.GetExitAt(here) == null && !(map.GetMapObjectAt(here) is DoorWindow)) return null;
            foreach (Direction direction in Direction.COMPASS)
            {
                Point next = here + direction;
                if (!map.IsInBounds(next) || game.Rules.GridDistance(next, leaderPoint) > 2 || map.GetExitAt(next) != null || map.GetMapObjectAt(next) is DoorWindow) continue;
                var action = game.Rules.IsBumpableFor(m_Actor, game, new Location(map, next));
                if (action is ActionMoveStep && action.IsLegal()) return action;
            }
            return null;
        }
        // Shared with independent residents: cached walking/open-door steps never authorize destruction.
        protected ActorAction FollowerStep(RogueGame game, Point goal, int radius)
        {
            Map map = m_Actor.Location.Map; Point here = m_Actor.Location.Position;
            if (game.Rules.GridDistance(here, goal) <= radius) return null;
            if (m_FollowerRouteMap != map || m_FollowerRouteGoal != goal || m_FollowerRoute == null || m_FollowerRoute.Count == 0)
            {
                ClearFollowerRoute();
                var bounds = Rectangle.FromLTRB(Math.Max(0, Math.Min(here.X, goal.X) - 8), Math.Max(0, Math.Min(here.Y, goal.Y) - 8),
                    Math.Min(map.Width, Math.Max(here.X, goal.X) + 9), Math.Min(map.Height, Math.Max(here.Y, goal.Y) + 9));
                for (int y = goal.Y - radius; y <= goal.Y + radius; y++)
                    for (int x = goal.X - radius; x <= goal.X + radius; x++)
                    {
                        var destination = new Point(x, y);
                        if (!map.IsInBounds(destination) || !bounds.Contains(destination)) continue;
                        var route = MouseMovePath.Find(here, destination, bounds, p => FollowerCanEnter(game, p));
                        if (route != null && (m_FollowerRoute == null || route.Count < m_FollowerRoute.Count)) m_FollowerRoute = route;
                    }
                // A nearby target can be on the other side of a long wall. Retry the existing
                // local-map pathfinder with the full map before reporting a blocked route.
                if (m_FollowerRoute == null && (bounds.Width != map.Width || bounds.Height != map.Height))
                    for (int y = goal.Y - radius; y <= goal.Y + radius && m_FollowerRoute == null; y++)
                        for (int x = goal.X - radius; x <= goal.X + radius; x++)
                        {
                            var destination = new Point(x, y);
                            if (!FollowerCanEnter(game, destination)) continue;
                            m_FollowerRoute = MouseMovePath.Find(here, destination, new Rectangle(0, 0, map.Width, map.Height), p => FollowerCanEnter(game, p));
                            if (m_FollowerRoute != null) break;
                        }
                m_FollowerRouteMap = map; m_FollowerRouteGoal = goal;
            }
            if (m_FollowerRoute == null || m_FollowerRoute.Count == 0) return null;
            while (m_FollowerRoute.Count > 0 && m_FollowerRoute[0] == here) m_FollowerRoute.RemoveAt(0);
            if (m_FollowerRoute.Count == 0) return null;
            Point next = m_FollowerRoute[0];
            if (game.Rules.GridDistance(here, next) != 1) { ClearFollowerRoute(); return null; }
            var stepAction = game.Rules.IsBumpableFor(m_Actor, game, new Location(map, next));
            if (stepAction != null && (stepAction is ActionMoveStep || stepAction is ActionOpenDoor || stepAction is ActionSwitchPlace) && stepAction.IsLegal()) return stepAction;
            ClearFollowerRoute(); return null;
        }
        bool FollowerCanEnter(RogueGame game, Point point)
        {
            Map map = m_Actor.Location.Map;
            if (!map.IsInBounds(point) || !map.GetTileAt(point).Model.IsWalkable || IsAnyUnsafeDamagingTrapThere(game, map, point)) return false;
            Actor other = map.GetActorAt(point);
            if (other != null && other != m_Actor) return false;
            MapObject obj = map.GetMapObjectAt(point); var door = obj as DoorWindow;
            return obj == null || obj.IsWalkable || door != null && !door.IsBarricaded && game.Rules.IsOpenableFor(m_Actor, door) ||
                obj.IsJumpable && game.Rules.HasActorJumpAbility(m_Actor);
        }
        internal ActorAction ConstrainFollowerRoutine(RogueGame game, ActorAction action)
        {
            if (!m_FollowerRoutine || !Travelling(m_Actor) || m_FollowerTravel == null || m_FollowerTravel.Contact == null) return action;
            // Optional movement must remain beside the observed leader. Orders and survival are handled earlier.
            if (action is ActionUseExit || action is ActionSleep) return new ActionWait(m_Actor, game);
            var move = action as ActionBump;
            if (move != null && game.Rules.GridDistance(m_Actor.Location.Position + move.Direction, m_FollowerTravel.Contact.Anchor) > 2)
                return new ActionWait(m_Actor, game);
            var directMove = action as ActionMoveStep;
            if (directMove != null && game.Rules.GridDistance(directMove.Destination.Position, m_FollowerTravel.Contact.Anchor) > 2)
                return new ActionWait(m_Actor, game);
            var take = action as ActionTakeItem;
            if (take != null)
            {
                XpdBase claim = m_Actor.Location.Map.XpdBaseAt(take.Position);
                if (claim != null && !claim.Owns(m_Actor)) return new ActionWait(m_Actor, game);
            }
            return action;
        }
    }
}
