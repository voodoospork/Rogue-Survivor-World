using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionCommunityRaid : ActorAction
    {
        public ActionCommunityRaid(Actor actor, RogueGame game) : base(actor, game) { }
        public override bool IsLegal() { return CommunityRaidSystem.CanAssemble(m_Game, m_Actor); }
        public override void Perform()
        { if (!IsLegal()) return; CommunityRaidSystem.Assemble(m_Game, m_Actor); m_Game.DoWait(m_Actor); }
    }
}
