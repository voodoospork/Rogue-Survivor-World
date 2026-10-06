using System;
using System.Collections.Generic;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    sealed class ResolvedCommunityHome
    {
        public Map Map;
        public XpdBase Claim;
        public Location Anchor;
        public int RetryTurn;
        public bool Present { get {
            if (Map == null || Claim == null || Claim.GroupLeader == null || Claim.GroupLeader.IsDead) return false;
            foreach (XpdBase claim in Map.XpdBases) if (claim == Claim) return true;
            return false;
        } }
    }
    partial class RogueGame
    {
        Session m_HomeCacheSession;
        readonly Dictionary<Guid, ResolvedCommunityHome> m_CommunityHomes = new Dictionary<Guid, ResolvedCommunityHome>();
        internal void InitializeCommunityHome(Actor actor)
        {
            if (!m_Session.GamePreset.Bases || !m_Session.GamePreset.NpcPersonalitiesEnabled || actor == null ||
                actor.SocialGroup == null || actor.GroupRoot.IsPlayer || actor.Faction != m_GameFactions.TheCivilians) return;
            XpdBase claim = actor.Location.Map.XpdBaseAt(actor.Location.Position);
            if (claim == null || claim.GroupLeader.IsPlayer || claim.CommunityIdentity != actor.SocialGroup.Identity) return;
            CommunityHomeState home = actor.SocialGroup.Home;
            if (home != null) return;
            actor.SocialGroup.Home = new CommunityHomeState { Place = CommunityShelterLocation.At(actor.Location) };
            CacheCommunityHome(actor.SocialGroup, actor.Location.Map, claim);
        }
        void CheckCommunityHomeCache()
        { if (m_HomeCacheSession != m_Session) { m_CommunityHomes.Clear(); m_HomeCacheSession = m_Session; } }
        void CacheCommunityHome(SocialGroup group, Map map, XpdBase claim)
        {
            CheckCommunityHomeCache();
            m_CommunityHomes[group.Identity] = new ResolvedCommunityHome { Map = map, Claim = claim,
                Anchor = new Location(map, group.Home.Place.Anchor) };
        }
        internal ResolvedCommunityHome ResolveCommunityHome(Actor actor)
        {
            SocialGroup group = actor == null ? null : actor.SocialGroup;
            if (group == null || group.Home == null || !m_Session.GamePreset.Bases || !m_Session.GamePreset.NpcPersonalitiesEnabled) return null;
            CheckCommunityHomeCache();
            ResolvedCommunityHome found;
            int turn = actor.Location.Map.LocalTime.TurnCounter;
            if (m_CommunityHomes.TryGetValue(group.Identity, out found))
            {
                if (found.Present) return found;
                group.Home.Watcher = Guid.Empty;
                if (turn < found.RetryTurn) return null;
            }
            // One resolution per group on reload/loss, never one world scan per NPC action.
            var maps = new List<Map> { actor.Location.Map };
            foreach (Map map in WorldMaps()) if (!maps.Contains(map)) maps.Add(map);
            foreach (Map map in maps)
            {
                CommunityShelterLocation place = group.Home.Place;
                if (map.Seed != place.MapSeed || map.Name != place.MapName ||
                    (map.District == null ? place.District.HasValue : place.District != map.District.WorldPosition)) continue;
                XpdBase claim = map.XpdBaseAt(place.Anchor);
                if (claim != null && !claim.GroupLeader.IsDead && claim.CommunityIdentity == group.Identity)
                { CacheCommunityHome(group, map, claim); return m_CommunityHomes[group.Identity]; }
            }
            m_CommunityHomes[group.Identity] = new ResolvedCommunityHome { RetryTurn = turn + 30 };
            group.Home.Watcher = Guid.Empty;
            return null;
        }
        internal bool AvailableForCommunityWatch(Actor actor)
        {
            OrderableAI ai = actor.Controller as OrderableAI;
            if (actor.IsDead || actor.IsSleeping || actor.InPlayerParty || ai == null || ai.Order != null ||
                m_Rules.IsActorTired(actor) || m_Rules.IsActorSleepy(actor) || m_Rules.IsActorStarving(actor) ||
                actor.HitPoints < m_Rules.ActorMaxHPs(actor) / 2) return false;
            NpcGroupPlan plan = actor.SocialGroup.Plan;
            if (plan != null && !plan.Finished && plan.CollectorId == actor.PersonalityIdentity) return false;
            NpcIntent intent = NpcIntentSystem.Select(NpcContent, actor);
            return intent == null || intent.Finished;
        }
        internal void ScheduleCommunityWatch(Actor actor)
        {
            ResolvedCommunityHome resolved = ResolveCommunityHome(actor);
            if (resolved == null) return;
            CommunityHomeState home = actor.SocialGroup.Home;
            int turn = actor.Location.Map.LocalTime.TurnCounter;
            Actor current = null;
            foreach (Actor member in actor.GroupRoot.PartyTree())
                if (member.PersonalityIdentity == home.Watcher) { current = member; break; }
            if (turn < home.NextRotationTurn && (current == null || AvailableForCommunityWatch(current))) return;
            Actor best = null;
            foreach (Actor member in actor.GroupRoot.PartyTree())
            {
                if (!AvailableForCommunityWatch(member) || member.Location.Map != resolved.Map || member == current) continue;
                if (best == null || member.SleepPoints > best.SleepPoints || (member.SleepPoints == best.SleepPoints &&
                    member.PersonalityIdentity.CompareTo(best.PersonalityIdentity) < 0)) best = member;
            }
            if (best == null && current != null && AvailableForCommunityWatch(current)) best = current;
            home.Watcher = best == null ? Guid.Empty : best.PersonalityIdentity;
            home.NextRotationTurn = turn + WorldTime.TURNS_PER_HOUR;
        }
        internal void TransferCommunityBases(Actor previous, Actor successor, Guid identity)
        {
            var maps = new List<Map> { previous.Location.Map };
            foreach (Map map in WorldMaps()) if (!maps.Contains(map)) maps.Add(map);
            foreach (Map map in maps) foreach (XpdBase claim in map.XpdBases)
                claim.TransferCommunity(previous, successor, identity);
        }
    }
}
