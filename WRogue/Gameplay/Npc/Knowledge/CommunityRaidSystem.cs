using System;
using System.Collections.Generic;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class CommunityRaidSystem
    {
        public const int MarchLimit = 180, SearchLimit = 20, ReturnLimit = 240;
        static bool Enabled(RogueGame game) { return game.Session.GamePreset.NpcPersonalitiesEnabled && game.Session.GamePreset.Bases; }
        static bool Free(Actor actor)
        { return actor != null && !actor.IsDead && !actor.IsSleeping && !actor.InPlayerParty &&
            actor.Controller is CivilianAI && ((CivilianAI)actor.Controller).Order == null && actor.Personality != null; }
        internal static bool Food(RogueGame game, Actor actor)
        { return actor.Inventory != null && actor.Inventory.Items.Any(i => !i.IsEntrustedCargo && i.Quantity > 0 &&
            i is ItemFood && !game.Rules.IsFoodSpoiled((ItemFood)i, actor.Location.Map.LocalTime.TurnCounter)); }
        static bool Ready(RogueGame game, Actor actor)
        { return Free(actor) && actor.CommunityRaid == null && actor.Predation == null && !game.Rules.IsActorHungry(actor) &&
            !game.Rules.IsActorTired(actor) && !game.Rules.IsActorSleepy(actor) &&
            actor.HitPoints >= 3 * game.Rules.ActorMaxHPs(actor) / 4 && Food(game, actor) &&
            actor.Inventory.Items.Any(i => !i.IsEntrustedCargo && (i is ItemMeleeWeapon || i is ItemRangedWeapon && ((ItemRangedWeapon)i).Ammo > 0)); }
        static bool At(Actor actor, CommunityShelterLocation place)
        { return place != null && (place.Matches(actor.Location.Map, actor.Location.Position) ||
            !place.BuildingBounds.HasValue && place.Matches(actor.Location.Map, place.Anchor) && Math.Max(Math.Abs(actor.Location.Position.X - place.Anchor.X),
                Math.Abs(actor.Location.Position.Y - place.Anchor.Y)) <= 2); }
        static bool NearObjective(RogueGame game, Actor actor, CommunityShelterLocation place)
        { return place.Matches(actor.Location.Map, place.Anchor) && game.Rules.GridDistance(actor.Location.Position, place.Anchor) <= 2; }
        internal static bool HasRoute(Actor actor, Map destination)
        {
            if (destination == null) return false;
            if (destination == actor.Location.Map) return true;
            var seen = new HashSet<Map> { actor.Location.Map }; var pending = new Queue<Map>(); pending.Enqueue(actor.Location.Map);
            while (pending.Count > 0 && seen.Count <= 16)
            {
                Map current = pending.Dequeue();
                foreach (var exit in actor.Personality.Knowledge.Exits)
                    if (exit.From.Map == current && exit.To.Map != null && seen.Add(exit.To.Map))
                    { if (exit.To.Map == destination) return true; pending.Enqueue(exit.To.Map); }
            }
            return false;
        }
        static bool Briefing(RogueGame game, Actor chief, out KnownCommunity target, out List<Actor> party)
        {
            target = null; party = new List<Actor>();
            SocialGroup group = chief.SocialGroup;
            if (!Enabled(game) || !CommunityDiplomacySystem.IsLeader(chief) || !Ready(game, chief) ||
                group.Home == null || group.Diplomacy == null || !At(chief, group.Home.Place) ||
                game.Session.WorldTime.TurnCounter < group.NextRaidWorldTurn || chief.ResidentCommunity.TravelHome != null) return false;
            // Count only residents actually seen at home; an absent name is not a spare defender.
            var residents = chief.Location.Map.Actors.Where(a => !a.IsDead && !a.InPlayerParty && a.SocialGroup == group &&
                At(a, group.Home.Place) && NpcKnowledgeSystem.Visible(game, chief, a.Location)).ToList();
            if (!residents.Contains(chief)) residents.Add(chief);
            if (residents.Count < 3 || residents.Any(a => a != chief && game.Rules.IsActorStarving(a))) return false;
            foreach (Actor other in chief.Location.Map.Actors)
                if (!other.IsDead && NpcKnowledgeSystem.Visible(game, chief, other.Location) && game.Rules.IsKnownEnemy(chief, other)) return false;
            int now = game.Session.WorldTime.TurnCounter;
            foreach (var relation in group.Diplomacy.Relations.OrderByDescending(r => r.Grievance))
            {
                if (!relation.Feud || relation.Target.Group == Guid.Empty || relation.Target.Group == group.Identity) continue;
                var known = chief.Personality.FindCommunity(relation.Target.Group);
                var declaration = chief.Personality.FindDiplomacy(group.Identity, relation.Target);
                if (known == null || known.ShelterLocation == null || declaration == null || !declaration.Message.Feud || declaration.Confidence < 60 ||
                    now < known.ShelterTurn || now - known.ShelterTurn > WorldTime.TURNS_PER_DAY) continue;
                var place = game.ResolveCommunityPlace(chief, known.ShelterLocation);
                if (!place.HasValue || !HasRoute(chief, place.Value.Map) || At(chief, known.ShelterLocation)) continue;
                target = known; break;
            }
            if (target == null) return false;
            party.Add(chief);
            // The chief cannot borrow command authority from membership. Willing, nearby residents opt in.
            foreach (var actor in residents.OrderBy(a => a.PersonalityIdentity))
            {
                if (actor == chief || party.Count >= Math.Min(4, residents.Count - 1) || !Ready(game, actor) ||
                    game.Rules.GridDistance(actor.Location.Position, chief.Location.Position) > 3 ||
                    game.Rules.StdDistance(actor.Location.Position, chief.Location.Position) > actor.AudioRange ||
                    PersonalitySystem.Attitude(actor, chief) < -10 ||
                    PersonalitySystem.Bias(actor, DecisionKind.Courage) + PersonalitySystem.Bias(actor, DecisionKind.Group) < -10 ||
                    actor.ResidentCommunity.TravelHome != null || actor.HasLeader && actor.Leader != chief) continue;
                party.Add(actor);
            }
            return party.Count >= 2;
        }
        public static ActorAction Select(RogueGame game, Actor chief)
        {
            if (!Enabled(game) || chief.CommunityRaid != null || !CommunityDiplomacySystem.IsLeader(chief) || chief.SocialGroup.Home == null) return null;
            int now = game.Session.WorldTime.TurnCounter;
            if (now < chief.SocialGroup.RaidReviewWorldTurn || now < chief.SocialGroup.NextRaidWorldTurn) return null;
            chief.SocialGroup.RaidReviewWorldTurn = now + 30;
            KnownCommunity target; List<Actor> party;
            return Briefing(game, chief, out target, out party) ? new ActionCommunityRaid(chief, game) : null;
        }
        public static bool CanAssemble(RogueGame game, Actor chief)
        { KnownCommunity target; List<Actor> party; return Briefing(game, chief, out target, out party); }
        public static void Assemble(RogueGame game, Actor chief)
        {
            KnownCommunity target; List<Actor> party;
            if (!Briefing(game, chief, out target, out party)) return;
            Guid id = djack.RogueSurvivor.Engine.SimulationClock.NewIdentity(); var group = chief.SocialGroup;
            var declaration = chief.Personality.FindDiplomacy(group.Identity, new PoliticalIdentity { Group = target.Identity });
            foreach (var actor in party)
            {
                int clock = CommunityAdaptationSystem.Turn(actor);
                var duty = new CommunityRaidDuty { Id = id, Community = group.Identity, Commander = chief.PersonalityIdentity,
                    Target = target.Identity, TargetName = target.Name, ShelterSource = target.ShelterSource, ShelterTurn = target.ShelterTurn,
                    Objective = target.ShelterLocation.Copy(), Home = group.Home.Place.Copy(), Deadline = clock + MarchLimit, LastContact = clock };
                duty.Party.AddRange(party.Select(a => a.PersonalityIdentity)); actor.CommunityRaid = duty;
                if (actor == chief) continue;
                actor.Personality.LearnDiplomacy(declaration.Message, game.Session.WorldTime.TurnCounter, "raid briefing by " + chief.UnmodifiedName, 100);
                var received = actor.Personality.LearnCommunity(target.Identity, target.Name, game.Session.WorldTime.TurnCounter, "raid briefing");
                foreach (var member in target.Members) { var copy = member.Copy(); copy.Source = "raid briefing by " + chief.UnmodifiedName; received.Member(copy); }
                // A briefing transmits only the chief's remembered route, not the world's exit graph.
                foreach (var exit in chief.Personality.Knowledge.Exits)
                    if (!actor.Personality.Knowledge.Exits.Any(e => e.From == exit.From))
                    { actor.Personality.Knowledge.Exits.Add(new NpcKnownExit(exit.From, exit.To));
                        if (actor.Personality.Knowledge.Exits.Count > 32) actor.Personality.Knowledge.Exits.RemoveAt(0); }
            }
            group.NextRaidWorldTurn = game.Session.WorldTime.TurnCounter + WorldTime.TURNS_PER_DAY;
            Announce(game, chief, "community_raid_assembled", "We are raiding " + target.Name + "'s reported shelter (turn " + target.ShelterTurn +
                ", " + target.ShelterSource + "). " + party.Count + " volunteers; return if separated or in trouble.");
        }
        internal static void Announce(RogueGame game, Actor speaker, string kind, string text)
        {
            var group = speaker.SocialGroup; if (group == null) return;
            int now = game.Session.WorldTime.TurnCounter;
            var note = new CommunityNote { EventId = game.Session.NextPersonalityEventId(), Kind = kind, Text = text,
                EventTurn = now, LearnedTurn = now, Source = "participant" };
            if (group.Tribe != null) group.Tribe.Note(note.Copy());
            foreach (var listener in speaker.Location.Map.Actors)
            {
                if (listener.IsDead || listener.IsSleeping || listener.Personality == null || listener.Model.Abilities.IsUndead ||
                    game.Rules.StdDistance(speaker.Location.Position, listener.Location.Position) > listener.AudioRange ||
                    listener != speaker && !NpcKnowledgeSystem.Visible(game, listener, speaker.Location)) continue;
                var known = listener.Personality.FindCommunity(group.Identity);
                if (known == null && listener.SocialGroup == group) known = listener.Personality.LearnCommunity(group.Identity, group.CommunityName, now, "own community");
                if (known == null) continue;
                var copy = note.Copy(); copy.Source = listener == speaker ? "participant" : "heard " + speaker.UnmodifiedName; known.Note(copy);
            }
            game.DoSay(speaker, speaker, text, RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_BRIEF);
        }
        internal static void Return(RogueGame game, Actor actor, string reason)
        {
            var duty = actor.CommunityRaid; if (duty == null || duty.Phase == CommunityRaidPhase.Returning) return;
            duty.Phase = CommunityRaidPhase.Returning; duty.Reason = reason; duty.Blocked = 0;
            duty.Deadline = CommunityAdaptationSystem.Turn(actor) + ReturnLimit;
            Announce(game, actor, "community_raid_returning", "Returning from the raid on " + duty.TargetName + ": " + reason + ".");
        }
        static bool KnownDead(Actor observer, Guid id)
        {
            var person = observer.Personality.Knowledge.Person(id);
            // Death is reported before removal. A later event in that same killing can refresh
            // the person sighting while the dying actor is still present; retain the death evidence.
            return person != null && person.Dead || observer.Personality.Knowledge.Facts.Any(f =>
                f.Kind == "death" && f.SubjectId == id && f.Confidence >= 60);
        }
        public static void Update(RogueGame game, Actor actor, IList<Actor> visible)
        {
            var duty = actor.CommunityRaid; if (duty == null || visible == null) return;
            if (!Enabled(game) || !Free(actor) || actor.SocialGroup == null || actor.SocialGroup.Identity != duty.Community)
            { actor.CommunityRaid = null; return; }
            int now = CommunityAdaptationSystem.Turn(actor);
            foreach (Actor seen in visible)
            {
                if (seen != actor && duty.Party.Contains(seen.PersonalityIdentity) && !seen.IsDead)
                {
                    duty.LastContact = now;
                    if (seen.CommunityRaid != null && seen.CommunityRaid.Id == duty.Id && seen.CommunityRaid.Phase == CommunityRaidPhase.Returning &&
                        game.Rules.StdDistance(seen.Location.Position, actor.Location.Position) <= actor.AudioRange)
                        Return(game, actor, "a nearby raider called a retreat");
                }
                if (CommunityDiplomacySystem.Recognizes(actor, seen).Group == duty.Target && game.Rules.IsKnownEnemy(actor, seen)) duty.SawDefenders = true;
            }
            var declaration = actor.Personality.FindDiplomacy(duty.Community, new PoliticalIdentity { Group = duty.Target });
            if (declaration != null && !declaration.Message.Feud) Return(game, actor, "learned that our feud ended");
            int losses = 0;
            foreach (var id in duty.Party)
            {
                if (KnownDead(actor, id)) { losses++; if (id == duty.Commander) Return(game, actor, "commander reported dead"); }
            }
            if (losses >= Math.Max(1, duty.Party.Count / 2)) Return(game, actor, "reported losses");
            if (actor.HitPoints < 3 * game.Rules.ActorMaxHPs(actor) / 5 || game.Rules.IsActorSleepy(actor) ||
                game.Rules.IsActorStarving(actor) || !Food(game, actor)) Return(game, actor, "health, rest or food needed");
            if (duty.Phase != CommunityRaidPhase.Returning && now - duty.LastContact >= 20) Return(game, actor, "lost contact with the party");
            if (duty.Phase != CommunityRaidPhase.Returning && now >= duty.Deadline) Return(game, actor, "sortie time limit");
            if (duty.Phase == CommunityRaidPhase.Outbound && NearObjective(game, actor, duty.Objective))
            { duty.Phase = CommunityRaidPhase.Searching; duty.SearchUntil = now + SearchLimit; }
            if (duty.Phase == CommunityRaidPhase.Searching && now >= duty.SearchUntil)
                Return(game, actor, duty.SawDefenders ? "leaving the contested shelter" : "no identified defenders found at the reported shelter");
            if (duty.Phase == CommunityRaidPhase.Returning && (At(actor, duty.Home) || now >= duty.Deadline))
            {
                Announce(game, actor, At(actor, duty.Home) ? "community_raid_home" : "community_raid_stranded",
                    At(actor, duty.Home) ? "Back at the raid's departure shelter. " + duty.Reason + "." : "Raid return route unfinished; resuming local survival.");
                actor.CommunityRaid = null;
            }
        }
    }
}
