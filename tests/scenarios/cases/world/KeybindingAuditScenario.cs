using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using djack.RogueSurvivor.Engine;

static class KeybindingAuditScenario
{
    static string Artifact(string name) { return Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), name); }
    static Keybindings Load(string name) { return Keybindings.Load(Path.Combine(Artifact("keybinding-fixtures"), name+".dat")); }
    static void SameOtherBindings(Keybindings before, Keybindings after)
    {
        foreach (PlayerCommand command in Enum.GetValues(typeof(PlayerCommand)))
            if(command!=PlayerCommand.NONE&&command!=PlayerCommand.SAVE_GAME&&command!=PlayerCommand.RECALL_FOLLOWERS)
                Check.Equal(before.Get(command), after.Get(command), "preserved binding " + command);
    }
    public static void Register()
    {
        ScenarioRunner.Add("world/keybinding-legacy-defaults",()=>TownScenarioFactory.Arena(5460,"...","...","..."),w=>{
            var original=Load("original-defaults");
            Check.Equal(Keys.S|Keys.Shift,original.Get(PlayerCommand.SAVE_GAME),"original Save is never moved to add recall");
            Check.Equal(Keys.R|Keys.Control,original.Get(PlayerCommand.RECALL_FOLLOWERS),"recall takes a free key");
            var previous=Load("previous-world");
            Check.Equal(Keys.S|Keys.Shift,previous.Get(PlayerCommand.SAVE_GAME),"prior World collision repaired");
            Check.Equal(Keys.R|Keys.Control,previous.Get(PlayerCommand.RECALL_FOLLOWERS),"prior default recall moved");
            var raw=BinarySaveStore.Load<Keybindings>(Path.Combine(Artifact("keybinding-fixtures"),"previous-world.dat"));
            Check.Equal(0,typeof(Keybindings).GetField("m_DefaultsRevision",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(raw),"genuine old schema lacks revision");
            SameOtherBindings(raw,previous);Check.Equal(false,previous.CheckForConflict(),"migrated map is consistent");
        });
        ScenarioRunner.Add("world/keybinding-custom-preservation",()=>TownScenarioFactory.Arena(5461,"...","...","..."),w=>{
            var custom=Load("custom");Check.Equal(Keys.F9,custom.Get(PlayerCommand.SAVE_GAME),"custom save unchanged");
            Check.Equal(Keys.F10,custom.Get(PlayerCommand.RECALL_FOLLOWERS),"custom recall unchanged");
            Check.Equal(PlayerCommand.SHOUT,custom.Get(Keys.R|Keys.Control),"custom occupant retained");
            var previous=Load("previous-world");previous.Set(PlayerCommand.SAVE_GAME,Keys.S|Keys.Control|Keys.Shift);
            previous.Set(PlayerCommand.RECALL_FOLLOWERS,Keys.S|Keys.Shift);
            string path=Artifact("keybinding-versioned-custom.dat");Keybindings.Save(previous,path);
            var reloaded=Keybindings.Load(path);
            Check.Equal(Keys.S|Keys.Control|Keys.Shift,reloaded.Get(PlayerCommand.SAVE_GAME),"intentional versioned Save customization retained");
            Check.Equal(Keys.S|Keys.Shift,reloaded.Get(PlayerCommand.RECALL_FOLLOWERS),"intentional versioned recall customization retained");
            var raw=BinarySaveStore.Load<Keybindings>(Path.Combine(Artifact("keybinding-fixtures"),"previous-world.dat"));
            raw.Set(PlayerCommand.SHOUT,Keys.R|Keys.Control);Keybindings.Save(raw,path);reloaded=Keybindings.Load(path);
            Check.Equal(Keys.S|Keys.Shift,reloaded.Get(PlayerCommand.SAVE_GAME),"original Save restored with occupied recall key");
            Check.Equal(Keys.R|Keys.Control|Keys.Shift,reloaded.Get(PlayerCommand.RECALL_FOLLOWERS),"free fallback chosen");
            Check.Equal(PlayerCommand.SHOUT,reloaded.Get(Keys.R|Keys.Control),"fallback never steals the custom key");
        });
        ScenarioRunner.Add("world/keybinding-inventory-shortcuts",()=>TownScenarioFactory.Arena(5462,"...","...","..."),w=>{
            var loaded=Load("custom-slot");Check.Equal(Keys.R,loaded.Get(PlayerCommand.ITEM_SLOT_0),"custom item slot unchanged");
            Check.Equal(Keys.S|Keys.Control|Keys.Shift,loaded.Get(PlayerCommand.RECALL_FOLLOWERS),"implicit Ctrl/Shift inventory shortcuts are occupied too");
            Check.Equal(true,loaded.CheckForConflict(),"pre-existing Shift+R revival versus custom slot collision is exposed");
            loaded.Set(PlayerCommand.REVIVE_CORPSE,Keys.F8);
            Check.Equal(false,loaded.CheckForConflict(),"recall allocator creates no further implicit collision");
            loaded.Set(PlayerCommand.DISARM_TRAP,Keys.None);loaded.Set(PlayerCommand.ITEM_SLOT_1,Keys.D);
            string path=Artifact("keybinding-inventory-copy.dat");Keybindings.Save(loaded,path);loaded=Keybindings.Load(path);
            Check.Equal(Keys.None,loaded.Get(PlayerCommand.DISARM_TRAP),"all occupied alternatives leave a command unbound");
            Check.Equal(PlayerCommand.ITEM_SLOT_1,loaded.Get(Keys.D),"no inventory key taken by migration");
            loaded.Set(PlayerCommand.RECALL_FOLLOWERS,Keys.D3|Keys.Control);
            Check.Equal(true,loaded.CheckForConflict(),"explicit overlap with implicit equip/drop/throw detected");
        });
        ScenarioRunner.Add("world/keybinding-rebind-confirmation",()=>TownScenarioFactory.Arena(5463,"...","...","..."),w=>{
            RogueGame.KeyBindings.ResetToDefaults();var ui=(ScenarioUI)w.Game.UI;
            ui.QueueWaitKey(Keys.N);Check.Equal(false,Check.Call(w.Game,"TryRebindPlayerCommand",PlayerCommand.RECALL_FOLLOWERS,Keys.S|Keys.Shift),"reject occupied-key reassignment");
            Check.Equal(PlayerCommand.SAVE_GAME,RogueGame.KeyBindings.Get(Keys.S|Keys.Shift),"N preserves Save");
            ui.QueueWaitKey(Keys.Y);Check.Equal(true,Check.Call(w.Game,"TryRebindPlayerCommand",PlayerCommand.RECALL_FOLLOWERS,Keys.S|Keys.Shift),"explicit reassignment allowed");
            Check.Equal(Keys.None,RogueGame.KeyBindings.Get(PlayerCommand.SAVE_GAME),"confirmation explains displaced command is unbound");
            RogueGame.KeyBindings.ResetToDefaults();ui.QueueWaitKey(Keys.Enter);
            Check.Equal(false,Check.Call(w.Game,"TryRebindPlayerCommand",PlayerCommand.RECALL_FOLLOWERS,Keys.D1|Keys.Control),"inventory shortcuts cannot be overwritten in the menu");
            Check.Equal(false,Check.Call(w.Game,"TryRebindPlayerCommand",PlayerCommand.RECALL_FOLLOWERS,Keys.Escape),"Escape cancels without becoming a binding");
            Check.Equal(Keys.R|Keys.Control,RogueGame.KeyBindings.Get(PlayerCommand.RECALL_FOLLOWERS),"cancellation leaves recall intact");
        });
    }
}
