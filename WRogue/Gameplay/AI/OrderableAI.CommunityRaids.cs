using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Gameplay.AI
{
    abstract partial class OrderableAI
    {
        protected ActorAction BehaviorCommunityRaid(RogueGame game, List<Actor> visible, bool retreatOnly = false)
        {
            var duty = m_Actor.CommunityRaid;
            if (duty == null) return retreatOnly ? null : CommunityRaidSystem.Select(game, m_Actor);
            if (Order != null || m_Actor.InPlayerParty || game.Rules.IsActorTired(m_Actor) ||
                game.Rules.IsActorHungry(m_Actor) && NpcFoodSupply.HasFood(game, m_Actor)) return null;
            bool returning = duty.Phase == CommunityRaidPhase.Returning;
            if (retreatOnly && !returning) return null;
            if (returning)
            {
                ActorAction care = BehaviorUseMedecine(game, 2, 1, 2, 4, 2);
                if (care != null) return care;
                if (game.Rules.IsActorSleepy(m_Actor) && IsInside(m_Actor)) return null;
            }
            if (duty.Phase == CommunityRaidPhase.Searching) return new ActionWait(m_Actor, game);
            // Pace the commander to visible companions. Missing companions are handled by the contact limit.
            if (!returning && m_Actor.PersonalityIdentity == duty.Commander && visible != null &&
                visible.Any(a => duty.Party.Contains(a.PersonalityIdentity) && !a.IsDead &&
                    game.Rules.GridDistance(a.Location.Position, m_Actor.Location.Position) > 3)) return new ActionWait(m_Actor, game);
            var place = game.ResolveCommunityPlace(m_Actor, returning ? duty.Home : duty.Objective);
            if (returning && place.HasValue && place.Value.Map == m_Actor.Location.Map)
            {
                // A busy anchor must not make a return impossible, or count an outside tile as home.
                Point? goal = null; int distance = int.MaxValue; Map map = m_Actor.Location.Map;
                for (int y = duty.Home.Anchor.Y - 1; y <= duty.Home.Anchor.Y + 1; y++)
                    for (int x = duty.Home.Anchor.X - 1; x <= duty.Home.Anchor.X + 1; x++)
                    {
                        var point = new Point(x, y);
                        if (!map.IsInBounds(point) || !map.IsWalkable(x, y) ||
                            duty.Home.BuildingBounds.HasValue && !duty.Home.BuildingBounds.Value.Contains(point) ||
                            map.GetActorAt(point) != null && map.GetActorAt(point) != m_Actor) continue;
                        int d = game.Rules.GridDistance(m_Actor.Location.Position, point);
                        if (d < distance) { distance = d; goal = point; }
                    }
                place = goal.HasValue ? new Location(map, goal.Value) : (Location?)null;
            }
            ActorAction move = place.HasValue ? BehaviorNpcKnownRoute(game, place.Value, true, returning ? 0 : 2) : null;
            if (move != null) { duty.Blocked = 0; return move; }
            if (++duty.Blocked >= (returning ? 16 : 8))
            {
                if (!returning) CommunityRaidSystem.Return(game, m_Actor, "route blocked or no known passage");
                else { CommunityRaidSystem.Announce(game, m_Actor, "community_raid_stranded", "Raid return route blocked; resuming local survival."); m_Actor.CommunityRaid = null; }
            }
            return new ActionWait(m_Actor, game);
        }
    }
}
