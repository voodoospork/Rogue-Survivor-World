using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionPlayerTalk : ActorAction
    {
        readonly Actor target;
        readonly bool? answer;
        public ActionPlayerTalk(Actor player, RogueGame game, Actor target, bool? answer) : base(player, game)
        { this.target = target; this.answer = answer; }
        public override bool IsLegal()
        {
            if (!m_Actor.IsPlayer || target == null || target.IsDead || m_Actor.IsSleeping || target.IsSleeping ||
                !target.Model.Abilities.IsIntelligent || target.Model.Abilities.IsUndead ||
                m_Actor.Location.Map != target.Location.Map ||
                m_Game.Rules.GridDistance(m_Actor.Location.Position, target.Location.Position) > 1 ||
                m_Game.Rules.AreEnemies(m_Actor, target)) return false;
            NpcReaction pending = NpcConversation.Pending(m_Actor, target);
            if (pending == null) return true;
            if (!answer.HasValue) return false;
            NpcEventDefinition request = m_Game.NpcContent.Event(pending.Kind);
            if (request == null || request.PlayerReply == null) return false;
            if (!answer.Value) return true;
            NpcEventDefinition outcome = m_Game.NpcContent.Event(request.PlayerReply.YesKind);
            Actor beneficiary = NpcConversation.ReplyBeneficiary(target, pending);
            return beneficiary != null && (outcome == null || outcome.CanReply == null || outcome.CanReply(m_Actor, beneficiary));
        }
        public override void Perform()
        {
            if (!IsLegal()) return;
            SocialGroup group = target.SocialGroup;
            bool firstCommunity = group != null && (m_Actor.Personality == null || m_Actor.Personality.FindCommunity(group.Identity) == null);
            NpcReaction pending = NpcConversation.Pending(m_Actor, target);
            if (pending != null) {
                NpcConversation.Reply(m_Game, m_Actor, target, pending, answer.Value);
                if (CommunityKnowledgeSystem.Talk(m_Game, target, m_Actor, false) && firstCommunity) m_Game.CommunityControlHint();
                return;
            }
            bool grouped = group != null && m_Game.Session.GamePreset.NpcPersonalitiesEnabled;
            if (grouped) m_Game.BeginCommunityConversation();
            m_Game.DoSay(m_Actor, target, "Any news?", grouped ? RogueGame.Sayflags.IS_BRIEF : RogueGame.Sayflags.NONE);
            bool community = CommunityKnowledgeSystem.Talk(m_Game, target, m_Actor);
            NpcFact rumor = NpcConversation.Rumor(target, m_Actor);
            if (rumor != null && !community) NpcConversation.ShareRumor(m_Game, target, m_Actor, rumor, true);
            else if (!community) m_Game.DoSay(target, m_Actor, "Nothing new for now.", RogueGame.Sayflags.IS_FREE_ACTION);
            if (community && firstCommunity) m_Game.CommunityControlHint();
        }
    }
}
