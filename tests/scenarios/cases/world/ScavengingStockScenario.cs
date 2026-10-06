using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Generators;

static class ScavengingStockScenario
{
    static ScenarioWorld Shops(int count)
    {
        var w = CommunityMembershipScenario.Arena(5010);
        for (int i = 0; i < count; i++)
        {
            int x = 1 + i * 7;
            w.Map.AddZone(new Zone("shop " + i, new Rectangle(x, 1, 6, 6)) { BuildingKind = BuildingKind.Grocery });
            for (int j = 0; j < 6; j++)
            {
                var point = new Point(x + 1 + j % 3, 2 + j / 3 * 2);
                w.Map.PlaceMapObjectAt(new MapObject("shelf", GameImages.OBJ_SHOP_SHELF), point);
                ItemFood food = j % 2 == 0 ? new ItemFood(w.Game.GameItems.CANNED_FOOD) : new ItemFood(w.Game.GameItems.GROCERIES, 100 + j);
                food.Quantity = 2; w.Map.DropItemAt(food, point);
            }
        }
        return w;
    }
    static string Digest(Map map)
    {
        var lines = new List<string>();
        foreach (var inventory in map.GroundInventories)
        {
            Point point = map.GetGroundInventoryPosition(inventory).Value;
            foreach (var item in inventory.Items)
                lines.Add(point + ":" + item.Model.ID + ":" + item.Quantity + ":" + (item is ItemFood && ((ItemFood)item).IsPerishable ? ((ItemFood)item).BestBefore.TurnCounter : -1));
        }
        return String.Join(";", lines.OrderBy(s => s));
    }
    static void Conservation(Map map, Dictionary<Item, Tuple<int, Point>> before)
    {
        var remaining = new HashSet<Item>(before.Keys);
        foreach (var inventory in map.GroundInventories)
        {
            Check.Equal(true, inventory.CountItems <= inventory.MaxCapacity, "redistribution respects container capacity");
            foreach (var item in inventory.Items)
            { Check.Equal(true, remaining.Remove(item), "every original object occurs once; no created/replaced/duplicated stock"); Check.Equal(before[item].Item1, item.Quantity, "quantity and original object condition/expiry conserved"); }
        }
        Check.Equal(0, remaining.Count, "no original item vanished");
    }
    static Dictionary<Item, Tuple<int, Point>> Capture(Map map)
    { return map.GroundInventories.SelectMany(i => i.Items.Select(item => new { Item = item, Place = map.GetGroundInventoryPosition(i).Value })).ToDictionary(p => p.Item, p => Tuple.Create(p.Item.Quantity, p.Place)); }
    public static void Register()
    {
        foreach (int count in new[] { 1, 2, 3 })
        {
            int shops = count;
            ScenarioRunner.Add("world/scavenging-stock-" + count + "-shops", () => Shops(shops), w =>
            {
                var before = Capture(w.Map); int moved = ScavengingStock.Redistribute(w.Map); Conservation(w.Map, before);
                Check.Equal(true, moved > 0, "stock is redistributed across existing reachable shelves");
                var totals = w.Map.Zones.Where(z => z.BuildingKind == BuildingKind.Grocery).Select(z => w.Map.GroundInventories.Where(i => z.Bounds.Contains(w.Map.GetGroundInventoryPosition(i).Value)).Sum(i => i.Items.OfType<ItemFood>().Sum(f => f.Quantity))).ToArray();
                if (shops > 1) Check.Equal(true, totals.Max() >= totals.Min() * 3, "small pools include both sparse and concentrated stores");
                else Check.Equal(true, w.Map.GroundInventories.Count() < 6, "single-store shelves have meaningful unevenness");
                string digest = Digest(w.Map); var repeat = Shops(shops); ScavengingStock.Redistribute(repeat.Map);
                Check.Equal(digest, Digest(repeat.Map), "local placement deterministic for same seed and stock");
                Console.WriteLine("Conserved " + before.Count + " intact stacks; shop totals " + String.Join(",", totals) + "; moved " + moved + ".");
            });
        }
        ScenarioRunner.Add("world/scavenging-stock-exclusions-and-fallback", () => Shops(2), w =>
        {
            var houseFood = new ItemFood(w.Game.GameItems.CANNED_FOOD); w.Map.DropItemAt(houseFood, new Point(20, 6));
            var pills = new ItemMedicine(w.Game.GameItems.PILLS_SLP); w.Map.DropItemAt(pills, new Point(2, 2));
            Actor actor = NpcIntentSupport.Actor(w, "existing", 20, 2); NpcIntentSupport.Food(w, actor, 2);
            var inventory = actor.Inventory.Items.ToArray(); var position = actor.Location;
            var before = Capture(w.Map); ScavengingStock.Redistribute(w.Map); Conservation(w.Map, before);
            Check.Equal(true, w.Map.GetItemsAt(20, 6).Contains(houseFood), "ordinary household food remains where generated");
            Check.Equal(true, w.Map.GetItemsAt(2, 2).Contains(pills), "non-healing medicines retain ordinary placement");
            Check.Equal(position, actor.Location, "actor position conserved"); Check.Equal(true, inventory.SequenceEqual(actor.Inventory.Items), "NPC inventory never enters pool");
            // Seal all eligible shelves. Entire pool falls back before any mutation.
            foreach (var zone in w.Map.Zones)
                for (int x = zone.Bounds.Left; x < zone.Bounds.Right; x++) for (int y = zone.Bounds.Top; y < zone.Bounds.Bottom; y++)
                    if (w.Map.GetMapObjectAt(x, y) == null) w.SetTile(x, y, '#');
            string digest = Digest(w.Map); Check.Equal(0, ScavengingStock.Redistribute(w.Map), "inaccessible pool retains original placement"); Check.Equal(digest, Digest(w.Map), "fallback is atomic and conservative");
        });
        ScenarioRunner.Add("world/scavenging-stock-pharmacy-mod", () =>
        {
            ModCatalog.Select(ModCatalog.Discover("mods")[0]); return CommunityMembershipScenario.Arena(5011);
        }, w =>
        {
            try
            {
                for (int shop = 0; shop < 2; shop++)
                {
                    w.Map.AddZone(new Zone("pharmacy " + shop, new Rectangle(1 + shop * 10, 1, 8, 6)) { BuildingKind = BuildingKind.Pharmacy });
                    for (int j = 0; j < 6; j++)
                    {
                        var point = new Point(2 + shop * 10 + j % 3, 2 + j / 3 * 2);
                        w.Map.PlaceMapObjectAt(new MapObject("shelf", GameImages.OBJ_SHOP_SHELF), point);
                        w.Map.DropItemAt(new ItemMedicine(j % 2 == 0 ? w.Game.GameItems.MEDIKIT : w.Game.GameItems.BANDAGE) { Quantity = 2 }, point);
                        w.Map.DropItemAt(new ItemMedicine(w.Game.GameItems.PILLS_SLP), point);
                    }
                }
                var before = Capture(w.Map); Check.Equal(true, ScavengingStock.Redistribute(w.Map) > 0, "bundled mod healing stock redistributes"); Conservation(w.Map, before);
                foreach (var pair in before.Where(p => p.Key is ItemMedicine && ((ItemMedicine)p.Key).Healing == 0))
                    Check.Equal(true, w.Map.GetItemsAt(pair.Value.Item2).Contains(pair.Key), "unrelated mod pharmaceuticals stay on their original shelf");
                Console.WriteLine("Bundled Deonapocalypse stock conservation passes for " + before.Count + " intact stacks.");
            }
            finally { ModCatalog.Select(); }
        });
        ScenarioRunner.Add("world/scavenging-generated-conservation", () => TownScenarioFactory.Create(5012, false), w =>
        {
            var timer = Stopwatch.StartNew(); CommunityNewWorldScenario.Generate(w, true, false); timer.Stop();
            int moved = 0, items = 0; var elapsed = Stopwatch.StartNew();
            for (int x = 0; x < Session.Get.World.Size; x++) for (int y = 0; y < Session.Get.World.Size; y++)
            {
                var district = Session.Get.World[x, y];
                var all = district.Maps.ToDictionary(m => m, m => Capture(m));
                var actors = district.Maps.SelectMany(m => m.Actors).ToDictionary(a => a, a => Tuple.Create(a.Location, a.Inventory == null ? new Item[0] : a.Inventory.Items.ToArray()));
                moved += ScavengingStock.Redistribute(district.EntryMap);
                foreach (var pair in all) { Conservation(pair.Key, pair.Value); items += pair.Value.Count; if (pair.Key != district.EntryMap) Check.Equal(true, pair.Value.All(p => pair.Key.GetItemsAt(p.Value.Item2).Contains(p.Key)), "basements/hospital/other maps retain original stock positions"); }
                foreach (var pair in actors) { Check.Equal(pair.Value.Item1, pair.Key.Location, "generated survivors never relocate during stock redistribution"); Check.Equal(true, pair.Value.Item2.SequenceEqual(pair.Key.Inventory == null ? new Item[0] : pair.Key.Inventory.Items.ToArray()), "generated actors retain exact equipment"); }
            }
            elapsed.Stop(); Check.Equal(true, moved > 0, "actual generated shops redistribute existing stock");
            Console.WriteLine("Real nine-district generation " + timer.Elapsed.TotalSeconds.ToString("F3") + "s; redistribution " + elapsed.Elapsed.TotalMilliseconds.ToString("F2") + "ms; conserved " + items + " ground objects, moved " + moved + " eligible stacks, no survivor/equipment/position changes.");
        });
    }
}
