using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionFollowerReply : ActorAction
    {
        public ActionFollowerReply(Actor actor, RogueGame game) : base(actor, game) { }
        public override bool IsLegal()
        { BaseAI ai = m_Actor.Controller as BaseAI; return ai != null && ai.CanReplyFollowerCall(m_Game); }
        public override void Perform()
        { BaseAI ai = m_Actor.Controller as BaseAI; if (ai != null) ai.ReplyFollowerCall(m_Game); }
    }
}
