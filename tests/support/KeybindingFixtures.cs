using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using djack.RogueSurvivor.Engine;

static class KeybindingFixtures
{
    public static void Write(string directory)
    {
        Check.Equal(null, typeof(Keybindings).GetField("m_DefaultsRevision", BindingFlags.NonPublic | BindingFlags.Instance),
            "fixture producer must be the preserved pre-fix assembly");
        if (Directory.Exists(directory) && Directory.GetFileSystemEntries(directory).Length != 0)
            throw new InvalidOperationException("Preserve existing keybinding fixtures.");
        Directory.CreateDirectory(directory);
        var previous = new Keybindings();
        BinarySaveStore.Save(Path.Combine(directory, "previous-world.dat"), previous);
        var original = new Keybindings(); original.Set(PlayerCommand.RECALL_FOLLOWERS, Keys.None);
        original.Set(PlayerCommand.SAVE_GAME, Keys.S | Keys.Shift);
        BinarySaveStore.Save(Path.Combine(directory, "original-defaults.dat"), original);
        var custom = new Keybindings(); custom.Set(PlayerCommand.SAVE_GAME, Keys.F9);
        custom.Set(PlayerCommand.RECALL_FOLLOWERS, Keys.F10); custom.Set(PlayerCommand.SHOUT, Keys.R | Keys.Control);
        BinarySaveStore.Save(Path.Combine(directory, "custom.dat"), custom);
        original.Set(PlayerCommand.ITEM_SLOT_0, Keys.R);
        BinarySaveStore.Save(Path.Combine(directory, "custom-slot.dat"), original);
        Console.WriteLine("Four genuine pre-fix keybinding fixtures written; no user settings involved.");
    }
}
