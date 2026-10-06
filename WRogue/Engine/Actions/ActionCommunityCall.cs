using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionCommunityCall : ActorAction
    {
        readonly Actor player;
        public ActionCommunityCall(Actor speaker, RogueGame game, Actor player) : base(speaker, game) { this.player = player; }
        public override bool IsLegal() { return CommunityEncounterSystem.CanCall(m_Game, m_Actor, player); }
        public override void Perform() { CommunityEncounterSystem.Call(m_Game, m_Actor, player); }
    }
}
