using System;
using System.Collections.Generic;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    // Evaluation-local values only: no cache survives actions, reports or another observer.
    sealed class NpcDecisionContext : IDisposable
    {
        [ThreadStatic] static NpcDecisionContext current;
        readonly NpcDecisionContext previous;
        readonly Actor owner;
        Dictionary<Guid, NpcMotivation> motivations;
        NpcContentCatalog catalog;
        Dictionary<Point, bool> sight;
        Map sightMap;
        Point sightOrigin;
        int sightRange;
        long sightGeometry;
        Dictionary<Actor, bool> enemies;
        bool relationsReady;
        long relationOccupancy, relationGeometry;
        internal static void RelationsReady()
        {
            if (current == null) return;
            current.relationsReady = true;
            current.relationOccupancy = current.owner.Location.Map.OccupancyRevision;
            current.relationGeometry = current.owner.Location.Map.GeometryRevision;
        }
        internal static void InvalidateRelations()
        { for (var context = current; context != null; context = context.previous) { context.enemies?.Clear(); context.motivations?.Clear(); } }
        internal static bool TryEnemy(Actor observer, Actor target, out bool result)
        {
            result = false; var context = current;
            if (context == null || !context.relationsReady || context.owner != observer || target == null) return false;
            var map = observer.Location.Map;
            if (context.relationOccupancy != map.OccupancyRevision || context.relationGeometry != map.GeometryRevision) {
                context.enemies?.Clear(); context.relationOccupancy = map.OccupancyRevision; context.relationGeometry = map.GeometryRevision;
            }
            return context.enemies != null && context.enemies.TryGetValue(target, out result);
        }
        internal static bool RememberEnemy(Actor observer, Actor target, bool result)
        {
            var context = current;
            if (context != null && context.relationsReady && context.owner == observer && target != null) {
                if (context.enemies == null) context.enemies = new Dictionary<Actor, bool>();
                context.enemies[target] = result;
            }
            return result;
        }
        NpcDecisionContext(Actor actor) { owner = actor; previous = current; current = this; }
        internal static NpcDecisionContext Evaluation(RogueGame game, Actor actor) { return new NpcDecisionContext(actor); }
        internal static bool Visible(RogueGame game, Actor actor, Location place)
        {
            if (place.Map != actor.Location.Map) return false;
            int range = game.Rules.ActorFOV(actor, actor.Location.Map.LocalTime, game.Session.World.Weather);
            if (game.Rules.GridDistance(actor.Location.Position, place.Position) > range) return false;
            var context = current;
            if (context == null || context.owner != actor) return LOS.CanTraceViewLine(actor.Location, place.Position);
            Map map = actor.Location.Map;
            if (context.sightMap != map || context.sightOrigin != actor.Location.Position ||
                context.sightRange != range || context.sightGeometry != map.GeometryRevision)
            {
                if (context.sight == null) context.sight = new Dictionary<Point, bool>(djack.RogueSurvivor.Data.PointComparer.Instance); else context.sight.Clear(); context.sightMap = map; context.sightOrigin = actor.Location.Position;
                context.sightRange = range; context.sightGeometry = map.GeometryRevision;
            }
            bool result;
            if (!context.sight.TryGetValue(place.Position, out result))
                context.sight.Add(place.Position, result = LOS.CanTraceViewLine(actor.Location, place.Position));
            return result;
        }
        internal static NpcMotivation Motivation(NpcContentCatalog content, Actor actor, Guid subject)
        {
            var context = current;
            if (context == null || context.owner != actor) return new NpcMotivation(content, actor, subject);
            if (context.motivations == null) context.motivations = new Dictionary<Guid, NpcMotivation>();
            if (context.catalog != content) { context.catalog = content; context.motivations.Clear(); }
            NpcMotivation result;
            if (!context.motivations.TryGetValue(subject, out result))
                context.motivations.Add(subject, result = new NpcMotivation(content, actor, subject));
            return result;
        }
        public void Dispose() { current = previous; }
    }
}
