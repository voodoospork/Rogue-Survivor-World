using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        readonly HashSet<Map> adaptiveActive = new HashSet<Map>();
        readonly HashSet<District> adaptiveArrivalActive = new HashSet<District>();
        World adaptiveWorkWorld;
        System.Drawing.Point adaptiveWorkOrigin;
        List<District> adaptiveWorkOrder;
        bool IsAdaptive { get { return m_Session.GamePreset.Simulation == SimulationMode.AdaptiveWorld; } }
        AdaptiveDistrictState AdaptiveState(District district)
        {
            m_Session.World.EnsureWeatherHistory();
            bool created = district.Adaptive == null;
            if (district.Adaptive == null) district.Adaptive = new AdaptiveDistrictState {
                EventRandom = new DiceRoller(unchecked(m_Session.Seed * 397 ^ district.WorldPosition.X * 7919 ^ district.WorldPosition.Y * 104729)),
                EventsThrough = district.EntryMap.LocalTime.TurnCounter };
            string identityScope = String.Format(System.Globalization.CultureInfo.InvariantCulture, "rsw/{0}/{1}/{2}",
                m_Session.Seed, district.WorldPosition.X, district.WorldPosition.Y);
            if (district.Adaptive.Identities == null) district.Adaptive.Identities = AdaptiveIdentityState.Create(identityScope + "/events");
            if (district.Adaptive.Arrivals == null) district.Adaptive.Arrivals = new List<AdaptiveArrival>();
            foreach (Map map in district.Maps) {
                bool found = false;
                foreach (var saved in district.Adaptive.Maps) if (saved.Map == map) { found = true; break; }
                if (!found) district.Adaptive.Maps.Add(new AdaptiveMapState { Map = map,
                    Random = new DiceRoller(unchecked(map.Seed * 397 ^ district.Adaptive.Maps.Count * 7919)),
                    NextReviewTurn = map.LocalTime.TurnCounter });
            }
            for (int i = 0; i < district.Adaptive.Maps.Count; i++)
            {
                var state = district.Adaptive.Maps[i];
                if (state.Identities == null) state.Identities = AdaptiveIdentityState.Create(identityScope + "/map/" + i);
                if (!state.HasWorldTurn) {
                    state.WorldTurn = district == m_Session.CurrentMap?.District ? m_Session.WorldTime.TurnCounter : Math.Min(state.Map.LocalTime.TurnCounter,m_Session.WorldTime.TurnCounter);
                    state.HasWorldTurn = true;
                }
            }
            if (created) district.Adaptive.EventsThrough = district.Adaptive.Maps.Find(s => s.Map == district.EntryMap).WorldTurn;
            return district.Adaptive;
        }
        AdaptiveMapState AdaptiveState(Map map)
        { foreach (var state in AdaptiveState(map.District).Maps) if (state.Map == map) return state; throw new InvalidOperationException("Missing map simulation state."); }
        bool PinnedMap(Map map)
        {
            if (map == m_Session.CurrentMap) return true;
            foreach (Actor actor in map.Actors) if (actor.InPlayerParty) return true;
            return false;
        }
        int QuietHorizon(Map map, AdaptiveMapState state, int target)
        {
            int turn = map.LocalTime.TurnCounter;
            if (PinnedMap(map) || map.CountCorpses > 0 || map.CountTimers > 0) return 0;
            foreach (var arrival in map.District.Adaptive.Arrivals) if (arrival.From.Map == map) return 0;
            int stamp = QuietStamp(map);
            if (state.QuietUntil > turn && state.QuietStamp == stamp && state.QuietGeometry == map.GeometryRevision && state.QuietOccupancy == map.OccupancyRevision)
                return Math.Min(target - turn, state.QuietUntil - turn);
            // Review eligibility at the boundary. A still-valid quiet activity needs
            // no full AI round merely because its previous safety proof expired.
            int review = state.NextReviewTurn > turn ? state.NextReviewTurn : turn + WorldTime.TURNS_PER_HOUR;
            int limit = review - turn;
            limit = Math.Min(limit, WorldTime.TURNS_PER_HOUR - turn % WorldTime.TURNS_PER_HOUR);
            limit = Math.Min(limit, m_Session.World.NextWeatherChange(state.WorldTurn) - state.WorldTurn);
            foreach (var obj in map.MapObjects) if (obj.IsOnFire) return 0;
            foreach (var inventory in map.GroundInventories)
                foreach (var item in inventory.Items) if (item is ItemPrimedExplosive) return 0;
            foreach (Actor actor in map.Actors) {
                if (actor.IsDead || actor.IsPlayer || actor.Model.Abilities.IsUndead || actor.Model.Abilities.IsRotting || actor.Infection > 0 || actor.IsRunning ||
                    actor.HitPoints != m_Rules.ActorMaxHPs(actor) || m_Rules.IsActorDisturbed(actor) ||
                    m_Rules.IsActorHungry(actor) || actor.DraggedCorpse != null || actor.CommunityRaid != null) return 0;
                var brain = actor.Controller as BaseAI;
                if (!actor.IsSleeping && (brain == null || !brain.QuietWait(this))) {
                    // The first travel batch supports an isolated resident on a map with no loose supplies.
                    // Other encounters and interactions retain the ordinary detailed scheduler.
                    if (brain == null || map.CountActors != 1 || map.CountGroundInventories != 0 || brain.QuietTravelUntil <= turn) return 0;
                    limit = Math.Min(limit, brain.QuietTravelUntil - turn);
                }
                if (actor.HasLeader) {
                    if (m_Rules.HasActorBondWith(actor, actor.Leader)) return 0;
                    int trust = m_Rules.ActorTrustIncrease(actor.Leader, actor);
                    if (trust > 0) limit = Math.Min(limit, (Rules.TRUST_BOND_THRESHOLD - actor.TrustInLeader - 1) / trust);
                }
                if (actor.Personality != null && actor.Personality.HasPendingSocialState) return 0;
                if (actor.Model.Abilities.HasToEat) limit = Math.Min(limit, actor.FoodPoints - m_Session.GamePreset.HungerPoints - 1);
                if (actor.Model.Abilities.HasSanity) limit = Math.Min(limit, actor.Sanity - m_Rules.ActorDisturbedLevel(actor) - 1);
                if (actor.Model.Abilities.HasToSleep) {
                    if (actor.IsSleeping) {
                        int gain = Math.Max(1, m_Rules.ActorSleepRegen(actor, m_Rules.IsOnCouch(actor)));
                        limit = Math.Min(limit, (m_Rules.ActorMaxSleep(actor) - actor.SleepPoints - 1) / gain);
                    }
                    else limit = Math.Min(limit, (actor.SleepPoints - m_Session.GamePreset.SleepPoints - 1) / (map.LocalTime.IsNight ? 2 : 1));
                }
                foreach (Item item in actor.Inventory.Items) {
                    if (item is ItemPrimedExplosive || item.IsEquipped && (item is ItemLight || item is ItemTracker)) return 0;
                    var food = item as ItemFood;
                    if (food != null && food.IsPerishable) {
                        int next = turn < food.BestBefore.TurnCounter ? food.BestBefore.TurnCounter : 2 * food.BestBefore.TurnCounter;
                        if (next > turn) limit = Math.Min(limit, next - turn);
                    }
                }
                foreach (Actor other in map.Actors) if (actor != other && m_Rules.AreEnemies(actor, other)) return 0;
            }
            state.NextReviewTurn = review;
            state.QuietUntil = turn + Math.Max(0,limit); state.QuietStamp = stamp;
            state.QuietGeometry = map.GeometryRevision; state.QuietOccupancy = map.OccupancyRevision;
            return Math.Min(target - turn, Math.Max(0, limit));
        }
        int QuietStamp(Map map)
        {
            unchecked {
                int stamp = 17 + (int)m_Session.World.Weather * 31 + (int)map.Lighting;
                foreach (var actor in map.Actors) {
                    stamp = stamp*31 + actor.HitPoints; stamp = stamp*31 + actor.Infection;
                    stamp = stamp*31 + (int)actor.Activity;
                    stamp = stamp*31 + (actor.IsDead || actor.IsRunning || actor.DraggedCorpse != null || actor.CommunityRaid != null ? 1 : 0);
                    stamp = stamp*31 + (actor.Leader == null ? 0 : actor.Leader.PersonalityIdentity.GetHashCode());
                    stamp = stamp*31 + (actor.Personality?.HasPendingSocialState == true ? 1 : 0);
                    stamp = stamp*31 + ((actor.Controller as BaseAI)?.QuietWait(this) == true ? 1 : 0);
                    stamp = stamp*31 + ((actor.Controller as BaseAI)?.QuietTravelUntil ?? 0);
                    stamp = stamp*31 + (actor.IsSleeping ? 1 : 0); stamp = stamp*31 + BaseAI.InventoryStamp(actor.Inventory, map.LocalTime.TurnCounter);
                    stamp = stamp*31 + (actor.Personality == null ? 0 : actor.Personality.LastIntentEventId.GetHashCode());
                }
                foreach (var inv in map.GroundInventories) stamp = stamp*31 + BaseAI.InventoryStamp(inv, map.LocalTime.TurnCounter);
                foreach (var obj in map.MapObjects) stamp = stamp*31 + (obj.IsOnFire ? 1 : 0);
                return stamp;
            }
        }
        bool QuietTick(Map map, AdaptiveMapState state)
        {
            int before = map.LocalTime.TurnCounter;
            bool night = map.LocalTime.IsNight;
            foreach (Actor actor in map.Actors) if (!actor.IsSleeping) {
                var brain = actor.Controller as BaseAI;
                bool travel = !brain.QuietWait(this);
                while (travel && actor.ActionPoints > 0) {
                    var action = brain.QuietTravelAction(this);
                    if (action == null || !action.IsLegal()) return false;
                    actor.PreviousStaminaPoints = actor.StaminaPoints;
                    action.Perform();
                    actor.PreviousHitPoints = actor.HitPoints; actor.PreviousFoodPoints = actor.FoodPoints;
                    actor.PreviousSleepPoints = actor.SleepPoints; actor.PreviousSanity = actor.Sanity;
                }
                if (!travel) {
                    int actions = Math.Max(0, (actor.ActionPoints + Rules.BASE_ACTION_COST - 1) / Rules.BASE_ACTION_COST);
                    if (actions > 0) {
                        actor.PreviousStaminaPoints = Math.Min(m_Rules.ActorMaxSTA(actor), actor.StaminaPoints + (actions - 1) * Rules.STAMINA_REGEN_WAIT);
                        actor.PreviousHitPoints = actor.HitPoints; actor.PreviousFoodPoints = actor.FoodPoints;
                        actor.PreviousSleepPoints = actor.SleepPoints; actor.PreviousSanity = actor.Sanity;
                        actor.ActionPoints -= actions * Rules.BASE_ACTION_COST; actor.LastActionTurn = before;
                        actor.StaminaPoints = Math.Min(m_Rules.ActorMaxSTA(actor), actor.StaminaPoints + actions * Rules.STAMINA_REGEN_WAIT);
                    }
                }
            }
            AdvanceMapScents(map);
            foreach (Actor actor in map.Actors) {
                int maxSta = m_Rules.ActorMaxSTA(actor);
                if (!actor.IsSleeping) {
                    actor.ActionPoints += m_Rules.ActorSpeed(actor);
                }
                actor.StaminaPoints = Math.Min(maxSta, actor.StaminaPoints + Rules.STAMINA_REGEN_PER_TURN);
                if (actor.Model.Abilities.HasToEat) actor.FoodPoints--;
                if (actor.Model.Abilities.HasToSleep) {
                    if (actor.IsSleeping) { actor.SleepPoints = Math.Min(m_Rules.ActorMaxSleep(actor), actor.SleepPoints + m_Rules.ActorSleepRegen(actor, m_Rules.IsOnCouch(actor))); actor.Activity = Activity.SLEEPING; }
                    else actor.SleepPoints -= night ? 2 : 1;
                }
                if (actor.Model.Abilities.HasSanity) actor.Sanity--;
                if (actor.HasLeader) ModifyActorTrustInLeader(actor, m_Rules.ActorTrustIncrease(actor.Leader, actor), false);
            }
            map.CheckNextActorIndex = 0;
            map.LocalTime.TurnCounter = before + 1;
            NpcIntentSystem.AdvanceClock(this, map);
            if (before / WorldTime.TURNS_PER_DAY != map.LocalTime.Day) PersonalitySystem.ResolveDue(this, map);
            if (night && !map.LocalTime.IsNight) HandleLivingNPCsUpgrade(map);
            state.QuietTurns++;
            state.WorldTurn++;
            return true;
        }
        internal void AdvanceAdaptiveMap(Map map, int target)
        {
            var state = AdaptiveState(map); int before = state.WorldTurn;
            int steps = 0;
            do {
                AdvanceAdaptiveStep(map, target);
                if (adaptiveActive.Contains(map) || before >= target) return;
                if (++steps > Math.Max(256, map.CountActors * 64)) throw new InvalidOperationException("Adaptive detailed round did not finish.");
            } while (state.WorldTurn == before);
        }
        void AdvanceAdaptiveStep(Map map, int target)
        {
            var state = AdaptiveState(map); state.TargetTurn = Math.Max(state.TargetTurn, target);
            if (state.WorldTurn >= target || adaptiveActive.Contains(map)) return;
            adaptiveActive.Add(map);
            try {
                using (new SimulationClock(m_Session, state.WorldTurn, state.Random, null, state.Identities)) {
                    int localTarget = target + map.LocalTime.TurnCounter - state.WorldTurn;
                    if (state.DetailedInProgress || QuietHorizon(map, state, localTarget) <= 0 || !QuietTick(map, state)) {
                        int before = map.LocalTime.TurnCounter;
                        state.DetailedInProgress = true;
                        AdvancePlay(map, SimFlags.HIDETAIL_TURN);
                        if (map.LocalTime.TurnCounter != before) {
                            state.DetailedInProgress = false;
                            state.DetailedTurns++; state.NextReviewTurn = map.LocalTime.TurnCounter + WorldTime.TURNS_PER_HOUR;
                        }
                    }
                }
            } finally { adaptiveActive.Remove(map); }
        }
        internal bool AdvanceAdaptiveDistrict(District district, int target, int maxRounds)
        {
            var state = AdaptiveState(district);
            ResolveAdaptiveArrivals(district, target);
            foreach (var map in state.Maps) map.TargetTurn = Math.Max(map.TargetTurn, target);
            for (int round = 0; round < maxRounds; round++) {
                int earliest = target;
                foreach (var map in state.Maps) earliest = Math.Min(earliest, map.WorldTurn);
                if (earliest >= target) return true;
                foreach (var map in state.Maps) {
                    if (map.WorldTurn > earliest || adaptiveActive.Contains(map.Map)) continue;
                    // Player input belongs to the normal scheduler, never background work.
                    if (map.Map == m_Session.CurrentMap) return false;
                    AdvanceAdaptiveMap(map.Map, earliest + 1);
                }
                AdaptiveDistrictEvents(district);
                ResolveAdaptiveArrivals(district, target);
            }
            foreach (var map in state.Maps) if (map.WorldTurn < target) return false;
            return true;
        }
        void AdaptiveDistrictEvents(District district)
        {
            var state = AdaptiveState(district);
            int frontier = state.Maps.Find(s => s.Map == district.EntryMap).WorldTurn;
            foreach (var map in state.Maps) frontier = Math.Min(frontier, map.WorldTurn);
            while (state.EventsThrough < frontier) {
            int turn = state.EventsThrough + 1;
            using (new SimulationClock(m_Session, turn, state.EventRandom, district, state.Identities)) {
                if (CheckForEvent_ZombieInvasion(district.EntryMap)) FireEvent_ZombieInvasion(district.EntryMap);
                if (CheckForEvent_RefugeesWave(district.EntryMap)) FireEvent_RefugeesWave(district);
                if (CheckForEvent_NationalGuard(district.EntryMap)) FireEvent_NationalGuard(district.EntryMap);
                if (CheckForEvent_ArmySupplies(district.EntryMap)) FireEvent_ArmySupplies(district.EntryMap);
                if (CheckForEvent_BikersRaid(district.EntryMap)) FireEvent_BikersRaid(district.EntryMap);
                if (CheckForEvent_GangstasRaid(district.EntryMap)) FireEvent_GangstasRaid(district.EntryMap);
                if (CheckForEvent_BlackOpsRaid(district.EntryMap)) FireEvent_BlackOpsRaid(district.EntryMap);
                if (CheckForEvent_BandOfSurvivors(district.EntryMap)) FireEvent_BandOfSurvivors(district.EntryMap);
                if (district.SewersMap != null && CheckForEvent_SewersInvasion(district.SewersMap)) FireEvent_SewersInvasion(district.SewersMap);
            }
            state.EventsThrough = turn;
            }
        }
        void AdaptiveIdleSlice()
        {
            if (!IsAdaptive || m_Session.GamePreset.AdaptivePauseBackground || m_Player == null || m_Player.IsDead || !m_IsGameRunning) return;
            int target = m_Session.WorldTime.TurnCounter;
            var watch = Stopwatch.StartNew();
            int budget = m_Session.GamePreset.AdaptiveSliceMilliseconds;
            EnsureAdaptiveWorkOrder();
            for (int visited = 0; visited < adaptiveWorkOrder.Count && watch.ElapsedMilliseconds < budget; visited++)
                AdvanceAdaptiveWork(target);
        }
        void EnsureAdaptiveWorkOrder()
        {
            var origin = m_Player.Location.Map.District.WorldPosition;
            if (adaptiveWorkOrder != null && adaptiveWorkWorld == m_Session.World && adaptiveWorkOrigin == origin) return;
            adaptiveWorkWorld = m_Session.World; adaptiveWorkOrigin = origin;
            if (m_Session.AdaptiveWorkOrigin != origin) { m_Session.AdaptiveWorkOrigin = origin; m_Session.AdaptiveWorkCursor = 0; }
            adaptiveWorkOrder = new List<District>();
            for (int y = 0; y < m_Session.World.Size; y++) for (int x = 0; x < m_Session.World.Size; x++)
                if (m_Session.World[x,y] != null) adaptiveWorkOrder.Add(m_Session.World[x,y]);
            adaptiveWorkOrder.Sort((a,b) => {
                int da = Math.Max(Math.Abs(a.WorldPosition.X-origin.X), Math.Abs(a.WorldPosition.Y-origin.Y));
                int db = Math.Max(Math.Abs(b.WorldPosition.X-origin.X), Math.Abs(b.WorldPosition.Y-origin.Y));
                int order = da.CompareTo(db); if (order != 0) return order;
                order = a.WorldPosition.Y.CompareTo(b.WorldPosition.Y); return order != 0 ? order : a.WorldPosition.X.CompareTo(b.WorldPosition.X);
            });
        }
        internal bool AdvanceAdaptiveWork(int target)
        {
            EnsureAdaptiveWorkOrder();
            if (adaptiveWorkOrder.Count == 0) return false;
            int cursor = m_Session.AdaptiveWorkCursor % adaptiveWorkOrder.Count;
            var district = adaptiveWorkOrder[cursor];
            // Persist the next queue position: a shorter slice or reload cannot restart the nearest district and starve its neighbours.
            m_Session.AdaptiveWorkCursor = (cursor + 1) % adaptiveWorkOrder.Count;
            return AdvanceAdaptiveDistrictSlice(district, target);
        }
        bool AdvanceAdaptiveDistrictSlice(District district, int target)
        {
            var state = AdaptiveState(district);
            if (ResolveAdaptiveArrivals(district, target)) return true;
            int earliest = target;
            foreach (var map in state.Maps) { map.TargetTurn = Math.Max(map.TargetTurn, target); earliest = Math.Min(earliest, map.WorldTurn); }
            if (earliest >= target) return false;
            // Stable ordering resumes the same actor after a slice or save/reload.
            foreach (var map in state.Maps) {
                if (map.Map == m_Session.CurrentMap || map.WorldTurn != earliest || adaptiveActive.Contains(map.Map)) continue;
                AdvanceAdaptiveStep(map.Map, earliest + 1);
                AdaptiveDistrictEvents(district);
                return true;
            }
            return false;
        }
        bool PrepareAdaptiveArrival(Actor actor, Exit exit)
        {
            if (!IsAdaptive || exit == null) return true;
            if (exit.ToMap == actor.Location.Map) return true;
            int target = actor.IsPlayer ? m_Session.WorldTime.TurnCounter : AdaptiveState(actor.Location.Map).WorldTurn;
            if (!actor.IsPlayer) {
                // Never insert an arrival into a map whose history has already passed it.
                int destinationTurn = AdaptiveState(exit.ToMap).WorldTurn;
                if (destinationTurn > target) {
                    var pending = AdaptiveState(actor.Location.Map.District).Arrivals;
                    var previous = pending.Find(a => a.Actor == actor);
                    if (previous == null) pending.Add(new AdaptiveArrival { Actor = actor, From = actor.Location, DueTurn = destinationTurn });
                    else { previous.From = actor.Location; previous.DueTurn = destinationTurn; }
                    return false;
                }
                if (adaptiveActive.Contains(exit.ToMap)) return false;
                while (!AdvanceAdaptiveDistrict(exit.ToMap.District, target, 1))
                    foreach (var map in AdaptiveState(exit.ToMap.District).Maps)
                        if (map.WorldTurn < target && (adaptiveActive.Contains(map.Map) || map.Map == m_Session.CurrentMap)) return false;
                return AdaptiveState(exit.ToMap).WorldTurn == target;
            }
            while (!AdvanceAdaptiveDistrict(exit.ToMap.District, target, 1)) {
                if (m_UI.UI_PeekKey()?.KeyCode == Keys.Escape) return false;
                if (exit.ToMap == m_Session.CurrentMap) return false;
            }
            return true;
        }
        bool ResolveAdaptiveArrivals(District district, int target)
        {
            if (adaptiveArrivalActive.Contains(district)) return false;
            var scheduler = district.Adaptive;
            var pending = scheduler.Arrivals;
            if (pending.Count == 0) return false;
            var ordered = new List<AdaptiveArrival>(pending);
            ordered.Sort((a,b) => { int order = a.DueTurn.CompareTo(b.DueTurn); return order != 0 ? order : a.Actor.PersonalityIdentity.CompareTo(b.Actor.PersonalityIdentity); });
            bool acted = false; adaptiveArrivalActive.Add(district);
            try {
                foreach (var arrival in ordered) {
                    Actor actor = arrival.Actor;
                    if (actor.IsDead || actor.IsPlayer || actor.Location != arrival.From || actor.Controller == null) { pending.Remove(arrival); continue; }
                    Map map = actor.Location.Map;
                    var state = AdaptiveState(map); int turn = state.WorldTurn;
                    if (arrival.DueTurn > turn || turn > target || actor.IsSleeping || actor.ActionPoints <= 0 || adaptiveActive.Contains(map)) continue;
                    // Use the same safe point as a continuous district round. A slice
                    // must not insert this action before other floors or world events.
                    if (state.DetailedInProgress || scheduler.EventsThrough < turn) continue;
                    bool boundary = true;
                    foreach (var floor in scheduler.Maps)
                        if (floor.WorldTurn < turn || floor.WorldTurn == turn && floor.DetailedInProgress) { boundary = false; break; }
                    if (!boundary) continue;
                    pending.Remove(arrival);
                    state.DetailedInProgress = true; adaptiveActive.Add(map);
                    try {
                        using (new SimulationClock(m_Session, turn, state.Random, null, state.Identities)) {
                            // Reconsider normally at the next legal action. Damage, orders or a blocked route may cancel the crossing.
                            actor.PreviousStaminaPoints = actor.StaminaPoints;
                            HandleAiActor(actor);
                            actor.PreviousHitPoints = actor.HitPoints; actor.PreviousFoodPoints = actor.FoodPoints;
                            actor.PreviousSleepPoints = actor.SleepPoints; actor.PreviousSanity = actor.Sanity;
                        }
                    } finally { adaptiveActive.Remove(map); }
                    acted = true;
                }
            } finally { adaptiveArrivalActive.Remove(district); }
            return acted;
        }
        internal void ChangeSimulationMode(SimulationMode mode)
        {
            if ((int)mode < 0 || (int)mode >= GamePreset.SimulationModeCount) throw new ArgumentOutOfRangeException(nameof(mode));
            bool enteringAdaptive = !IsAdaptive && mode == SimulationMode.AdaptiveWorld;
            StopSimThread(false);
            // Preserve the frontier and remaining AP. Changing scheduling must not run a player's unfinished input recursively.
            if (mode == SimulationMode.AdaptiveWorld) m_Session.World.RecordWeather(m_Session.WorldTime.TurnCounter);
            m_Session.GamePreset.Simulation = mode;
            for (int x = 0; x < m_Session.World.Size; x++)
            for (int y = 0; y < m_Session.World.Size; y++)
            {
                var district = m_Session.World[x,y];
                if (district == null) continue;
                if (enteringAdaptive && district.Adaptive != null) {
                    int frontier = Int32.MaxValue;
                    foreach (var state in district.Adaptive.Maps) {
                        if (district == m_Session.CurrentMap?.District) { state.WorldTurn = m_Session.WorldTime.TurnCounter; state.HasWorldTurn = true; }
                        frontier = Math.Min(frontier, state.WorldTurn);
                    }
                    // Classic may have already processed or deliberately skipped this interval.
                    if (frontier != Int32.MaxValue) district.Adaptive.EventsThrough = frontier;
                }
                foreach (var map in district.Maps)
                    foreach (var actor in map.Actors) (actor.Controller as BaseAI)?.InvalidateRoutine();
            }
            if (m_Player != null && m_IsGameRunning && mode != SimulationMode.AdaptiveWorld) AfterPlayerEnterDistrict();
        }
    }
}
