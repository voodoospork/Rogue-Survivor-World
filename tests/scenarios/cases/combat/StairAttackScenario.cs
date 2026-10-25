using System.Drawing;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

static class StairAttackScenario
{
    public static void Register()
    {
        Register("neutral-keyboard", false, false, false, false);
        Register("neutral-mouse", false, true, false, false);
        Register("hostile", true, false, false, false);
        Register("tired", false, false, true, false);
        Register("cancel", false, false, false, true);
    }

    static void Register(string name, bool hostile, bool mouse, bool tired, bool cancel)
    {
        ScenarioRunner.Add("combat/stair-attack-" + name, () => TownScenarioFactory.Arena(4530,
            ".....", ".....", "....."), world =>
        {
            world.Map.LocalTime.TurnCounter = 12 * WorldTime.TURNS_PER_HOUR;
            Actor player = SkillScenario.Actor(world);
            player.Controller = new PlayerController();
            world.Place(player, 1, 1);
            world.SetPlayer(player);
            world.Game.ComputeViewRect(player.Location.Position);
            player.CurrentMeleeAttack = Attack.MeleeAttack(new Verb("hit"), 1000, 5, 0, 0);
            if (tired) player.StaminaPoints = 0;

            Actor target = SkillScenario.Actor(world, hostile);
            target.HitPoints = 100;
            world.Map.RemoveActor(target);
            Map landing = new Map(4531, "upper landing", 5, 3);
            landing.LocalTime.TurnCounter = world.Map.LocalTime.TurnCounter;
            for (int y = 0; y < landing.Height; y++)
                for (int x = 0; x < landing.Width; x++)
                    landing.SetTileModelAt(x, y, world.Game.GameTiles.FLOOR_ASPHALT);
            world.Map.District.AddUniqueMap(landing);
            Point destination = new Point(3, 1);
            landing.PlaceActorAt(target, destination);
            world.Map.SetExitAt(player.Location.Position, new Exit(landing, destination));
            landing.SetExitAt(destination, new Exit(world.Map, player.Location.Position));
            Check.Equal(hostile, world.Game.Rules.AreEnemies(player, target), "initial hostility");

            RogueGame.KeyBindings.ResetToDefaults();
            ScenarioUI ui = (ScenarioUI)world.Game.UI;
            ui.ContinueImportantMessages = true;
            if (!cancel)
            {
                if (mouse)
                {
                    Point screen = (Point)Check.Call(world.Game, "MapToScreen", player.Location.Position);
                    screen.Offset(1, 1);
                    ui.MousePosition = screen;
                    ui.QueueClick(MouseButtons.Left);
                }
                else ui.QueueKey(Keys.NumPad5);
            }
            // Queued keyboard input takes priority over clicks in the real reader.
            if (!mouse) ui.QueueKey(Keys.Escape);
            int ap = player.ActionPoints, stamina = player.StaminaPoints, hp = target.HitPoints;
            bool attacked = !tired && !cancel;
            Check.Equal(attacked, Check.Call(world.Game, "HandlePlayerBreak", player),
                "explicit break command attacks the stair blocker when legal");
            Check.Equal(ap - (attacked ? Rules.BASE_ACTION_COST : 0), player.ActionPoints,
                "only a completed attack spends action points");
            Check.Equal(stamina - (attacked ? Rules.STAMINA_COST_MELEE_ATTACK : 0), player.StaminaPoints,
                "normal melee stamina cost");
            Check.Equal(attacked, target.HitPoints < hp, "only a completed attack damages the blocker");
            if (!hostile)
            {
                Check.Equal(attacked, player.IsAggressorOf(target), "normal aggression consequences");
                Check.Equal(attacked, target.IsSelfDefenceFrom(player), "blocker retains self defense");
            }
            Check.Same(world.Map, player.Location.Map, "attacking does not move the player upstairs");
            Check.Same(landing, target.Location.Map, "blocker stays on its own landing");
        });
    }
}
