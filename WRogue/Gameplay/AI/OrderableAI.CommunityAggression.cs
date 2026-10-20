using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Gameplay.AI
{
    abstract partial class OrderableAI
    {
        protected ActorAction BehaviorCommunityPredation(RogueGame game, bool enemies)
        {
            var duty = m_Actor.Predation; if (duty == null) return null;
            if (game.Rules.IsActorTired(m_Actor) || game.Rules.IsActorHungry(m_Actor) && NpcFoodSupply.HasFood(game, m_Actor) ||
                game.Rules.IsActorSleepy(m_Actor) && IsInside(m_Actor)) return null;
            if (duty.Returning) {
                ActorAction care = BehaviorUseMedecine(game, 2, 1, 2, 4, 2);
                if (care != null) return care;
            }
            else if (enemies) return null;
            Location goal = duty.Returning ? duty.Departure : duty.LastSeen;
            if (!duty.Returning && goal.Map == m_Actor.Location.Map && NpcKnowledgeSystem.Visible(game, m_Actor, goal)) {
                var inventory = goal.Map.GetItemsAt(goal.Position);
                var item = inventory == null ? null : inventory.Items.FirstOrDefault(i => !i.IsEntrustedCargo &&
                    (i is ItemFood && !game.Rules.IsFoodSpoiled((ItemFood)i, goal.Map.LocalTime.TurnCounter) ||
                    i is ItemMedicine && ((ItemMedicine)i).Healing > 0) && game.Rules.CanActorGetItem(m_Actor, i));
                if (item != null && m_Actor.Location == goal)
                    return new ActionTakeItem(m_Actor, game, goal.Position, item);
                if (item == null && game.Rules.GridDistance(m_Actor.Location.Position, goal.Position) <= 1) {
                    CommunityAggressionSystem.Return(m_Actor); return new ActionWait(m_Actor, game);
                }
            }
            ActorAction move = BehaviorNpcKnownRoute(game, goal, true, duty.Returning ? 1 : 0);
            if (move != null) { duty.Blocked = 0; return move; }
            if (++duty.Blocked >= 8) {
                if (duty.Returning) m_Actor.Predation = null;
                else CommunityAggressionSystem.Return(m_Actor);
            }
            return new ActionWait(m_Actor, game);
        }
    }
}
