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
            if (Map == null || Claim == null || Claim.GroupLeader == null ||
                (Claim.GroupLeader.IsDead && (Claim.CommunityGroup == null || Claim.CommunityGroup.Tribe == null || Claim.CommunityGroup.Tribe.Dissolved))) return false;
            foreach (XpdBase claim in Map.XpdBases) if (claim == Claim) return true;
            return false;
        } }
    }
    partial class RogueGame
    {
        Session m_HomeCacheSession;
        readonly Dictionary<Guid, ResolvedCommunityHome> m_CommunityHomes = new Dictionary<Guid, ResolvedCommunityHome>();
        readonly Dictionary<string, Map> m_CommunityPlaceMaps = new Dictionary<string, Map>();
        internal void InitializeCommunityHome(Actor actor)
        { InitializeCommunityHomeCore(actor, false); }
        internal void InitializeLegacySettlementHome(Actor actor)
        { InitializeCommunityHomeCore(actor, true); }
        void InitializeCommunityHomeCore(Actor actor, bool explicitLegacySettlement)
        {
            if (!m_Session.GamePreset.Bases || !m_Session.GamePreset.NpcPersonalitiesEnabled || actor == null ||
                actor.SocialGroup == null || (actor.GroupRoot.IsPlayer && actor.Community == null) || actor.Faction != m_GameFactions.TheCivilians) return;
            // A new command party's private camp cannot enter compatibility migration
            // and bypass the independent founding proposal/consent path.
            if (m_Session.GamePreset.OrganicCommunities && actor.Community == null &&
                actor.SocialGroup.Home == null && !explicitLegacySettlement) return;
            XpdBase claim = actor.Location.Map.XpdBaseAt(actor.Location.Position);
            if (claim == null || claim.GroupLeader.IsPlayer || claim.CommunityIdentity != actor.SocialGroup.Identity) return;
            CommunityHomeState home = actor.SocialGroup.Home;
            if (home != null) return;
            actor.SocialGroup.Home = new CommunityHomeState { Place = CommunityShelterLocation.At(actor.Location) };
            CacheCommunityHome(actor.SocialGroup, actor.Location.Map, claim);
        }
        void CheckCommunityHomeCache()
        { if (m_HomeCacheSession != m_Session) { m_CommunityHomes.Clear(); m_CommunityPlaceMaps.Clear(); m_HomeCacheSession = m_Session; } }
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
                if (claim != null && (!claim.GroupLeader.IsDead || group.Tribe != null && !group.Tribe.Dissolved) && claim.CommunityIdentity == group.Identity)
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
            foreach (Actor member in actor.CommunityResidents)
                if (member.PersonalityIdentity == home.Watcher) { current = member; break; }
            if (turn < home.NextRotationTurn && (current == null || AvailableForCommunityWatch(current))) return;
            Actor best = null;
            foreach (Actor member in actor.CommunityResidents)
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
        internal void InvalidateCommunityHomes() { m_CommunityHomes.Clear(); }
        internal void RebindTribeClaims(SocialGroup group, Actor chief)
        { foreach (Map map in WorldMaps()) foreach (XpdBase claim in map.XpdBases) claim.RebindCommunity(chief, group.Identity); }
        internal void ReleaseTribeClaims(Actor source, SocialGroup group, XpdBase except = null)
        {
            var maps = new List<Map> { source.Location.Map }; foreach (Map map in WorldMaps()) if (!maps.Contains(map)) maps.Add(map);
            foreach (Map map in maps) { var release = new List<XpdBase>(); foreach (XpdBase claim in map.XpdBases)
                    if (claim.CommunityIdentity == group.Identity && (except == null || !claim.IsPartOf(except))) release.Add(claim);
                foreach (XpdBase claim in release) map.RemoveXpdBase(claim); }
            InvalidateCommunityHomes();
        }
        internal Location? ResolveCommunityPlace(Actor owner, CommunityShelterLocation place)
        {
            if (place == null) return null; CheckCommunityHomeCache();
            string key = place.MapSeed + ":" + place.MapName + ":" + place.District;
            Map found;
            if (!m_CommunityPlaceMaps.TryGetValue(key, out found))
            {
                var maps = new List<Map> { owner.Location.Map }; foreach (Map map in WorldMaps()) if (!maps.Contains(map)) maps.Add(map);
                foreach (Map map in maps) if (map.Seed == place.MapSeed && map.Name == place.MapName &&
                    (map.District == null ? !place.District.HasValue : place.District == map.District.WorldPosition)) { found = map; break; }
                if (m_CommunityPlaceMaps.Count >= 64) m_CommunityPlaceMaps.Clear(); m_CommunityPlaceMaps[key] = found;
            }
            return found == null ? (Location?)null : new Location(found, place.Anchor);
        }
        void ObserveReportedHomes(Actor player)
        {
            if (player == null || player.IsSleeping || player.Personality == null || !CommunityAdaptationSystem.Enabled(this)) return;
            int turn = player.Location.Map.LocalTime.TurnCounter;
            foreach (KnownCommunity known in player.Personality.Communities)
            {
                if (known.ShelterLocation == null || !known.ShelterLocation.Matches(player.Location.Map, player.Location.Position) ||
                    turn - known.LastHomeSurveyTurn < 60) continue;
                known.LastHomeSurveyTurn = turn; bool civilianSeen = false;
                foreach (Actor actor in player.Location.Map.Actors)
                    if (actor != player && !actor.IsDead && !actor.Model.Abilities.IsUndead &&
                        known.ShelterLocation.Matches(actor.Location.Map, actor.Location.Position) &&
                        NpcKnowledgeSystem.Visible(this, player, actor.Location)) { civilianSeen = true; break; }
                if (!civilianSeen) known.Note(new CommunityNote { EventId = m_Session.NextPersonalityEventId(), Kind = "home_apparently_empty",
                    EventTurn = turn, LearnedTurn = turn, Source = "observed here",
                    Text = "No living residents are visible at the reported home. It appears abandoned; survivors may be elsewhere." });
            }
        }
    }
}
