using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;

namespace djack.RogueSurvivor.Gameplay.Generators
{
    static class ScavengingStock
    {
        sealed class Stock { public Item Item; public Point From, To; }
        sealed class Bin { public readonly List<Point> Cells = new List<Point>(); public int Weight, Units; }
        static bool Eligible(Item item, BuildingKind kind)
        { return kind == BuildingKind.Grocery ? item is ItemFood : item is ItemMedicine && ((ItemMedicine)item).Healing > 0; }
        public static int Redistribute(Map map)
        {
            int moved = 0;
            var reachable = Reachable(map);
            foreach (var kind in new[] { BuildingKind.Grocery, BuildingKind.Pharmacy })
            {
                var bins = new List<Bin>(); var pool = new List<Stock>(); var capacity = new Dictionary<Point, int>();
                var random = new Random(unchecked(map.Seed * 397 + (int)kind));
                foreach (var zone in map.Zones.Where(z => z.BuildingKind == kind).OrderBy(z => z.Bounds.Y).ThenBy(z => z.Bounds.X))
                {
                    var bin = new Bin();
                    for (int y = zone.Bounds.Top; y < zone.Bounds.Bottom; y++) for (int x = zone.Bounds.Left; x < zone.Bounds.Right; x++)
                    {
                        var point = new Point(x, y); var shelf = map.GetMapObjectAt(point);
                        if (shelf == null || shelf.ImageID != GameImages.OBJ_SHOP_SHELF || capacity.ContainsKey(point)) continue;
                        var inventory = map.GetItemsAt(point);
                        var items = inventory == null ? new List<Item>() : inventory.Items.Where(i => Eligible(i, kind)).ToList();
                        // If original eligible stock is inaccessible, retain the entire pool unchanged.
                        bool accessible = Direction.COMPASS.Any(d => { Point neighbor = point + d; return map.IsInBounds(neighbor) && reachable[neighbor.X, neighbor.Y]; });
                        if (!accessible && items.Count > 0) { bins.Clear(); pool.Clear(); goto NextKind; }
                        if (!accessible) continue;
                        bin.Cells.Add(point);
                        capacity[point] = Map.GROUND_INVENTORY_SLOTS - (inventory == null ? 0 : inventory.CountItems - items.Count);
                        foreach (var item in items) pool.Add(new Stock { Item = item, From = point });
                    }
                    if (bin.Cells.Count > 0) bins.Add(bin);
                }
                if (pool.Count == 0 || bins.Count == 0) continue;
                if (bins.Count == 1) bins = bins[0].Cells.Select(p => { var b = new Bin(); b.Cells.Add(p); return b; }).ToList();
                // Local randomness does not advance generation's actor/equipment dice.
                bins = bins.OrderBy(b => random.Next()).ToList();
                int sparse = bins.Count == 1 ? 0 : Math.Max(1, bins.Count / 4);
                int cache = bins.Count == 1 ? 1 : Math.Max(1, bins.Count / 4);
                for (int i = 0; i < bins.Count; i++)
                { bins[i].Weight = i < sparse ? 1 : i >= bins.Count - cache ? 8 : 4; bins[i].Cells.Sort((a, b) => a.Y == b.Y ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y)); }
                int totalUnits = pool.Sum(s => s.Item.Quantity), totalWeight = bins.Sum(b => b.Weight);
                foreach (var stock in pool.OrderByDescending(s => s.Item.Quantity).ThenBy(s => s.From.Y).ThenBy(s => s.From.X))
                {
                    var bin = bins.Where(b => b.Cells.Any(p => capacity[p] > 0)).OrderByDescending(b => (double)totalUnits * b.Weight / totalWeight - b.Units).FirstOrDefault();
                    if (bin == null) goto NextKind; // Complete preflight before mutating any inventory.
                    var point = bin.Cells.First(p => capacity[p] > 0);
                    stock.To = point; capacity[point]--; bin.Units += stock.Item.Quantity;
                }
                foreach (var stock in pool) map.RemoveItemAt(stock.Item, stock.From);
                var placed = new List<Stock>();
                foreach (var stock in pool)
                {
                    if (!map.PlaceItemIntact(stock.Item, stock.To))
                    {
                        foreach (var prior in placed) map.RemoveItemAt(prior.Item, prior.To);
                        foreach (var original in pool)
                            if (!map.PlaceItemIntact(original.Item, original.From)) throw new InvalidOperationException("Cannot restore original shop stock.");
                        goto NextKind;
                    }
                    placed.Add(stock);
                }
                moved += pool.Count(s => s.From != s.To);
            NextKind: ;
            }
            return moved;
        }
        internal static bool[,] Reachable(Map map)
        {
            var seen = new bool[map.Width, map.Height]; var queue = new Queue<Point>();
            for (int x = 0; x < map.Width; x++) { Enqueue(map, new Point(x, 0), seen, queue); Enqueue(map, new Point(x, map.Height - 1), seen, queue); }
            for (int y = 0; y < map.Height; y++) { Enqueue(map, new Point(0, y), seen, queue); Enqueue(map, new Point(map.Width - 1, y), seen, queue); }
            while (queue.Count > 0) { var point = queue.Dequeue(); foreach (var direction in Direction.COMPASS) Enqueue(map, point + direction, seen, queue); }
            return seen;
        }
        static void Enqueue(Map map, Point point, bool[,] seen, Queue<Point> queue)
        {
            if (!map.IsInBounds(point) || seen[point.X, point.Y] || !map.GetTileAt(point).Model.IsWalkable) return;
            var obj = map.GetMapObjectAt(point);
            if (obj != null && !obj.IsWalkable && !(obj is DoorWindow)) return;
            seen[point.X, point.Y] = true; queue.Enqueue(point);
        }
    }
}
