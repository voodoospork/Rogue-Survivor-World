using System.Linq;
using System.Collections.Generic;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class CommunityAggressionSystem
    {
        internal const int HuntLimit = 40, ReturnLimit = 60;
        internal static bool PredatoryPolicy(Actor a)
        { var policy = CommunityCultureSystem.KnownPolicy(a); return policy != null && policy.AggressionKnown &&
            policy.Aggression != CommunityAggression.Guarded; }
        static bool Eligible(RogueGame game, Actor a)
        { return game.Session.GamePreset.NpcPersonalitiesEnabled && game.Session.GamePreset.Bases &&
            a != null && !a.IsDead && !a.IsSleeping && !a.InPlayerParty && a.Personality != null &&
            a.Faction.ID == (int)GameFactions.IDs.TheCivilians && a.Controller is CivilianAI &&
            ((CivilianAI)a.Controller).Order == null && a.SocialGroup != null && a.SocialGroup.Home != null &&
            a.CommunityRaid == null && a.ResidentCommunity.TravelHome == null; }
        internal static int Food(RogueGame game, Actor a)
        { return a.Inventory == null ? 0 : a.Inventory.Items.Where(i => !i.IsEntrustedCargo && i is ItemFood &&
            !game.Rules.IsFoodSpoiled((ItemFood)i, a.Location.Map.LocalTime.TurnCounter)).Sum(i => i.Quantity); }
        internal static int Medicine(Actor a)
        { return a.Inventory == null ? 0 : a.Inventory.Items.Where(i => !i.IsEntrustedCargo && i is ItemMedicine &&
            ((ItemMedicine)i).Healing > 0).Sum(i => i.Quantity); }
        static bool Outsider(Actor a, Actor b)
        { return b != null && b != a && !b.IsDead && !b.Model.Abilities.IsUndead && b.Model.Abilities.IsIntelligent &&
            b.SocialGroup != a.SocialGroup && b.GroupRoot != a.GroupRoot; }
        // Only visible wounds and equipped weapons influence the estimate. Backpacks,
        // ammunition counts, remote companions and foreign membership lists are not read.
        static int Strength(RogueGame game, Actor a)
        { return (a.HitPoints * 2 < game.Rules.ActorMaxHPs(a) ? 2 : 4) +
            (a.GetEquippedWeapon() is ItemRangedWeapon ? 12 : a.GetEquippedWeapon() is ItemMeleeWeapon ? 4 : 0); }
        internal static bool AvoidUnarmedAttack(RogueGame game, Actor a, Actor b)
        {
            if (!game.Session.GamePreset.NpcPersonalitiesEnabled || a.InPlayerParty ||
                a.Faction.ID != (int)GameFactions.IDs.TheCivilians || !CommunityDiplomacySystem.PoliticalGroup(a) ||
                b == null || b.IsDead || b.Model.Abilities.IsUndead || b.Location.Map != a.Location.Map) return false;
            var own = a.GetEquippedWeapon();
            bool ready = own is ItemMeleeWeapon || own is ItemGrenade || own is ItemRangedWeapon && ((ItemRangedWeapon)own).Ammo > 0;
            if (ready || !NpcKnowledgeSystem.Visible(game, a, b.Location)) return false;
            // Their ammunition and backpack are unknown. An equipped firearm is a threat
            // even if its owner secretly has no ammunition.
            var theirs = b.GetEquippedWeapon();
            return theirs is ItemMeleeWeapon || theirs is ItemRangedWeapon || theirs is ItemGrenade;
        }
        public static bool CanAttack(RogueGame game, Actor a, Actor b)
        {
            if (!Eligible(game, a) || a.Predation != null || !Outsider(a, b) ||
                b.Location.Map != a.Location.Map || !NpcKnowledgeSystem.Visible(game, a, b.Location) ||
                game.Rules.IsKnownEnemy(a, b) || AvoidUnarmedAttack(game, a, b)) return false;
            var policy = CommunityCultureSystem.KnownPolicy(a);
            if (policy == null || !policy.AggressionKnown || policy.Aggression == CommunityAggression.Guarded) return false;
            if (policy.Aggression == CommunityAggression.AttackOutsiders) return true;
            if (CommunityAdaptationSystem.Turn(a) < a.NextPredationClock ||
                game.Rules.IsActorTired(a) || game.Rules.IsActorSleepy(a) || game.Rules.IsActorStarving(a) ||
                a.HitPoints < 3 * game.Rules.ActorMaxHPs(a) / 4 ||
                Food(game, a) > 1 && (a.HitPoints >= game.Rules.ActorMaxHPs(a) || Medicine(a) > 0)) return false;
            int opposition = 0;
            foreach (Actor seen in a.Location.Map.Actors)
                if (Outsider(a, seen) && game.Rules.GridDistance(seen.Location.Position, b.Location.Position) <= 3 &&
                    NpcKnowledgeSystem.Visible(game, a, seen.Location)) opposition += Strength(game, seen);
            return Strength(game, a) * 2 >= opposition * 3;
        }
        public static ActorAction Select(RogueGame game, Actor a, IList<Actor> visible)
        {
            if (!Eligible(game, a) || visible == null) return null;
            // Neutral policies cost no target scans. Initialization is part of ordinary culture observation.
            var policy = CommunityCultureSystem.KnownPolicy(a);
            if (policy == null || policy.Aggression == CommunityAggression.Guarded) return null;
            foreach (Actor b in visible.OrderBy(b => game.Rules.GridDistance(a.Location.Position, b.Location.Position)))
                if (CanAttack(game, a, b)) return new ActionCommunityAggression(a, game, b);
            return null;
        }
        public static void Begin(RogueGame game, Actor a, Actor b)
        {
            if (!CanAttack(game, a, b)) return;
            if (CommunityCultureSystem.KnownPolicy(a).Aggression == CommunityAggression.Raiders)
            {
                int now = CommunityAdaptationSystem.Turn(a);
                a.Predation = new CommunityPredation { Target = b.PersonalityIdentity, Departure = a.Location,
                    LastSeen = b.Location, Deadline = now + HuntLimit, InitialFood = Food(game, a), InitialMedicine = Medicine(a) };
                a.NextPredationClock = now + WorldTime.TURNS_PER_DAY / 2;
            }
            // Use ordinary aggression: the victim has self-defense, and killing a neutral
            // survivor remains murder. No group-wide feud or hidden enemy broadcast.
            bool melee = game.Rules.CanActorMeleeAttack(a, b);
            if (!melee) game.DoMakeAggression(a, b);
            game.DoSay(a, b, a.SocialGroup.CommunityName + (a.Predation == null ? ": We kill outsiders on sight!" : ": We're taking whatever supplies you carry!"),
                RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_DANGER);
            CommunityCultureSystem.HearCallRules(game, a, actualThreat: true);
            if (melee) game.DoMeleeAttack(a, b);
            else game.DoWait(a);
        }
        internal static void Return(Actor a)
        { var duty = a.Predation; if (duty == null || duty.Returning) return;
            duty.Returning = true; duty.Blocked = 0; duty.Deadline = CommunityAdaptationSystem.Turn(a) + ReturnLimit; }
        public static void Update(RogueGame game, Actor a, IList<Actor> visible)
        {
            var duty = a.Predation; if (duty == null) return;
            if (!Eligible(game, a)) { a.Predation = null; return; }
            int now = CommunityAdaptationSystem.Turn(a);
            if (visible != null) foreach (var seen in visible)
                if (seen.PersonalityIdentity == duty.Target && !seen.IsDead) duty.LastSeen = seen.Location;
            if (!duty.Returning && (now >= duty.Deadline || Food(game, a) > duty.InitialFood || Medicine(a) > duty.InitialMedicine ||
                a.HitPoints < 3 * game.Rules.ActorMaxHPs(a) / 5 || game.Rules.IsActorSleepy(a) || game.Rules.IsActorStarving(a))) Return(a);
            if (duty.Returning && (now >= duty.Deadline || duty.Departure.Map == a.Location.Map &&
                game.Rules.GridDistance(duty.Departure.Position, a.Location.Position) <= 1)) a.Predation = null;
        }
    }
}
