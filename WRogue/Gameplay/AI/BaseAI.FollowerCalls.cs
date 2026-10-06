using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.AI;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Gameplay.AI
{
    abstract partial class BaseAI
    {
        [OptionalField] Guid m_FollowerCallLeader;
        [OptionalField] int m_FollowerCallExpires;
        [OptionalField] bool m_FollowerCallRecall;

        internal void HearPersonalRecall(RogueGame game, Actor caller)
        {
            if (!Travelling(m_Actor)) return;
            if (m_FollowerTravel == null) m_FollowerTravel = new FollowerTravelState();
            m_FollowerTravel.Learn(caller, game.Session.WorldTime.TurnCounter, "heard recall");
            m_FollowerTravel.Leader = m_Actor.Leader.PersonalityIdentity;
            m_FollowerTravel.Recalling = true; m_FollowerTravel.Status = "catching up after recall";
            ClearFollowerRoute(); m_FollowerCallRecall = true;
            m_FollowerCallLeader = caller.PersonalityIdentity;
            m_FollowerCallExpires = game.Session.WorldTime.TurnCounter + 5;
        }

        internal void HearFollowerCall(RogueGame game, Actor leader)
        {
            string reason;
            if (leader == null || !leader.IsPlayer || m_Actor.Leader != leader || m_Actor.IsDead || m_Actor.IsSleeping ||
                m_Actor.Location.Map != leader.Location.Map || game.Rules.IsKnownEnemy(m_Actor, leader) || !game.Rules.CanActorShout(m_Actor, out reason) ||
                game.Rules.StdDistance(m_Actor.Location.Position, leader.Location.Position) > m_Actor.AudioRange) return;
            m_FollowerCallLeader = leader.PersonalityIdentity;
            m_FollowerCallRecall = false;
            m_FollowerCallExpires = game.Session.WorldTime.TurnCounter + 5;
        }

        internal bool CanReplyFollowerCall(RogueGame game)
        {
            Actor leader = m_FollowerCallRecall ? game.Player : m_Actor.Leader; string reason;
            return m_FollowerCallLeader != Guid.Empty && leader != null && leader.IsPlayer && !leader.IsDead && !leader.IsSleeping &&
                leader.PersonalityIdentity == m_FollowerCallLeader && game.Session.WorldTime.TurnCounter <= m_FollowerCallExpires &&
                !m_Actor.IsDead && !m_Actor.IsSleeping && m_Actor.Location.Map == leader.Location.Map &&
                (!m_FollowerCallRecall || Travelling(m_Actor) && game.Rules.IsActorTrustingLeader(m_Actor)) &&
                !game.Rules.IsKnownEnemy(m_Actor, leader) && game.Rules.CanActorShout(m_Actor, out reason) &&
                game.Rules.StdDistance(m_Actor.Location.Position, leader.Location.Position) <= leader.AudioRange &&
                game.Rules.StdDistance(m_Actor.Location.Position, leader.Location.Position) <= m_Actor.AudioRange;
        }

        ActorAction SelectFollowerReply(RogueGame game, List<Percept> percepts)
        {
            if (m_FollowerCallLeader == Guid.Empty) return null;
            if (!CanReplyFollowerCall(game))
            {
                Actor caller = m_FollowerCallRecall ? game.Player : m_Actor.Leader;
                if (caller == null || caller.PersonalityIdentity != m_FollowerCallLeader ||
                    game.Session.WorldTime.TurnCounter > m_FollowerCallExpires ||
                    m_Actor.Location.Map != caller.Location.Map) m_FollowerCallLeader = Guid.Empty;
                return null;
            }
            if (game.Rules.IsActorStarving(m_Actor) || game.Rules.IsActorTired(m_Actor) || game.Rules.IsActorSleepy(m_Actor) ||
                (game.Rules.IsActorHungry(m_Actor) && NpcFoodSupply.HasFood(game, m_Actor)) ||
                m_Actor.HitPoints < game.Rules.ActorMaxHPs(m_Actor) / 2 ||
                game.Rules.ActorInfectionPercent(m_Actor) >= game.Session.GamePreset.InfectionWeakThreshold) return null;
            // A brief reply pauses routine work for one action and retains the underlying order.
            if (percepts != null) foreach (Percept percept in percepts)
            {
                if (percept.Location.Map != m_Actor.Location.Map || percept.Turn != m_Actor.Location.Map.LocalTime.TurnCounter) continue;
                Actor actor = percept.Percepted as Actor;
                if (actor != null && game.Rules.IsKnownEnemy(m_Actor, actor)) return null;
                Inventory items = percept.Percepted as Inventory;
                if (items != null) foreach (Item item in items.Items) if (item is ItemGrenadePrimed) return null;
            }
            return new ActionFollowerReply(m_Actor, game);
        }

        internal void ReplyFollowerCall(RogueGame game)
        {
            if (!CanReplyFollowerCall(game)) return;
            m_FollowerCallLeader = Guid.Empty;
            Actor caller = m_FollowerCallRecall ? game.Player : m_Actor.Leader;
            string reply = m_FollowerCallRecall ? "Coming back!" : "Here!";
            m_FollowerCallRecall = false;
            game.DoSay(m_Actor, caller, reply, RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_SHOUT |
                RogueGame.Sayflags.IS_BRIEF | RogueGame.Sayflags.IS_FOLLOWER_CALL);
        }
    }
}
