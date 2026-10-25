using System;
using System.Linq;
using System.Collections.Generic;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class RobberySystem
    {
        internal const int ResponseTurns = 8;
        internal static int Now(RogueGame game) { return game.Session.WorldTime.TurnCounter; }
        internal static bool Human(Actor a)
        { return a != null && !a.IsDead && !a.IsSleeping && a.Model.Abilities.IsIntelligent && !a.Model.Abilities.IsUndead; }
        internal static bool Near(RogueGame game, Actor a, Actor b)
        { return Human(a) && Human(b) && a != b && a.Location.Map == b.Location.Map &&
            game.Rules.GridDistance(a.Location.Position, b.Location.Position) <= 1 &&
            game.Rules.StdDistance(a.Location.Position, b.Location.Position) <= b.AudioRange &&
            game.Rules.StdDistance(a.Location.Position, b.Location.Position) <= a.AudioRange &&
            NpcKnowledgeSystem.Visible(game, a, b.Location) && NpcKnowledgeSystem.Visible(game, b, a.Location); }
        internal static bool ReadyWeapon(Actor a)
        { var w = a.GetEquippedWeapon(); return w != null && !w.IsEntrustedCargo &&
            (w is ItemMeleeWeapon || w is ItemGrenade || w is ItemRangedWeapon && ((ItemRangedWeapon)w).Ammo > 0); }
        // Opponent ammunition, skills and backpack contents are never inspected.
        internal static int VisibleStrength(RogueGame game, Actor a)
        {
            var weapon = a.GetEquippedWeapon();
            int gear = weapon is ItemRangedWeapon ? 14 : weapon is ItemMeleeWeapon ? 6 : weapon is ItemGrenade ? 8 : 0;
            bool armor = a.Inventory != null && a.Inventory.Items.Any(i => i.IsEquipped && i is ItemBodyArmor);
            return (a.HitPoints * 2 < game.Rules.ActorMaxHPs(a) ? 2 : 5) + gear + (armor ? 3 : 0);
        }
        internal static int Desperation(RogueGame game, Actor a)
        { return (CommunityAggressionSystem.Food(game, a) <= 1 ? 15 : 0) +
            (game.Rules.IsActorHungry(a) ? 15 : 0) + (game.Rules.IsActorStarving(a) ? 20 : 0) +
            (a.HitPoints < game.Rules.ActorMaxHPs(a) && CommunityAggressionSystem.Medicine(a) == 0 ? 15 : 0); }
        internal static int Opposition(RogueGame game, Actor observer, Actor target)
        {
            int power = VisibleStrength(game, target);
            foreach (var seen in observer.Location.Map.Actors)
                if (seen != observer && seen != target && Human(seen) && !seen.IsSleeping &&
                    seen.GroupRoot != observer.GroupRoot && (observer.SocialGroup == null || seen.SocialGroup != observer.SocialGroup) &&
                    game.Rules.GridDistance(seen.Location.Position, target.Location.Position) <= 3 &&
                    NpcKnowledgeSystem.Visible(game, observer, seen.Location)) power += VisibleStrength(game, seen);
            return power;
        }
        internal static bool HasState(Actor a) { return a.Robbing != null || a.BeingRobbed != null; }
        public static bool CanDemand(RogueGame game, Actor a, Actor b, RobberyGoods goods)
        {
            return game.Session.GamePreset.NpcPersonalitiesEnabled && Near(game, a, b) &&
                a.Inventory != null && b.Inventory != null && !HasState(a) && !HasState(b) &&
                a.GroupRoot != b.GroupRoot && (a.SocialGroup == null || a.SocialGroup != b.SocialGroup) &&
                !game.Rules.IsKnownEnemy(a, b) && !game.Rules.IsKnownEnemy(b, a) &&
                Now(game) >= a.NextRobberyWorldTurn && a.Predation == null && a.CommunityRaid == null &&
                (ReadyWeapon(a) || b.GetEquippedWeapon() == null) &&
                (goods != RobberyGoods.Weapon || b.GetEquippedWeapon() != null && !b.GetEquippedWeapon().IsEntrustedCargo);
        }
        internal static string Goods(RobberyDemand d)
        { return d.Goods == RobberyGoods.Weapon ? "your equipped weapon" : d.Units + (d.Goods == RobberyGoods.Food ? " food unit(s)" : " healing medicine unit(s)"); }
        public static void Demand(RogueGame game, Actor a, Actor b, RobberyGoods goods)
        {
            if (!CanDemand(game, a, b, goods)) return;
            var d = new RobberyDemand { Id = SimulationClock.NewIdentity(), Robber = a.PersonalityIdentity,
                Victim = b.PersonalityIdentity, Goods = goods, Units = goods == RobberyGoods.Food ? 2 : 1,
                Pressure = Desperation(game, a), Expires = Now(game) + ResponseTurns, RobberHP = a.HitPoints, VictimHP = b.HitPoints };
            a.Robbing = d; b.BeingRobbed = d.Copy(); a.NextRobberyWorldTurn = Now(game) + WorldTime.TURNS_PER_DAY / 2;
            // Threats give the victim ordinary self-defense without inflicting a free hit.
            game.DoMakeAggression(a, b);
            game.InterruptForRobbery(b);
            game.DoSay(a, b, (d.Pressure >= 40 ? "I'm desperate! " : "") + "Hand over " + Goods(d) + ". Refuse and I may attack!" +
                (b.IsPlayer ? " V: answer; you can also fight or flee." : ""), RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_DANGER);
            PersonalitySystem.Report(game, new SignificantEvent("robbery_threat", a, b, a.Location.Map, a.Location.Position, Now(game)));
            if (a.SocialGroup != null) CommunityRaidSystem.Announce(game, a, "community_robbery", "We demanded " + Goods(d) + " from " + b.UnmodifiedName + ".");
            game.SpendActorActionPoints(a, Rules.BASE_ACTION_COST);
        }
        internal static Actor Robber(RogueGame game, Actor victim)
        {
            var d = victim.BeingRobbed; if (d == null) return null;
            var robber = victim.Location.Map.Actors.FirstOrDefault(a => a.PersonalityIdentity == d.Robber);
            return robber != null && robber.Robbing != null && robber.Robbing.Id == d.Id && Near(game, robber, victim) &&
                robber.HitPoints >= d.RobberHP && victim.HitPoints >= d.VictimHP && !victim.IsAggressorOf(robber) &&
                Now(game) < d.Expires ? robber : null;
        }
        internal static bool Usable(RogueGame game, Actor owner, Item item, RobberyGoods goods)
        { return item != null && item.Quantity > 0 && !item.IsEntrustedCargo && (goods == RobberyGoods.Weapon ? item == owner.GetEquippedWeapon() :
            goods == RobberyGoods.Food ? item is ItemFood && !game.Rules.IsFoodSpoiled((ItemFood)item, owner.Location.Map.LocalTime.TurnCounter) :
            item is ItemMedicine && ((ItemMedicine)item).Healing > 0); }
        // Move existing units intact. Capacity is checked before splitting, so a refusal
        // or full inventory never destroys a stack or drops it onto somebody else's claim.
        internal static int Transfer(RogueGame game, Actor from, Actor to, RobberyGoods goods, int units)
        {
            if (!Near(game, from, to) || from.Inventory == null || to.Inventory == null) return 0;
            int received = 0;
            foreach (var item in from.Inventory.Items.Where(i => Usable(game, from, i, goods)).ToArray())
            {
                if (received >= units) break;
                received += game.MoveExtortionUnits(from, to, item, units - received);
            }
            return received;
        }
        internal static bool Surrender(RogueGame game, Actor victim, Actor robber)
        {
            var d = victim.BeingRobbed;
            if (d == null || !victim.Inventory.Items.Any(i => Usable(game, victim, i, d.Goods))) return false;
            int defense = VisibleStrength(game, victim);
            foreach (var ally in victim.Location.Map.Actors)
                if (ally != victim && ally != robber && Human(ally) && victim.SocialGroup != null &&
                    CommunityDiplomacySystem.Recognizes(victim, ally).Group == victim.SocialGroup.Identity &&
                    game.Rules.GridDistance(victim.Location.Position, ally.Location.Position) <= 3 &&
                    NpcKnowledgeSystem.Visible(game, victim, ally.Location)) defense += VisibleStrength(game, ally);
            int threat = VisibleStrength(game, robber) * 100 / Math.Max(1, defense);
            // Scarcity makes a victim protect food/medicine; courage raises resistance.
            return threat + (d.Pressure >= 40 ? 20 : 0) > 115 + Desperation(game, victim) / 2 +
                PersonalitySystem.Bias(victim, DecisionKind.Courage);
        }
        public static void Answer(RogueGame game, Actor victim, bool surrender)
        {
            Actor robber = Robber(game, victim); if (robber == null) return;
            var d = victim.BeingRobbed;
            int received = surrender ? Transfer(game, victim, robber, d.Goods, d.Units) : 0;
            game.DoSay(victim, robber, received > 0 ? "Take it. Leave me alone." : surrender ?
                "I cannot hand that over." : "No. You are getting nothing.", RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_DANGER);
            victim.BeingRobbed = null; robber.Robbing = null;
            if (received > 0)
            {
                // A successful surrender ends this personal coercion, never prior crimes,
                // faction hostility or community feuds. These were neutral before the demand.
                robber.RemoveAggressorOf(victim); victim.RemoveSelfDefenceFrom(robber);
                PersonalitySystem.Report(game, new SignificantEvent("robbery_paid", robber, victim, victim.Location.Map, victim.Location.Position, Now(game)));
            }
            else Escalate(game, robber, victim, d);
            game.SpendActorActionPoints(victim, Rules.BASE_ACTION_COST);
        }
        static void Escalate(RogueGame game, Actor robber, Actor victim, RobberyDemand demand)
        {
            if (robber.IsPlayer) return; // The player chooses their own next action.
            if (ReadyWeapon(robber) || victim.GetEquippedWeapon() == null)
            {
                if (VisibleStrength(game, robber) * (100 + Desperation(game, robber)) >= Opposition(game, robber, victim) * 100 &&
                    robber.HitPoints * 3 >= game.Rules.ActorMaxHPs(robber))
                {
                    int clock = CommunityAdaptationSystem.Turn(robber);
                    robber.Predation = new CommunityPredation { Target = victim.PersonalityIdentity, Departure = robber.Location,
                        LastSeen = victim.Location, Deadline = clock + CommunityAggressionSystem.HuntLimit,
                        InitialFood = CommunityAggressionSystem.Food(game, robber), InitialMedicine = CommunityAggressionSystem.Medicine(robber) };
                    return;
                }
            }
            // Backing down ends only the new threat; no remote feud is created by refusal.
            if (robber.HitPoints >= demand.RobberHP && victim.HitPoints >= demand.VictimHP && !victim.IsAggressorOf(robber))
            { robber.RemoveAggressorOf(victim); victim.RemoveSelfDefenceFrom(robber); }
            game.DoSay(robber, victim, "Keep it. This is too risky.", RogueGame.Sayflags.IS_FREE_ACTION);
        }
        internal static void Update(RogueGame game, Actor actor)
        {
            if (actor.BeingRobbed != null && Robber(game, actor) == null) actor.BeingRobbed = null;
            var d = actor.Robbing; if (d == null) return;
            var target = actor.Location.Map.Actors.FirstOrDefault(a => a.PersonalityIdentity == d.Victim);
            if (!Human(actor) || !Near(game, actor, target) || Now(game) >= d.Expires ||
                target.BeingRobbed == null || target.BeingRobbed.Id != d.Id ||
                actor.HitPoints < d.RobberHP || target.HitPoints < d.VictimHP || target.IsAggressorOf(actor))
            {
                actor.Robbing = null;
                if (target != null && target.BeingRobbed != null && target.BeingRobbed.Id == d.Id) target.BeingRobbed = null;
                // No unseen pursuit or remembered location is manufactured by a timeout.
                if (Human(actor) && Human(target) && NpcKnowledgeSystem.Visible(game, actor, target.Location)) Escalate(game, actor, target, d);
            }
        }
        internal static bool Autonomous(RogueGame game, Actor a, Actor b)
        {
            var p = CommunityCultureSystem.KnownPolicy(a);
            return game.Session.GamePreset.NpcPersonalitiesEnabled && game.Session.GamePreset.Bases && p != null && p.AggressionKnown && p.Aggression == CommunityAggression.Raiders &&
                Human(a) && Human(b) && !a.InPlayerParty && a.Controller is CivilianAI && ((CivilianAI)a.Controller).Order == null &&
                a.SocialGroup != null && a.SocialGroup.Home != null && a.ResidentCommunity.TravelHome == null &&
                a.Predation == null && a.CommunityRaid == null && !HasState(a) && !HasState(b) && a.GroupRoot != b.GroupRoot &&
                a.SocialGroup != b.SocialGroup && !CommunityTributeSystem.Truce(a, b) && Now(game) >= a.NextRobberyWorldTurn &&
                !game.Rules.IsKnownEnemy(a, b) && !game.Rules.IsKnownEnemy(b, a) &&
                a.Location.Map == b.Location.Map && NpcKnowledgeSystem.Visible(game, a, b.Location) &&
                !game.Rules.IsActorSleepy(a) && !game.Rules.IsActorTired(a) && a.HitPoints * 3 >= game.Rules.ActorMaxHPs(a) &&
                !(game.Rules.IsActorHungry(a) && CommunityAggressionSystem.Food(game, a) > 0) &&
                Desperation(game, a) >= 15 && (ReadyWeapon(a) || b.GetEquippedWeapon() == null) &&
                VisibleStrength(game, a) * (100 + Desperation(game, a)) >= Opposition(game, a, b) * 125;
        }
        internal static RobberyGoods Need(RogueGame game, Actor a)
        { return a.HitPoints < game.Rules.ActorMaxHPs(a) && CommunityAggressionSystem.Medicine(a) == 0 &&
            CommunityAggressionSystem.Food(game, a) > 0 ? RobberyGoods.Medicine : RobberyGoods.Food; }
    }
}
