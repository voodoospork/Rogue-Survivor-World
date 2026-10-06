using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;

static class TrapDisarmScenario
{
    static ScenarioWorld Arena(int seed)
    {
        var world = TownScenarioFactory.Arena(seed, "........", "........", "........", "........", "........");
        Session.Get.GamePreset = GamePreset.World();
        RogueGame.KeyBindings.ResetToDefaults();
        return world;
    }
    static void View(ScenarioWorld world, Actor player)
    { world.Game.ComputeViewRect(player.Location.Position); Check.Call(world.Game, "UpdatePlayerFOV", player); }
    static ItemTrap Trap(ScenarioWorld world, Point position, ItemModel model, Actor owner = null)
    {
        var trap = new ItemTrap(model); trap.Activate(owner); world.Map.DropItemAt(trap, position); return trap;
    }
    public static void Register()
    {
        ScenarioRunner.Add("items/trap-disarm-action", () => Arena(5201), w =>
        {
            Actor player = NpcIntentSupport.Player(w, 2, 2); View(w, player);
            player.Inventory.MaxCapacity = 0;
            int hp = player.HitPoints;
            foreach (ItemModel model in new[] { w.Game.GameItems.BEAR_TRAP, w.Game.GameItems.BARBED_WIRE, w.Game.GameItems.SPIKES, w.Game.GameItems.EMPTY_CAN })
            {
                Point position = new Point(3, 2); ItemTrap trap = Trap(w, position, model);
                int quantity = trap.Quantity; player.ActionPoints = Rules.BASE_ACTION_COST;
                Check.Equal(true, w.Game.DoDisarmTrap(player, position, trap), "adjacent trap disarmed despite full inventory");
                Check.Equal(0, player.ActionPoints, "one normal action");
                Check.Equal(false, trap.IsActivated, "disabled"); Check.Equal(false, trap.IsTriggered, "not sprung");
                Check.Equal(null, trap.Owner, "old ownership cleared"); Check.Equal(null, trap.BaseOwner, "old base link cleared");
                Check.Equal(true, w.Map.GetItemsAt(position).Contains(trap), "same trap stays on ground");
                Check.Equal(quantity, trap.Quantity, "no quantity reward or loss"); Check.Equal(hp, player.HitPoints, "no step or trigger damage");
                Check.Equal(new Point(2, 2), player.Location.Position, "player stays beside trap");
                player.ActionPoints = Rules.BASE_ACTION_COST;
                Check.Equal(false, w.Game.DoDisarmTrap(player, position, trap), "repeating inactive trap rejected");
                Check.Equal(Rules.BASE_ACTION_COST, player.ActionPoints, "invalid repetition free"); w.Map.RemoveItemAt(trap, position);
            }
            var own = Trap(w, player.Location.Position, w.Game.GameItems.BEAR_TRAP, player);
            Check.Equal(true, w.Game.DoDisarmTrap(player, player.Location.Position, own), "own-tile armed trap can be handled");
        });
        ScenarioRunner.Add("items/trap-disarm-boundaries", () => Arena(5202), w =>
        {
            Actor player = NpcIntentSupport.Player(w, 2, 2); View(w, player);
            Point position = new Point(3, 2); ItemTrap trap = Trap(w, position, w.Game.GameItems.BEAR_TRAP); string reason;
            Check.Equal(false, w.Game.Rules.CanActorDisarmTrap(player, new Point(5, 2), trap, out reason), "distant target rejected");
            Check.Equal(false, w.Game.Rules.CanActorDisarmTrap(player, new Point(1, 2), trap, out reason), "matching item must occupy actual target");
            Check.Equal(false, w.Game.Rules.CanActorDisarmTrap(player, new Point(-1, 2), trap, out reason), "bounds checked");
            trap.IsTriggered = true; player.ActionPoints = 100;
            Check.Equal(false, w.Game.DoDisarmTrap(player, position, trap), "cannot bypass existing escape rule");
            Check.Equal(true, trap.IsTriggered, "sprung trap intact"); Check.Equal(100, player.ActionPoints, "rejected action free");
            trap.IsTriggered = false;
            var door = new DoorWindow("door", "closed", "open", "broken", 40); w.Map.PlaceMapObjectAt(door, position);
            Check.Equal(false, w.Game.DoDisarmTrap(player, position, trap), "closed obstacle prevents access");
            w.Map.RemoveMapObjectAt(position.X, position.Y);
            player.IsSleeping = true; Check.Equal(false, w.Game.DoDisarmTrap(player, position, trap), "sleeping actor cannot disarm");
            player.IsSleeping = false; player.IsDead = true; Check.Equal(false, w.Game.DoDisarmTrap(player, position, trap), "dead actor cannot disarm");
        });
        ScenarioRunner.Add("items/trap-disarm-input", () => Arena(5203), w =>
        {
            Actor player = NpcIntentSupport.Player(w, 2, 2); View(w, player);
            Point position = new Point(3, 2); ItemTrap trap = Trap(w, position, w.Game.GameItems.BEAR_TRAP);
            var ui = (ScenarioUI)w.Game.UI; player.ActionPoints = 100;
            ui.QueueKey(Keys.D); ui.QueueKey(Keys.NumPad6); Check.Call(w.Game, "HandlePlayerActor", player);
            Check.Equal(false, trap.IsActivated, "real D direction input disables trap"); Check.Equal(0, player.ActionPoints, "input charged once");
            trap.Activate(null); player.ActionPoints = 100; ui.QueueKey(Keys.Escape);
            Check.Equal(false, Check.Call(w.Game, "HandlePlayerDisarmTrap", player), "cancel free");
            Check.Equal(100, player.ActionPoints, "cancel does not advance turn");
            ui.QueueKey(Keys.NumPad4); ui.QueueKey(Keys.Escape);
            Check.Equal(false, Check.Call(w.Game, "HandlePlayerDisarmTrap", player), "empty tile then cancellation free");
            Check.Equal(true, trap.IsActivated, "wrong direction changes no trap");
            string hover = String.Join(" ", (string[])Check.Call(w.Game, "DescribeStuffAt", w.Map, position));
            Check.Equal(true, hover.Contains("Disarm from an adjacent tile"), "map hover advertises action");
        });
        ScenarioRunner.Add("items/trap-disarm-claimed", () => Arena(5204), w =>
        {
            Actor player = NpcIntentSupport.Player(w, 2, 2), owner = NpcIntentSupport.Actor(w, "Owner", 4, 2); View(w, player);
            Point position = new Point(3, 2); var home = new XpdBase(owner, new[] { position }); w.Map.AddXpdBase(home);
            ItemTrap trap = Trap(w, position, w.Game.GameItems.BEAR_TRAP, owner); trap.SetBaseOwner(home);
            Check.Equal(true, w.Game.DoDisarmTrap(player, position, trap), "foreign claimed trap handled");
            Check.Equal(true, w.Game.Rules.AreEnemies(player, owner), "awake witness reacts to tampering");
            Check.Same(home, w.Map.XpdBaseAt(position), "claim survives disabled trap");
        });
        ScenarioRunner.Add("items/trap-disarm-unwitnessed", () => Arena(5205), w =>
        {
            Actor player = NpcIntentSupport.Player(w, 2, 2), owner = NpcIntentSupport.Actor(w, "Owner", 4, 2); View(w, player);
            Point position = new Point(3, 2); var home = new XpdBase(owner, new[] { position }); w.Map.AddXpdBase(home);
            ItemTrap trap = Trap(w, position, w.Game.GameItems.BEAR_TRAP, owner); trap.SetBaseOwner(home); owner.IsSleeping = true;
            Check.Equal(true, w.Game.DoDisarmTrap(player, position, trap), "unwitnessed disarm succeeds");
            Check.Equal(false, w.Game.Rules.AreEnemies(player, owner), "sleeping owner learns no instant hostility");
        });
        ScenarioRunner.Add("items/trap-disarm-persistence", () => Arena(5206), w =>
        {
            Actor player = NpcIntentSupport.Player(w, 2, 2); View(w, player);
            ItemTrap trap = Trap(w, new Point(3, 2), w.Game.GameItems.BEAR_TRAP);
            Check.Equal(true, w.Game.DoDisarmTrap(player, new Point(3, 2), trap), "disarmed before save");
            string root = Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), path = Path.Combine(root, "trap-disarm-format-5.dat");
            BinarySaveStore.Save(path, w.Map); Map loaded = BinarySaveStore.Load<Map>(path); loaded.ReconstructAuxiliaryFields();
            var saved = (ItemTrap)loaded.GetItemsAt(new Point(3, 2)).TopItem;
            Check.Equal(false, saved.IsActivated, "reload preserves inactive state"); Check.Equal(null, saved.Owner, "reload preserves cleared owner");
            var bindings = new Keybindings(); bindings.Set(PlayerCommand.DISARM_TRAP, Keys.None);
            path = Path.Combine(root, "trap-disarm-old-keys.dat"); Keybindings.Save(bindings, path);
            var migrated = Keybindings.Load(path); Check.Equal(Keys.D, migrated.Get(PlayerCommand.DISARM_TRAP), "older defaults gain D");
            bindings.Set(PlayerCommand.SHOUT, Keys.D); Keybindings.Save(bindings, path); migrated = Keybindings.Load(path);
            Check.Equal(PlayerCommand.SHOUT, migrated.Get(Keys.D), "existing custom D preserved");
            Check.Equal(Keys.D | Keys.Control, migrated.Get(PlayerCommand.DISARM_TRAP), "free fallback used");
            Check.Equal(true, PlayerCommandCatalog.Bindable.Any(e => e.Command == PlayerCommand.DISARM_TRAP), "command rebindable");
        });
    }
}
