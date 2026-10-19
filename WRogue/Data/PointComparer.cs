using System.Collections.Generic;
using System.Drawing;

namespace djack.RogueSurvivor.Data
{
    // Framework 4.8 Point has no IEquatable<Point>. The default comparer boxes
    // both operands on every collision. Coordinate equality remains unchanged.
    sealed class PointComparer : IEqualityComparer<Point>
    {
        internal static readonly PointComparer Instance = new PointComparer();
        public bool Equals(Point a, Point b) { return a.X == b.X && a.Y == b.Y; }
        public int GetHashCode(Point point) { unchecked { return point.X * 397 ^ point.Y; } }
    }
}
