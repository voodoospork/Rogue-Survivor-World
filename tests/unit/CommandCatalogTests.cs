using System;
using System.Collections;
using System.Reflection;
using System.Windows.Forms;

static class CommandCatalogTests
{
    public static void Run()
    {
        Type commandType = Check.Type("Engine.PlayerCommand");
        Type catalogType = Check.Type("Engine.PlayerCommandCatalog");
        Array entries = (Array)catalogType.GetField("Bindable").GetValue(null);
        Type bindingsType = Check.Type("Engine.Keybindings");
        object defaults = Activator.CreateInstance(bindingsType, true);
        Hashtable seen = new Hashtable();
        Hashtable usedKeys = new Hashtable();

        foreach (object entry in entries)
        {
            object command = entry.GetType().GetField("Command").GetValue(entry);
            string label = (string)entry.GetType().GetField("Label").GetValue(entry);
            Check.Equal(false, seen.ContainsKey(command), "duplicate command " + command);
            Check.Equal(true, !String.IsNullOrEmpty(label), "empty label " + command);
            Check.Equal(true, Check.Call(defaults, "Get", new Type[] { commandType }, command).Equals(Keys.None) == false,
                "missing default key " + command);
            seen[command] = true;
            var key = (Keys)Check.Call(defaults, "Get", new Type[] { commandType }, command);
            Check.Equal(false, usedKeys.ContainsKey(key), "duplicate default key " + key);
            Check.Equal(command, Check.Call(defaults, "Get", key), "default inverse maps to the same command");
            usedKeys[key] = true;
        }
        foreach (object command in Enum.GetValues(commandType))
            if (command.ToString() != "NONE")
                Check.Equal(true, seen.ContainsKey(command), "command missing from binding screen " + command);
        Check.Equal(false, Check.Call(defaults, "CheckForConflict"), "all defaults are consistent");
        object recall = Enum.Parse(commandType, "RECALL_FOLLOWERS");
        object save = Enum.Parse(commandType, "SAVE_GAME");
        Check.Equal(Keys.S | Keys.Shift, Check.Call(defaults, "Get", new[] { commandType }, save), "original Save key preserved");
        Check.Throws<InvalidOperationException>(() => Check.Call(defaults, "SetDefault", recall, Keys.S | Keys.Shift), "new default cannot take Save's key");
        Check.Equal(save, Check.Call(defaults, "Get", Keys.S | Keys.Shift), "failed default leaves Save intact");
        Check.Throws<InvalidOperationException>(() => Check.Call(defaults, "SetDefault", recall, Keys.D1 | Keys.Control), "defaults cannot take implicit inventory keys");
        Check.Call(defaults, "Set", recall, Keys.None);
        Check.Call(defaults, "Set", Enum.Parse(commandType, "DISARM_TRAP"), Keys.None);
        Check.Equal(Enum.Parse(commandType, "NONE"), Check.Call(defaults, "Get", Keys.None), "unbound keys are not reverse bindings");
        Check.Equal(false, Check.Call(defaults, "CheckForConflict"), "multiple unbound commands are legal");
        IDictionary forward = (IDictionary)bindingsType.GetField("m_CommandToKeyData", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(defaults);
        forward[recall] = Keys.S | Keys.Shift;
        Check.Equal(true, Check.Call(defaults, "CheckForConflict"), "duplicate forward assignments detected");
        forward.Remove(recall);
        IDictionary reverse = (IDictionary)bindingsType.GetField("m_KeyToCommand", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(defaults);
        reverse[Keys.F9] = save;
        Check.Equal(true, Check.Call(defaults, "CheckForConflict"), "stale reverse assignments detected");
    }
}
