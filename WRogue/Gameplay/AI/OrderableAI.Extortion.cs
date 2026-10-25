using System.Collections.Generic;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Gameplay.AI
{
    abstract partial class OrderableAI
    {
        protected ActorAction BehaviorRobberyResponse(RogueGame game)
        {
            RobberySystem.Update(game, m_Actor);
            var robber = RobberySystem.Robber(game, m_Actor);
            if (robber != null) return new ActionAnswerRobbery(m_Actor, game, RobberySystem.Surrender(game, m_Actor, robber));
            return m_Actor.Robbing != null ? new ActionWait(m_Actor, game) : null;
        }
        protected ActorAction BehaviorExtortion(RogueGame game, IList<Actor> visible)
        {
            if (visible == null) return null;
            var policy = CommunityCultureSystem.KnownPolicy(m_Actor);
            bool raider = policy != null && policy.AggressionKnown && policy.Aggression == CommunityAggression.Raiders;
            // Ordinary survivors have no extortion targets to scan. A paying guarded
            // chief still handles their own agreement, without sorting robbery targets.
            if (!raider && !CommunityTributeSystem.Active(m_Actor)) return null;
            ActorAction tribute = CommunityTributeSystem.Select(game, m_Actor, visible);
            if (tribute != null) return tribute;
            var chief = CommunityTributeSystem.Approach(game, m_Actor, visible);
            if (chief != null)
            {
                var approach = BehaviorIntelligentBumpToward(game, chief.Location.Position, false, false);
                if (approach != null) return approach;
            }
            if (!raider) return null;
            foreach (var target in visible.OrderBy(a => game.Rules.GridDistance(m_Actor.Location.Position, a.Location.Position)))
            {
                if (!RobberySystem.Autonomous(game, m_Actor, target)) continue;
                var goods = RobberySystem.Need(game, m_Actor);
                if (RobberySystem.CanDemand(game, m_Actor, target, goods))
                    return CommunityEncounterSystem.Select(game, m_Actor, visible, false) ?? new ActionRobbery(m_Actor, game, target, goods);
                if (game.Rules.GridDistance(m_Actor.Location.Position, target.Location.Position) > 1)
                {
                    var approach = BehaviorIntelligentBumpToward(game, target.Location.Position, false, false);
                    if (approach != null) return approach;
                }
            }
            return null;
        }
    }
}
