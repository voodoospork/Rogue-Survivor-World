using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionJoinCommunity : ActorAction
    {
        readonly Actor leader;
        public ActionJoinCommunity(Actor player, RogueGame game, Actor leader) : base(player, game) { this.leader = leader; }
        public override bool IsLegal()
        { string reason; return CommunityMembershipSystem.CanJoin(m_Game, m_Actor, leader, out reason) &&
            m_Actor.Location.Map == leader.Location.Map && m_Game.Rules.GridDistance(m_Actor.Location.Position, leader.Location.Position) <= 1; }
        public override void Perform() { if (IsLegal()) CommunityMembershipSystem.Join(m_Game, m_Actor, leader); }
    }
    sealed class ActionLeaveCommunity : ActorAction
    {
        public ActionLeaveCommunity(Actor player, RogueGame game) : base(player, game) { }
        public override bool IsLegal() { return m_Actor.IsPlayer && !m_Actor.IsDead && !m_Actor.IsSleeping &&
            m_Actor.CommunityMembership != null && (m_Actor.Leader != null || m_Actor.Community != null); }
        public override void Perform() { if (IsLegal()) CommunityMembershipSystem.Leave(m_Game, m_Actor); }
    }
}
