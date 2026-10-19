using System;
using System.Collections.Generic;
using System.Drawing;

using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.AI;

namespace djack.RogueSurvivor.Gameplay.AI.Sensors
{
    [Serializable]
    class LOSSensor : Sensor
    {
        #region Types
        [Flags]
        public enum SensingFilter
        {
            /// <summary>
            /// Actors.
            /// </summary>
            ACTORS = (1<<0),

            /// <summary>
            /// Stacks of items (Inventory).
            /// </summary>
            ITEMS = (1<<1),

            /// <summary>
            /// Stacks of corpses (List of Corpse>
            /// </summary>
            CORPSES = (1<<2)
        }
        #endregion

        #region Fields
        HashSet<Point> m_FOV;
        SensingFilter m_Filters;
        [NonSerialized] Map m_CachedMap;
        [NonSerialized] Point m_CachedPosition;
        [NonSerialized] int m_CachedRange;
        [NonSerialized] long m_CachedGeometry;
        [NonSerialized] internal int CacheHits, CacheMisses;
        #endregion

        #region Properties
        public HashSet<Point> FOV
        {
            get { return m_FOV; }
        }

        public SensingFilter Filters
        {
            get { return m_Filters; }
            set { m_Filters = value; }
        }
        #endregion

        #region Init
        public LOSSensor(SensingFilter filters)
        {
            m_Filters = filters;
        }
        #endregion

        #region Sensor
        public override List<Percept> Sense(RogueGame game, Actor actor)
        {
            long start = SimulationPerformance.Start();
            try { return SenseCore(game, actor); }
            finally { SimulationPerformance.End(SimulationPerformance.Part.Sense, start); }
        }
        List<Percept> SenseCore(RogueGame game, Actor actor)
        {
            // compute FOV
            int maxRange = game.Rules.ActorFOV(actor, actor.Location.Map.LocalTime, game.Session.World.Weather);
            Map map = actor.Location.Map;
            // Geometry only: current occupants, stock, corpses and dates are read below.
            if (m_FOV != null && m_CachedMap == map && m_CachedPosition == actor.Location.Position &&
                m_CachedRange == maxRange && m_CachedGeometry == map.GeometryRevision) { ++CacheHits;SimulationPerformance.Cache(SimulationPerformance.Reuse.FovHit); }
            else {
                m_FOV = LOS.ComputeFOVFor(game.Rules, actor, map.LocalTime, game.Session.World.Weather);
                m_CachedMap = map; m_CachedPosition = actor.Location.Position;
                m_CachedRange = maxRange; m_CachedGeometry = map.GeometryRevision; ++CacheMisses;
                SimulationPerformance.Cache(SimulationPerformance.Reuse.FovMiss);
            }

            // compute percepts.
            List<Percept> list = new List<Percept>();

            #region Actors
            if ((m_Filters & SensingFilter.ACTORS) != 0)
            {
                // roughly estimate time for two sensing methods.
                int searchFovMethodTime = maxRange * maxRange;
                int searchActorsListMethodTime = actor.Location.Map.CountActors;

                // choose method which seems less costly in time.
                if (searchFovMethodTime < searchActorsListMethodTime)
                {
                    // FOV check : bad when few actors, good when many actors.
                    foreach (Point p in m_FOV)
                    {
                        Actor other = actor.Location.Map.GetActorAt(p.X, p.Y);
                        if (other != null && other != actor)
                            list.Add(new Percept(other, actor.Location.Map.LocalTime.TurnCounter, other.Location));
                    }
                }
                else
                {
                    // Actors list check : good when few actors, bad when many actors.
                    foreach (Actor other in actor.Location.Map.Actors)
                    {
                        if (other == actor)
                            continue;

                        if (game.Rules.LOSDistance(actor.Location.Position, other.Location.Position) > maxRange)
                            continue;

                        if (m_FOV.Contains(other.Location.Position))
                            list.Add(new Percept(other, actor.Location.Map.LocalTime.TurnCounter, other.Location));
                    }
                }
            }
            #endregion

            #region Items
            if ((m_Filters & SensingFilter.ITEMS) != 0)
            {
                foreach (Point p in m_FOV)
                {
                    Inventory inv = actor.Location.Map.GetItemsAt(p);
                    if (inv == null || inv.IsEmpty)
                        continue;
                    list.Add(new Percept(inv, actor.Location.Map.LocalTime.TurnCounter, new Location(actor.Location.Map, p)));
                }
            }
            #endregion

            #region Corpses
            if ((m_Filters & SensingFilter.CORPSES) != 0)
            {
                foreach (Point p in m_FOV)
                {
                    List<Corpse> corpses = actor.Location.Map.GetCorpsesAt(p.X, p.Y);
                    if (corpses != null)
                        list.Add(new Percept(corpses, actor.Location.Map.LocalTime.TurnCounter, new Location(actor.Location.Map, p)));
                }
            }
            #endregion

            // done.
            return list;
        }
        #endregion
    }
}
