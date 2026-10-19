using System;
using djack.RogueSurvivor.Data;

namespace djack.RogueSurvivor.Engine
{
    // A historical clock never moves the live player's world clock backwards.
    sealed class SimulationClock : IDisposable
    {
        [ThreadStatic] static SimulationClock current;
        readonly SimulationClock previous;
        readonly Session session;
        readonly WorldTime time;
        readonly DiceRoller random;
        readonly District eventDistrict;
        readonly AdaptiveIdentityState identities;
        internal SimulationClock(Session owner, int turn, DiceRoller dice, District events = null, AdaptiveIdentityState identity = null)
        { session = owner; time = new WorldTime(Math.Max(0, turn - 1)); time.TurnCounter = turn;
            random = dice; eventDistrict = events; identities = identity; previous = current; current = this; }
        internal static WorldTime Time(Session owner, WorldTime live)
        { return current != null && current.session == owner ? current.time : live; }
        internal static DiceRoller Random { get { return current == null ? null : current.random; } }
        internal static DiceRoller RandomFor(Session owner)
        { return current != null && current.session == owner ? current.random : null; }
        internal static Weather WeatherAt(World world, Weather live)
        { return current != null && current.session.World == world ? world.WeatherAt(current.time.TurnCounter) : live; }
        internal static WorldTime LocalTime(Map map, WorldTime live)
        {
            if (current?.eventDistrict == null || current.eventDistrict != map.District) return live;
            var state = map.District.Adaptive?.Maps.Find(s => s.Map == map);
            int offset = state == null || !state.HasWorldTurn ? 0 : live.TurnCounter - state.WorldTurn;
            int turn = current.time.TurnCounter + offset;
            var local = new WorldTime(Math.Max(0, turn - 1)); local.TurnCounter = Math.Max(0, turn); return local;
        }
        internal static bool HasIdentityStream { get { return current?.identities != null && current.session == Session.Get; } }
        internal static Guid NewIdentity()
        { return HasIdentityStream ? current.identities.NewIdentity() : Guid.NewGuid(); }
        internal static int ModelCount(int model, int legacy)
        { return HasIdentityStream ? current.identities.NextModelCount(model) : legacy; }
        public void Dispose() { current = previous; }
    }
}
