using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionCommunityDiplomacy : ActorAction
    {
        readonly CommunityRelation declaration;
        readonly Actor listener;
        readonly NpcFact report;
        readonly bool introduction;
        public ActionCommunityDiplomacy(Actor actor, RogueGame game) : base(actor, game) { introduction = true; }
        public ActionCommunityDiplomacy(Actor actor, RogueGame game, CommunityRelation relation) : base(actor, game) { declaration = relation; }
        public ActionCommunityDiplomacy(Actor actor, RogueGame game, Actor recipient, NpcFact fact) : base(actor, game) { listener = recipient; report = fact; }
        public override bool IsLegal()
        {
            return !m_Actor.IsDead && !m_Actor.IsSleeping && (introduction ? m_Actor.SocialGroup != null : declaration != null ? CommunityDiplomacySystem.Declaration(m_Actor) == declaration :
                listener != null && !listener.IsDead && !listener.IsSleeping && listener.Location.Map == m_Actor.Location.Map &&
                m_Game.Rules.StdDistance(m_Actor.Location.Position, listener.Location.Position) <= listener.AudioRange && !m_Game.Rules.IsKnownEnemy(m_Actor, listener));
        }
        public override void Perform()
        {
            if (introduction)
            {
                m_Game.DoSay(m_Actor, null, "We're " + m_Actor.SocialGroup.CommunityName + ".", RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FREE_ACTION);
                CommunityDiplomacySystem.Introduce(m_Game, m_Actor);
                (m_Actor.SocialGroup.Diplomacy ?? (m_Actor.SocialGroup.Diplomacy = new CommunityDiplomacyState())).NextIntroductionTurn = m_Game.Session.WorldTime.TurnCounter + 60;
                m_Game.SpendActorActionPoints(m_Actor, Rules.BASE_ACTION_COST); return;
            }
            if (declaration != null) { CommunityDiplomacySystem.Declare(m_Game, m_Actor, declaration); return; }
            string text = report.DiplomacyMessage != null ? report.DiplomacyMessage.Name + " reports " + report.DiplomacyMessage.Stance +
                " with " + report.DiplomacyMessage.Target.Name + ". " + report.DiplomacyMessage.Reason +
                "; report turn " + report.DiplomacyMessage.Turn + (report.DiplomacyMessage.PeaceTurn > 0 ? "; peace ratified turn " + report.DiplomacyMessage.PeaceTurn : "") + "." :
                (report.Source == NpcKnowledgeSource.Told ? "I was told: " : "I witnessed: ") + report.DiplomacyEvidence.Explanation +
                "; " + (report.SubjectName ?? "someone") + " (" + (report.DiplomacyEvidence.Subject.Name ?? "unknown affiliation") + "), " +
                (report.OtherName ?? "someone") + " (" + (report.DiplomacyEvidence.Other.Name ?? "unknown affiliation") + "), turn " + report.EventTurn + ".";
            m_Game.DoSay(m_Actor, listener, text, RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FREE_ACTION);
            NpcKnowledgeSystem.Hear(m_Game, listener, m_Actor, report.SpeechSnapshot());
            m_Actor.Personality.Knowledge.Told(report.EventId, listener.PersonalityIdentity, m_Game.Session.WorldTime.TurnCounter);
            m_Game.SpendActorActionPoints(m_Actor, Rules.BASE_ACTION_COST);
        }
    }
}
