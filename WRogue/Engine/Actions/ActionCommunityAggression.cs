using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionCommunityAggression : ActorAction
    {
        readonly Actor target;
        public ActionCommunityAggression(Actor actor, RogueGame game, Actor target) : base(actor, game) { this.target = target; }
        public override bool IsLegal() { return CommunityAggressionSystem.CanAttack(m_Game, m_Actor, target); }
        public override void Perform() { CommunityAggressionSystem.Begin(m_Game, m_Actor, target); }
    }
}
