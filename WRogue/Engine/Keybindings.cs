using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace djack.RogueSurvivor.Engine
{
    [Serializable]
    class Keybindings
    {
        #region Fields
        Dictionary<PlayerCommand, Keys> m_CommandToKeyData;
        Dictionary<Keys, PlayerCommand> m_KeyToCommand;
        [System.Runtime.Serialization.OptionalField] int m_DefaultsRevision;
        const int CURRENT_DEFAULTS_REVISION = 1;
        #endregion

        #region Init
        public Keybindings()
        {
            m_CommandToKeyData = new Dictionary<PlayerCommand, Keys>();
            m_KeyToCommand = new Dictionary<Keys, PlayerCommand>();
            ResetToDefaults();
        }

        public void ResetToDefaults()
        {
            m_CommandToKeyData.Clear();
            m_KeyToCommand.Clear();
            m_DefaultsRevision = CURRENT_DEFAULTS_REVISION;

            SetDefault(PlayerCommand.BARRICADE_MODE, Keys.B);
            SetDefault(PlayerCommand.BREAK_MODE, Keys.K);
            SetDefault(PlayerCommand.CLOSE_DOOR, Keys.C);
            SetDefault(PlayerCommand.DISARM_TRAP, Keys.D);
            SetDefault(PlayerCommand.FIRE_MODE, Keys.F);
            SetDefault(PlayerCommand.HELP_MODE, Keys.H);
            SetDefault(PlayerCommand.KEYBINDING_MODE, Keys.K | Keys.Shift);

            SetDefault(PlayerCommand.ITEM_SLOT_0, Keys.D1);
            SetDefault(PlayerCommand.ITEM_SLOT_1, Keys.D2);
            SetDefault(PlayerCommand.ITEM_SLOT_2, Keys.D3);
            SetDefault(PlayerCommand.ITEM_SLOT_3, Keys.D4);
            SetDefault(PlayerCommand.ITEM_SLOT_4, Keys.D5);
            SetDefault(PlayerCommand.ITEM_SLOT_5, Keys.D6);
            SetDefault(PlayerCommand.ITEM_SLOT_6, Keys.D7);
            SetDefault(PlayerCommand.ITEM_SLOT_7, Keys.D8);
            SetDefault(PlayerCommand.ITEM_SLOT_8, Keys.D9);
            SetDefault(PlayerCommand.ITEM_SLOT_9, Keys.D0);

            SetDefault(PlayerCommand.ABANDON_GAME, Keys.A | Keys.Shift);
            SetDefault(PlayerCommand.ADVISOR, Keys.H | Keys.Shift);
            SetDefault(PlayerCommand.BUILD_LARGE_FORTIFICATION, Keys.N | Keys.Control);
            SetDefault(PlayerCommand.BUILD_SMALL_FORTIFICATION, Keys.N);
            SetDefault(PlayerCommand.CITY_INFO, Keys.I);
            SetDefault(PlayerCommand.RELATIONSHIPS, Keys.I | Keys.Shift);
            SetDefault(PlayerCommand.EAT_CORPSE, Keys.E | Keys.Shift);
            SetDefault(PlayerCommand.GIVE_ITEM, Keys.G);
            SetDefault(PlayerCommand.HINTS_SCREEN_MODE, Keys.H | Keys.Control);
            SetDefault(PlayerCommand.NEGOCIATE_TRADE, Keys.E);
            SetDefault(PlayerCommand.TALK, Keys.V);
            SetDefault(PlayerCommand.HEARD_JOURNAL, Keys.J);
            SetDefault(PlayerCommand.LOAD_GAME, Keys.L | Keys.Shift);
            SetDefault(PlayerCommand.MARK_ENEMIES_MODE, Keys.E | Keys.Control);
            SetDefault(PlayerCommand.MESSAGE_LOG, Keys.M | Keys.Shift);
            SetDefault(PlayerCommand.MOUSE_MOVE_MODE, Keys.M);
            SetDefault(PlayerCommand.XPD_BASE, Keys.B | Keys.Control);
            SetDefault(PlayerCommand.MOVE_E, Keys.NumPad6);
            SetDefault(PlayerCommand.MOVE_N, Keys.NumPad8);
            SetDefault(PlayerCommand.MOVE_NE, Keys.NumPad9);
            SetDefault(PlayerCommand.MOVE_NW, Keys.NumPad7);
            SetDefault(PlayerCommand.MOVE_S, Keys.NumPad2);
            SetDefault(PlayerCommand.MOVE_SE, Keys.NumPad3);
            SetDefault(PlayerCommand.MOVE_SW, Keys.NumPad1);
            SetDefault(PlayerCommand.MOVE_W, Keys.NumPad4);
            SetDefault(PlayerCommand.OPTIONS_MODE, Keys.O | Keys.Shift);
            SetDefault(PlayerCommand.ORDER_MODE, Keys.O);
            SetDefault(PlayerCommand.PULL_MODE, Keys.P | Keys.Control); // alpha10
            SetDefault(PlayerCommand.PUSH_MODE, Keys.P);
            SetDefault(PlayerCommand.QUIT_GAME, Keys.Q | Keys.Shift);
            SetDefault(PlayerCommand.REVIVE_CORPSE, Keys.R | Keys.Shift);
            SetDefault(PlayerCommand.RUN_TOGGLE, Keys.R);
            SetDefault(PlayerCommand.SAVE_GAME, Keys.S | Keys.Shift);
            SetDefault(PlayerCommand.SCREENSHOT, Keys.N | Keys.Shift);
            SetDefault(PlayerCommand.SHOUT, Keys.S);
            SetDefault(PlayerCommand.RECALL_FOLLOWERS, Keys.R | Keys.Control);
            SetDefault(PlayerCommand.SLEEP, Keys.Z);
            SetDefault(PlayerCommand.SWITCH_PLACE, Keys.S | Keys.Control);
            SetDefault(PlayerCommand.LEAD_MODE, Keys.T);
            SetDefault(PlayerCommand.USE_SPRAY, Keys.A);
            SetDefault(PlayerCommand.USE_EXIT, Keys.X);
            SetDefault(PlayerCommand.WAIT_OR_SELF, Keys.NumPad5);
            SetDefault(PlayerCommand.WAIT_LONG, Keys.W);
        }
        void SetDefault(PlayerCommand command, Keys key)
        {
            // New defaults must never silently evict an existing command.
            if (key == Keys.None || Get(key) != PlayerCommand.NONE || IsInventoryShortcut(key))
                throw new InvalidOperationException("Conflicting default key: " + key + " for " + command);
            Set(command, key);
        }

        internal bool IsInventoryShortcut(Keys key)
        {
            if ((key & Keys.Modifiers) == Keys.None) return false;
            Keys code = key & Keys.KeyCode;
            for (PlayerCommand slot = PlayerCommand.ITEM_SLOT_0; slot <= PlayerCommand.ITEM_SLOT_9; slot++)
                if (Get(slot) == code) return true;
            return false;
        }

        void AssignMissing(PlayerCommand command, params Keys[] candidates)
        {
            if (Get(command) != Keys.None) return;
            foreach (Keys key in candidates)
                if (Get(key) == PlayerCommand.NONE && !IsInventoryShortcut(key)) { Set(command, key); return; }
        }

        void RepairRecallDefaults()
        {
            // Only the exact pair shipped by the earlier World build is repaired.
            // A versioned file can intentionally use that pair without being changed.
            if (m_DefaultsRevision < CURRENT_DEFAULTS_REVISION &&
                Get(PlayerCommand.SAVE_GAME) == (Keys.S | Keys.Control | Keys.Shift) &&
                Get(PlayerCommand.RECALL_FOLLOWERS) == (Keys.S | Keys.Shift) &&
                !IsInventoryShortcut(Keys.S | Keys.Shift))
            {
                Set(PlayerCommand.RECALL_FOLLOWERS, Keys.None);
                Set(PlayerCommand.SAVE_GAME, Keys.S | Keys.Shift);
                Logger.WriteLine(Logger.Stage.RUN_MAIN, "Restored Shift+S save; moving old default recall to a free key.");
            }
            AssignMissing(PlayerCommand.RECALL_FOLLOWERS, Keys.R | Keys.Control,
                Keys.R | Keys.Control | Keys.Shift, Keys.S | Keys.Control | Keys.Shift);
            m_DefaultsRevision = CURRENT_DEFAULTS_REVISION;
        }
        #endregion

        #region Getting & Setting
        /// <summary>
        /// Get KeyData (key code & modifiers).
        /// </summary>
        /// <param name="command"></param>
        /// <returns></returns>
        public Keys Get(PlayerCommand command)
        {
            Keys key;
            if (m_CommandToKeyData.TryGetValue(command, out key))
                return key;
            else
                return Keys.None;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key">KeyData</param>
        /// <returns></returns>
        public PlayerCommand Get(Keys key)
        {
            if (key == Keys.None) return PlayerCommand.NONE;
            PlayerCommand cmd;
            if (m_KeyToCommand.TryGetValue(key, out cmd))
                return cmd;
            return PlayerCommand.NONE;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="command"></param>
        /// <param name="key">KeyData</param>
        public void Set(PlayerCommand command, Keys key)
        {
            if (command == PlayerCommand.NONE) throw new ArgumentException("Cannot bind the empty command.", "command");
            Keys prevKey = Get(command);
            if (prevKey != Keys.None) m_KeyToCommand.Remove(prevKey);
            m_CommandToKeyData.Remove(command);
            m_KeyToCommand.Remove(Keys.None);
            if (key == Keys.None) return;
            // remove previous bind.
            PlayerCommand prevCommand = Get(key);
            if (prevCommand != PlayerCommand.NONE)
            {
                m_CommandToKeyData.Remove(prevCommand);
            }
                     
            // rebind.
            m_CommandToKeyData[command] = key;
            m_KeyToCommand[key] = command;
       }
        #endregion

        #region Checking for keys conflict
        public bool CheckForConflict()
        {
            var seen = new HashSet<Keys>();
            foreach (var binding in m_CommandToKeyData)
            {
                if (binding.Value == Keys.None) continue;
                if (!seen.Add(binding.Value) || Get(binding.Value) != binding.Key ||
                    IsInventoryShortcut(binding.Value)) return true;
            }
            foreach (var binding in m_KeyToCommand)
                if (binding.Key != Keys.None && Get(binding.Value) != binding.Key) return true;
            return false;
        }
        #endregion

        #region Saving & Loading
        public static void Save(Keybindings kb, string filepath)
        {
            if (kb == null)
                throw new ArgumentNullException("kb");

            Logger.WriteLine(Logger.Stage.RUN_MAIN, "saving keybindings...");

            BinarySaveStore.Save(filepath, kb);

            Logger.WriteLine(Logger.Stage.RUN_MAIN, "saving keybindings... done!");
        }

        /// <summary>
        /// Attempt to load, if failed return bindings with defaults.
        /// </summary>
        /// <returns></returns>
        public static Keybindings Load(string filepath)
        {
            Logger.WriteLine(Logger.Stage.RUN_MAIN, "loading keybindings...");

            Keybindings kb;

            try
            {
                kb = BinarySaveStore.Load<Keybindings>(filepath);
                kb.AssignMissing(PlayerCommand.DISARM_TRAP, Keys.D, Keys.D | Keys.Control);
                kb.RepairRecallDefaults();
                kb.AssignMissing(PlayerCommand.MOUSE_MOVE_MODE, Keys.M);
                kb.AssignMissing(PlayerCommand.XPD_BASE, Keys.B | Keys.Control);
                kb.AssignMissing(PlayerCommand.RELATIONSHIPS, Keys.I | Keys.Shift);
                if (kb.Get(PlayerCommand.TALK) == (Keys.T | Keys.Control))
                {
                    if (kb.Get(Keys.V) == PlayerCommand.NONE && !kb.IsInventoryShortcut(Keys.V)) kb.Set(PlayerCommand.TALK, Keys.V);
                    else if (kb.Get(Keys.V | Keys.Shift) == PlayerCommand.NONE && !kb.IsInventoryShortcut(Keys.V | Keys.Shift))
                        kb.Set(PlayerCommand.TALK, Keys.V | Keys.Shift);
                    // If both alternatives are occupied, retain the old binding.
                }
                else kb.AssignMissing(PlayerCommand.TALK, Keys.V, Keys.V | Keys.Shift);
                kb.AssignMissing(PlayerCommand.HEARD_JOURNAL, Keys.J, Keys.J | Keys.Shift);
            }
            catch (Exception e)
            {
                Logger.WriteLine(Logger.Stage.RUN_MAIN, "failed to load keybindings (first run?), using defaults.");
                Logger.WriteLine(Logger.Stage.RUN_MAIN, String.Format("load exception : {0}.", e.ToString()));
                kb = new Keybindings();
                kb.ResetToDefaults();
            }

            Logger.WriteLine(Logger.Stage.RUN_MAIN, "loading keybindings... done!");

            return kb;
        }

        #endregion
    }
}
