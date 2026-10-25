using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        internal void InterruptForRobbery(Actor victim)
        { if (victim.IsPlayer && m_IsPlayerLongWait) m_IsPlayerLongWaitForcedStop = true; }
        bool HandleRobberyAnswer(Actor player)
        {
            var robber = RobberySystem.Robber(this, player); if (robber == null) return false;
            AddCommunitySpeech(robber.UnmodifiedName + " demands " + RobberySystem.Goods(player.BeingRobbed) +
                ". Y: surrender available goods   N: refuse   Esc: choose another action.", Color.Orange);
            RedrawPlayScreen();
            while (true)
            {
                Keys key = m_UI.UI_WaitKey().KeyCode;
                if (key == Keys.Escape) return false;
                if (key != Keys.Y && key != Keys.N) continue;
                var answer = new ActionAnswerRobbery(player, this, key == Keys.Y);
                if (!answer.IsLegal()) return false;
                answer.Perform(); return true;
            }
        }
        bool HandlePlayerRobbery(Actor player, Actor target)
        {
            AddCommunitySpeech("ROB " + target.UnmodifiedName + " - this is a threat of violence. One turn to demand; no guaranteed surrender.", Color.Orange);
            AddCommunitySpeech("1: Demand food (up to 2 units)   2: Healing medicine (1 unit)   3: Equipped weapon   Esc: cancel", Color.Orange);
            RedrawPlayScreen();
            while (true)
            {
                Keys key = m_UI.UI_WaitKey().KeyCode; if (key == Keys.Escape) return false;
                RobberyGoods goods;
                if (key == Keys.D1 || key == Keys.NumPad1) goods = RobberyGoods.Food;
                else if (key == Keys.D2 || key == Keys.NumPad2) goods = RobberyGoods.Medicine;
                else if (key == Keys.D3 || key == Keys.NumPad3) goods = RobberyGoods.Weapon;
                else continue;
                var action = new ActionRobbery(player, this, target, goods);
                if (!action.IsLegal()) { AddMessage(MakeErrorMessage("Cannot make that demand now. You need a ready weapon to threaten an armed person.")); return false; }
                action.Perform(); return true;
            }
        }
    }
}
