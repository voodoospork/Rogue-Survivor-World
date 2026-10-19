using System;
using System.Collections.Generic;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        Session m_DiplomacySession;
        Dictionary<Guid, SocialGroup> m_DiplomacyGroups;
        internal void ClearDiplomacyCache() { m_DiplomacySession = null; m_DiplomacyGroups = null; }
        internal void RememberDiplomacyGroup(SocialGroup group)
        {
            if (group != null && m_DiplomacySession == m_Session && m_DiplomacyGroups != null)
                m_DiplomacyGroups[group.Identity] = group;
        }
        internal bool IdentifiesDiplomacySpeaker(Actor listener, Actor speaker)
        { return listener.IsPlayer ? IsVisibleToPlayer(speaker) : NpcKnowledgeSystem.Visible(this, listener, speaker.Location); }
        internal SocialGroup FindDiplomacyGroup(Guid id)
        {
            if (m_DiplomacySession != m_Session || m_DiplomacyGroups == null)
            {
                m_DiplomacySession = m_Session; m_DiplomacyGroups = new Dictionary<Guid, SocialGroup>();
                foreach (Map map in WorldMaps()) foreach (Actor actor in map.Actors)
                    if (!actor.IsDead && actor.SocialGroup != null) m_DiplomacyGroups[actor.SocialGroup.Identity] = actor.SocialGroup;
            }
            SocialGroup group;
            return m_DiplomacyGroups.TryGetValue(id, out group) && group.Members.Contains(group.LeaderId) ? group : null;
        }
        internal int EstablishLocalPoliceUnits()
        {
            if (!(m_Session.GamePreset.InitialCommunities || m_Session.GamePreset.OrganicCommunities) || !m_Session.GamePreset.NpcPersonalitiesEnabled) return 0;
            var names = new Dictionary<string, int>(); int total = 0;
            foreach (Map map in WorldMaps())
            {
                var available = new List<Actor>();
                foreach (Actor actor in map.Actors)
                    if (!actor.IsDead && !actor.IsPlayer && !actor.IsUnique && actor.Faction == m_GameFactions.ThePolice &&
                        actor.Leader == null && actor.CountFollowers == 0 && actor.SocialGroup == null) available.Add(actor);
                available.Sort((a, b) => {
                    int capacity = m_Rules.ActorMaxFollowers(b).CompareTo(m_Rules.ActorMaxFollowers(a));
                    if (capacity != 0) return capacity;
                    int row = a.Location.Position.Y.CompareTo(b.Location.Position.Y);
                    return row != 0 ? row : a.Location.Position.X.CompareTo(b.Location.Position.X);
                });
                bool station = map.Name.IndexOf("Police Station", StringComparison.OrdinalIgnoreCase) >= 0;
                while (available.Count > 0)
                {
                    Actor leader = available[0]; available.RemoveAt(0);
                    int limit = station ? m_Rules.ActorMaxFollowers(leader) : Math.Min(2, m_Rules.ActorMaxFollowers(leader));
                    var candidates = available.FindAll(a => station || m_Rules.GridDistance(leader.Location.Position, a.Location.Position) <= 6);
                    candidates.Sort((a, b) => {
                        int distance = m_Rules.GridDistance(leader.Location.Position, a.Location.Position).CompareTo(m_Rules.GridDistance(leader.Location.Position, b.Location.Position));
                        if (distance != 0) return distance;
                        int row = a.Location.Position.Y.CompareTo(b.Location.Position.Y);
                        return row != 0 ? row : a.Location.Position.X.CompareTo(b.Location.Position.X);
                    });
                    for (int i = 0; i < candidates.Count && i < limit; i++) { leader.AddFollower(candidates[i]); available.Remove(candidates[i]); }
                    SocialGroup group = leader.EnsurePersonalGroup();
                    string district = map.District == null ? map.Name : World.CoordToString(map.District.WorldPosition.X, map.District.WorldPosition.Y);
                    string prefix = station ? "Police Station detail" : "District " + district + " police patrol";
                    int number; names.TryGetValue(prefix, out number); names[prefix] = ++number;
                    group.CommunityName = prefix + " " + number;
                    group.Diplomacy = new CommunityDiplomacyState(); total++;
                    CommunityCultureSystem.Initialize(this,group,leader.PartyTree());
                    foreach (Actor member in leader.PartyTree()) CommunityKnowledgeSystem.RefreshKnownSurroundings(this, member);
                }
            }
            ClearDiplomacyCache(); return total;
        }
        void ReportCommunityCombat(Actor attacker, Actor victim, bool killing)
        {
            if (!m_Session.GamePreset.NpcPersonalitiesEnabled || attacker.Model.Abilities.IsUndead || victim.Model.Abilities.IsUndead ||
                !attacker.Model.Abilities.IsIntelligent || !victim.Model.Abilities.IsIntelligent || attacker == victim) return;
            if (!CommunityDiplomacySystem.PoliticalGroup(attacker) && !CommunityDiplomacySystem.PoliticalGroup(victim)) return;
            int turn = m_Session.WorldTime.TurnCounter;
            var episodes = attacker.CommunityCombatEpisodes ?? (attacker.CommunityCombatEpisodes = new List<CommunityCombatEpisode>());
            var episode = episodes.Find(e => e.Victim == victim.PersonalityIdentity && turn - e.LastTurn < 30);
            if (episode == null)
            {
                episode = new CommunityCombatEpisode { Id = m_Session.NextPersonalityEventId(), Victim = victim.PersonalityIdentity };
                episodes.Add(episode); if (episodes.Count > 16) episodes.RemoveAt(0);
            }
            episode.LastTurn = turn;
            if (!killing && episode.InjuryReported) return;
            episode.InjuryReported = true;
            var source = new SignificantEvent(killing ? "community_killing" : "community_injury", victim, attacker,
                victim.Location.Map, victim.Location.Position, turn) { CombatEpisode = episode.Id,
                KnownMurder = killing && m_Rules.IsMurder(attacker, victim), KnownSelfDefense = attacker.IsSelfDefenceFrom(victim)||CommunityCultureSystem.LocalSelfDefense(attacker,victim) };
            PersonalitySystem.Report(this, source);
        }
    }
}
