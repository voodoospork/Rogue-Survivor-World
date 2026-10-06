using System;
using System.Collections.Generic;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class ScavengingSystem
    {
        public static int Count(RogueGame game, Inventory items, string resource, int turn)
        {
            int count = 0;
            if (items != null) foreach (var item in items.Items)
                if (resource == "food" ? item is ItemFood && !game.Rules.IsFoodSpoiled((ItemFood)item, turn) :
                    item is ItemMedicine && ((ItemMedicine)item).Healing > 0) count += item.Quantity;
            return count;
        }
        public static string PlaceLabel(CommunityShelterLocation place)
        {
            return place.MapName + (place.District.HasValue ? " / district " + World.CoordToString(place.District.Value.X, place.District.Value.Y) : "") +
                " / (" + place.Anchor.X + "," + place.Anchor.Y + ")";
        }
        public static void ObserveNpc(NpcPerceptionContext c, string resource)
        {
            var knowledge = c.Owner.Personality.Knowledge;
            int units = Count(c.Game, c.Items, resource, c.Turn);
            var old = knowledge.Places.Find(p => p.Kind == resource && p.Place == c.Place);
            var fact = knowledge.Facts.LastOrDefault(f => f.Kind == resource + "_cache" && f.Place == c.Place);
            bool claimed = c.Place.Map.XpdBaseAt(c.Place.Position) != null;
            if ((units > 0 || old != null) && (old == null || old.Units != units || old.Risk != c.Risk || fact == null || fact.SupplyLocation == null || fact.SupplyClaimed != claimed || c.Turn - fact.EventTurn > 180))
                knowledge.Learn(new NpcFact { Kind = resource + "_cache", EventId = Session.Get.NextPersonalityEventId(), EventTurn = c.Turn,
                    LearnedTurn = c.Turn, Confidence = 90, Source = NpcKnowledgeSource.Witness, SourceId = c.Owner.PersonalityIdentity,
                    Place = c.Place, SupplyLocation = CommunityShelterLocation.At(c.Place), SupplyClaimed = claimed, Units = units, Risk = c.Risk });
            if (old != null || units > 0) knowledge.RememberPlace(new NpcKnownPlace(c.Place, resource, c.Turn, units, c.Risk));
        }
        public static void ObserveEmptyNpc(RogueGame game, Actor actor)
        {
            // At most sixteen remembered spots; disappearance produces a local observation, never a world scan.
            foreach (var known in actor.Personality.Knowledge.Places.ToArray())
                if ((known.Kind == "food" || known.Kind == "medicine") && known.Units > 0 &&
                    NpcKnowledgeSystem.Visible(game, actor, known.Place) && known.Place.Map.GetItemsAt(known.Place.Position) == null)
                {
                    var claim = known.Place.Map.XpdBaseAt(known.Place.Position);
                    ObserveNpc(new NpcPerceptionContext(game, actor, known.Place, risk: claim != null && !claim.Owns(actor) ? 1 : 0), known.Kind);
                }
        }
        public static void Hear(RogueGame game, Actor listener, Actor speaker, NpcFact fact)
        {
            if (!listener.IsPlayer || (fact.Kind != "food_cache" && fact.Kind != "medicine_cache") || fact.Confidence < 40) return;
            var place = fact.SupplyLocation;
            if (place == null) return; // Old facts gain geometry through a new observation, not through hidden state.
            listener.Personality.Scavenging.Record(fact.Kind == "food_cache" ? "food" : "medicine", "Reported stock spot", false, place,
                new SupplyObservation { EventId = fact.EventId, ObservedTurn = fact.EventTurn, LearnedTurn = fact.LearnedTurn,
                    Confidence = fact.Confidence, Units = fact.Units, Confirmed = true, Claimed = fact.SupplyClaimed || fact.Risk > 0,
                    Source = game.IdentifiesDiplomacySpeaker(listener, speaker) ? "reported by " + speaker.UnmodifiedName : "reported by an unseen voice" });
        }
        public static List<NpcFact> Reports(Actor speaker, Actor listener)
        {
            if (speaker.Personality == null) return new List<NpcFact>();
            int turn = speaker.Location.Map.LocalTime.TurnCounter;
            var knowledge = speaker.Personality.Knowledge;
            var newest = knowledge.Facts.Where(f => (f.Kind == "food_cache" || f.Kind == "medicine_cache") && (f.SupplyLocation != null || f.Place.Map != null))
                .Select(ReportSnapshot)
                .GroupBy(f => f.Kind + ":" + f.SupplyLocation.MapSeed + ":" + f.SupplyLocation.MapName + ":" + f.SupplyLocation.District + ":" + f.SupplyLocation.Anchor)
                .Select(g => g.OrderByDescending(f => f.EventTurn).ThenByDescending(f => f.Confidence).ThenByDescending(f => f.EventId).First());
            return newest.Where(f => f.Confidence >= 40 && f.Hops < 3 && turn - f.EventTurn <= 2 * WorldTime.TURNS_PER_DAY &&
                !knowledge.WasTold(f.EventId, listener.PersonalityIdentity) && (listener.Personality == null ||
                !listener.Personality.Knowledge.Facts.Exists(k => k.Kind == f.Kind && k.EventId == f.EventId)))
                .OrderByDescending(f => f.EventTurn).ThenBy(f => f.EventId).Take(3).ToList();
        }
        public static NpcFact ReportSnapshot(NpcFact fact)
        {
            if (fact.SupplyLocation != null || fact.Place.Map == null) return fact;
            // Older saves already retain this remembered location. Copy its identity/point only;
            // never consult current stock, claims, or building geometry to upgrade a report.
            var copy = fact.SpeechSnapshot(); var map = fact.Place.Map;
            copy.SupplyLocation = new CommunityShelterLocation { MapSeed = map.Seed, MapName = map.Name,
                District = map.District == null ? (System.Drawing.Point?)null : map.District.WorldPosition, Anchor = fact.Place.Position };
            return copy;
        }
        public static void Share(RogueGame game, Actor speaker, Actor listener, NpcFact fact)
        {
            game.DoSay(speaker, listener, (fact.Source == NpcKnowledgeSource.Told ? "I heard of " : "I saw ") +
                fact.Units + " usable " + (fact.Kind == "food_cache" ? "food" : "healing medicine") + " at " + PlaceLabel(fact.SupplyLocation) +
                ", turn " + fact.EventTurn + ", confidence " + fact.Confidence + "%" +
                (fact.Units == 0 ? ". That spot was empty; the rest of the building is unconfirmed" : "") +
                (fact.SupplyClaimed || fact.Risk > 0 ? ". Claimed supplies: taking them may provoke the owners" : "") + ".",
                RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_RUMOR | RogueGame.Sayflags.IS_FREE_ACTION, fact.EventId, fact.StoryId);
            foreach (var hearer in speaker.Location.Map.Actors)
                if (hearer != speaker && !hearer.IsDead && !hearer.IsSleeping && hearer.Model.Abilities.IsIntelligent &&
                    !hearer.Model.Abilities.IsUndead && game.Rules.StdDistance(hearer.Location.Position, speaker.Location.Position) <= hearer.AudioRange)
                {
                    if (hearer.Personality == null) hearer.Personality = new PersonalityState();
                    NpcKnowledgeSystem.Hear(game, hearer, speaker, fact.SpeechSnapshot());
                }
            speaker.Personality.Knowledge.Told(fact.EventId, listener.PersonalityIdentity, speaker.Location.Map.LocalTime.TurnCounter);
            speaker.Personality.Knowledge.NextTalkTurn = speaker.Location.Map.LocalTime.TurnCounter + 30;
        }
    }
}
