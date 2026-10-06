using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine.Actions
{
    sealed class ActionCommunityAlarm : ActorAction
    {
        readonly Actor threat;
        public ActionCommunityAlarm(Actor actor, RogueGame game, Actor threat) : base(actor, game) { this.threat = threat; }
        public override bool IsLegal() { return !m_Actor.IsDead && !m_Actor.IsSleeping && threat != null && !threat.IsDead &&
            m_Actor.SocialGroup != null && m_Actor.SocialGroup.Home != null &&
            m_Game.Rules.AreEnemies(m_Actor, threat) && NpcKnowledgeSystem.Visible(m_Game, m_Actor, threat.Location); }
        public override void Perform()
        {
            if (!IsLegal()) return;
            m_Actor.SocialGroup.Home.NextAlarmTurn = m_Actor.Location.Map.LocalTime.TurnCounter + 30;
            string location = CommunityKnowledgeSystem.PlaceLabel(threat.Location);
            m_Game.DoSay(m_Actor, m_Actor, "Alarm! " + threat.UnmodifiedName + " threatens " + m_Actor.SocialGroup.CommunityName +
                " at " + location + "!", RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_SHOUT |
                RogueGame.Sayflags.IS_DANGER | RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_RUMOR);
            SignificantEvent source = NpcEvents.Publish(m_Game, "community_alarm", threat, m_Actor);
            var report = new NpcFact { EventId = source.Id, Kind = source.Kind, SubjectId = threat.PersonalityIdentity,
                SubjectName = threat.UnmodifiedName, OtherId = m_Actor.PersonalityIdentity, OtherName = m_Actor.UnmodifiedName,
                Place = threat.Location, EventTurn = source.Turn, LearnedTurn = source.Turn, Confidence = 100,
                Source = NpcKnowledgeSource.Witness, SourceId = m_Actor.PersonalityIdentity };
            foreach (Actor listener in m_Actor.Location.Map.Actors)
                if (listener != m_Actor && !listener.IsDead && !listener.IsSleeping && !listener.Model.Abilities.IsUndead &&
                    listener.Model.Abilities.IsIntelligent && m_Game.Rules.StdDistance(listener.Location.Position,
                    m_Actor.Location.Position) <= listener.AudioRange) NpcKnowledgeSystem.Hear(m_Game, listener, m_Actor, report);
        }
    }
}
