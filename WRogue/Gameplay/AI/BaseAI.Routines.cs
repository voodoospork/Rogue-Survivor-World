using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.AI;
using djack.RogueSurvivor.Engine.Items;

namespace djack.RogueSurvivor.Gameplay.AI
{
    abstract partial class BaseAI
    {
        [Serializable] sealed class RoutineState
        {
            public Location Target;
            public bool Rest;
            public int NextDecision, HP, Inventory, Stock;
            public long Event;
            public ActorOrder Order;
            public Activity Activity;
            public NpcPlanStep Step;
            public Actor TargetPerson;
            [OptionalField] public int Directives;
        }
        [OptionalField] RoutineState routine;
        [OptionalField] int nextGoals, goalInventory, goalHP;
        [OptionalField] long goalEvent;
        [OptionalField] int nextTradeScan;
        [NonSerialized] ActorAction directedAction;
        [NonSerialized] Location directedDestination;
        int NextRoutineDecision(int turn)
        {
            int phase = (m_Actor.PersonalityIdentity.GetHashCode() & Int32.MaxValue) % 4;
            int delta = (phase - turn % 4 + 4) % 4;
            return turn + (delta == 0 ? 4 : delta);
        }
        int DirectiveStamp()
        { var d = Directives; return (d.CanTakeItems ? 1 : 0) | (d.CanFireWeapons ? 2 : 0) | (d.CanThrowGrenades ? 4 : 0) |
            (d.CanSleep ? 8 : 0) | (d.CanTrade ? 16 : 0) | (int)d.Courage << 5; }
        void RememberDirectedAction(ActorAction action, System.Drawing.Point destination)
        { directedAction = action; directedDestination = new Location(m_Actor.Location.Map, destination); }
        internal void InvalidateRoutine(bool urgent = true) { routine = null; nextGoals = 0; if (urgent) nextTradeScan = 0; }
        protected bool TradeScanNow(RogueGame game)
        {
            if (game.Session.GamePreset.Simulation == SimulationMode.Classic) return true;
            int turn = m_Actor.Location.Map.LocalTime.TurnCounter;
            if (turn < nextTradeScan) return false;
            nextTradeScan = NextRoutineDecision(turn); return true;
        }
        internal bool QuietWait(RogueGame game)
        { return routine != null && routine.Rest && routine.Target == m_Actor.Location &&
            (m_Actor.StaminaPoints < game.Rules.ActorMaxSTA(m_Actor) ||
                Order != null && Order.Task == ActorTasks.GUARD && Order.Location == m_Actor.Location) &&
            routine.HP == m_Actor.HitPoints && routine.Inventory == InventoryStamp(m_Actor.Inventory, m_Actor.Location.Map.LocalTime.TurnCounter) &&
            routine.Order == Order && routine.Directives == DirectiveStamp() &&
            routine.Event == (m_Actor.Personality == null ? 0 : m_Actor.Personality.LastIntentEventId); }
        internal int QuietTravelUntil
        { get { return routine != null && !routine.Rest ? routine.NextDecision : 0; } }
        internal ActorAction QuietTravelAction(RogueGame game)
        { return routine != null && !routine.Rest ? ContinueRoutine(game, new List<Percept>()) : null; }
        internal static int InventoryStamp(Inventory inventory, int turn)
        {
            unchecked {
                int stamp = 17;
                if (inventory != null) foreach (Item item in inventory.Items) {
                    stamp = stamp * 31 + item.Model.ID; stamp = stamp * 31 + item.Quantity;
                    stamp = stamp * 31 + (item.IsEquipped ? 1 : 0);
                    stamp = stamp * 31 + (item is ItemPrimedExplosive ? 1 : 0);
                    var food = item as ItemFood;
                    if (food != null && food.IsPerishable) {
                        stamp = stamp * 31 + food.BestBefore.TurnCounter;
                        stamp = stamp * 31 + (turn < food.BestBefore.TurnCounter ? 0 : turn < 2 * food.BestBefore.TurnCounter ? 1 : 2);
                    }
                }
                return stamp;
            }
        }
        internal bool RefreshGoalsNow(RogueGame game, bool urgent)
        {
            if (game.Session.GamePreset.Simulation == SimulationMode.Classic) return true;
            int turn = m_Actor.Location.Map.LocalTime.TurnCounter, inventory = InventoryStamp(m_Actor.Inventory, m_Actor.Location.Map.LocalTime.TurnCounter);
            long eventId = m_Actor.Personality.LastIntentEventId;
            if (!urgent && turn < nextGoals && goalInventory == inventory && goalHP == m_Actor.HitPoints && goalEvent == eventId) return false;
            nextGoals = NextRoutineDecision(turn);
            goalInventory = inventory; goalHP = m_Actor.HitPoints; goalEvent = eventId;
            return true;
        }
        bool RoutineSafe(RogueGame game, IList<Percept> percepts, out int stock)
        {
            stock = 17;
            if (!(this is CivilianAI) || m_Actor.IsSleeping || m_Actor.IsDead || m_Actor.Predation != null ||
                Personality.RobberySystem.HasState(m_Actor) || Personality.CommunityTributeSystem.Active(m_Actor) ||
                Personality.CommunityAggressionSystem.PredatoryPolicy(m_Actor) ||
                game.Rules.IsActorHungry(m_Actor) || game.Rules.IsActorSleepy(m_Actor) || game.Rules.IsActorDisturbed(m_Actor) ||
                m_Actor.HitPoints < game.Rules.ActorMaxHPs(m_Actor)) return false;
            if (m_Actor.Activity == Activity.FIGHTING || m_Actor.Activity == Activity.CHASING ||
                m_Actor.Activity == Activity.FLEEING || m_Actor.Activity == Activity.FLEEING_FROM_EXPLOSIVE) return false;
            if (m_Actor.Personality != null && m_Actor.Personality.Reactions.Count > 0) return false;
            foreach (Item item in m_Actor.Inventory.Items) if (item is ItemPrimedExplosive) return false;
            if (game.Session.GamePreset.Infection && game.Rules.ActorInfectionPercent(m_Actor) >= game.Session.GamePreset.InfectionWeakThreshold) return false;
            if (percepts != null) foreach (Percept percept in percepts) {
                Actor other = percept.Percepted as Actor;
                if (other != null && (game.Rules.IsKnownEnemy(m_Actor, other) || other.IsPlayer &&
                    game.Rules.GridDistance(m_Actor.Location.Position, other.Location.Position) <= 4)) return false;
                Inventory inventory = percept.Percepted as Inventory;
                if (inventory != null) {
                    unchecked { stock = stock * 31 + percept.Location.Position.GetHashCode(); stock = stock * 31 + InventoryStamp(inventory, m_Actor.Location.Map.LocalTime.TurnCounter); }
                    foreach (Item item in inventory.Items) if (item is ItemPrimedExplosive) return false;
                }
            }
            return true;
        }
        ActorAction ContinueRoutine(RogueGame game, List<Percept> percepts)
        {
            if (game.Session.GamePreset.Simulation == SimulationMode.Classic || routine == null) return null;
            var state = routine;
            int stock;
            if (m_Actor.Location.Map != state.Target.Map || m_Actor.Location.Map.LocalTime.TurnCounter >= state.NextDecision ||
                state.HP != m_Actor.HitPoints || state.Order != Order || state.Inventory != InventoryStamp(m_Actor.Inventory, m_Actor.Location.Map.LocalTime.TurnCounter) ||
                state.Directives != DirectiveStamp() ||
                state.Event != (m_Actor.Personality == null ? 0 : m_Actor.Personality.LastIntentEventId) ||
                !RoutineSafe(game, percepts, out stock) || stock != state.Stock) { routine = null; return null; }
            if (state.Step != null && (m_Actor.Personality == null || !HasRoutineStep(state.Step))) { routine = null; return null; }
            if (!state.Rest && game.Rules.IsActorTired(m_Actor)) { routine = null; return null; }
            if (state.TargetPerson != null && percepts != null) foreach (var percept in percepts)
                if (percept.Percepted == state.TargetPerson && percept.Location != state.Target) { routine = null; return null; }
            ActorAction action;
            if (state.Rest) {
                if (!QuietWait(game)) { routine = null; return null; }
                action = new ActionWait(m_Actor, game);
            }
            else {
                if (game.Rules.GridDistance(m_Actor.Location.Position, state.Target.Position) <= 1) { routine = null; return null; }
                var proposed = BehaviorIntelligentBumpToward(game, state.Target.Position, false, false);
                var bump = proposed as ActionBump;
                action = bump == null ? proposed : bump.ConcreteAction;
                if (!(action is ActionMoveStep)) { routine = null; return null; }
            }
            if (action == null || !action.IsLegal()) { routine = null; return null; }
            m_Actor.Activity = state.Activity;
            m_Actor.TargetActor = state.TargetPerson;
            return action;
        }
        bool HasRoutineStep(NpcPlanStep step)
        { foreach (var goal in m_Actor.Personality.Intents) if (!goal.Finished && goal.Plan != null && goal.Plan.Current == step) return true; return false; }
        void RememberRoutine(RogueGame game, ActorAction action, List<Percept> percepts)
        {
            routine = null;
            if (game.Session.GamePreset.Simulation == SimulationMode.Classic) return;
            int stock;
            if (!RoutineSafe(game, percepts, out stock)) return;
            bool directed = action != null && action == directedAction;
            var bump = action as ActionBump; if (bump != null) action = bump.ConcreteAction;
            var move = action as ActionMoveStep;
            bool rest = action is ActionWait && (Order != null && Order.Task == ActorTasks.GUARD && Order.Location == m_Actor.Location ||
                m_Actor.StaminaPoints < game.Rules.ActorMaxSTA(m_Actor));
            if (!rest && move == null) return;
            Location target = m_Actor.Location; NpcPlanStep step = null;
            if (move != null) {
                if (directed) target = directedDestination;
                else if (Order != null && (Order.Task == ActorTasks.GUARD || Order.Task == ActorTasks.PATROL)) target = Order.Location;
                else if (m_Actor.Personality != null) foreach (var goal in m_Actor.Personality.Intents)
                    if (!goal.Finished && goal.Plan != null && goal.Plan.Current != null && goal.Plan.Current.Action == NpcPlanAction.Travel) {
                        target = goal.Plan.Current.Place; step = goal.Plan.Current; break;
                    }
                if (target.Map != m_Actor.Location.Map || game.Rules.GridDistance(target.Position, move.Destination.Position) >=
                    game.Rules.GridDistance(target.Position, m_Actor.Location.Position)) return;
            }
            routine = new RoutineState { Target = target, Rest = rest, NextDecision = NextRoutineDecision(m_Actor.Location.Map.LocalTime.TurnCounter),
                HP = m_Actor.HitPoints, Inventory = InventoryStamp(m_Actor.Inventory, m_Actor.Location.Map.LocalTime.TurnCounter), Stock = stock,
                Event = m_Actor.Personality == null ? 0 : m_Actor.Personality.LastIntentEventId, Order = Order, Step = step, Activity = m_Actor.Activity, Directives = DirectiveStamp() };
            if (!rest && percepts != null) foreach (var percept in percepts)
                if (percept.Percepted is Actor && percept.Location == target) { routine.TargetPerson = (Actor)percept.Percepted; break; }
        }
    }
}
