using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;
namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionCommunityDecision : ActorAction
    {
        readonly Actor other; readonly string kind;
        public string Kind { get { return kind; } }
        public ActionCommunityDecision(Actor actor, RogueGame game, Actor other, string kind) : base(actor, game) { this.other = other; this.kind = kind; }
        public override bool IsLegal() { return CommunityAdaptationSystem.Legal(m_Game, m_Actor, other, kind); }
        public override void Perform() { CommunityAdaptationSystem.Perform(m_Game, m_Actor, other, kind); }
    }
}
