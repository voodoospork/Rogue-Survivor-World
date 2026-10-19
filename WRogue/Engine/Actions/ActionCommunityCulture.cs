using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;
namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionCommunityCulture : ActorAction
    {
        readonly Actor target;
        readonly CommunityCase incident;
        readonly string kind;
        public ActionCommunityCulture(Actor actor,RogueGame game,Actor target,CommunityCase incident,string kind) : base(actor,game)
        { this.target=target;this.incident=incident;this.kind=kind; }
        public override bool IsLegal() { return !m_Actor.IsDead&&!m_Actor.IsSleeping&&m_Actor.SocialGroup!=null&&
            (target==null||!target.IsDead&&!target.IsSleeping&&target.Location.Map==m_Actor.Location.Map&&
            NpcKnowledgeSystem.Visible(m_Game,m_Actor,target.Location)&&m_Game.Rules.StdDistance(m_Actor.Location.Position,target.Location.Position)<=target.AudioRange); }
        public override void Perform() { if(IsLegal())CommunityCultureSystem.Perform(m_Game,m_Actor,target,incident,kind); }
    }
}
