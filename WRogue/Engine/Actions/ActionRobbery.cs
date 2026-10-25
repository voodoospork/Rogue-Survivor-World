using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionRobbery : ActorAction
    {
        readonly Actor target;
        readonly RobberyGoods goods;
        public ActionRobbery(Actor actor, RogueGame game, Actor target, RobberyGoods goods) : base(actor, game)
        { this.target = target; this.goods = goods; }
        public override bool IsLegal() { return RobberySystem.CanDemand(m_Game, m_Actor, target, goods); }
        public override void Perform() { RobberySystem.Demand(m_Game, m_Actor, target, goods); }
    }
    sealed class ActionAnswerRobbery : ActorAction
    {
        readonly bool surrender;
        public ActionAnswerRobbery(Actor actor, RogueGame game, bool surrender) : base(actor, game) { this.surrender = surrender; }
        public override bool IsLegal() { return RobberySystem.Robber(m_Game, m_Actor) != null; }
        public override void Perform() { RobberySystem.Answer(m_Game, m_Actor, surrender); }
    }
    sealed class ActionTribute : ActorAction
    {
        readonly Actor target;
        readonly bool answer, accept, expire;
        public ActionTribute(Actor actor, RogueGame game, Actor target, bool answer = false, bool accept = true, bool expire = false) : base(actor, game)
        { this.target = target; this.answer = answer; this.accept = accept; this.expire = expire; }
        public override bool IsLegal() { return expire ? CommunityTributeSystem.CanExpire(m_Game, m_Actor, target) : answer ? CommunityTributeSystem.CanAnswer(m_Game, m_Actor, target) :
            CommunityTributeSystem.CanDemand(m_Game, m_Actor, target); }
        public override void Perform() { if (expire) CommunityTributeSystem.Expire(m_Game, m_Actor, target);
            else if (answer) CommunityTributeSystem.Answer(m_Game, m_Actor, target, accept);
            else CommunityTributeSystem.Demand(m_Game, m_Actor, target); }
    }
}
