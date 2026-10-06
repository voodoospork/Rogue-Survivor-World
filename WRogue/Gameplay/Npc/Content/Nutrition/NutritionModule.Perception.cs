using System;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;
namespace djack.RogueSurvivor.Gameplay.Personality
{
    sealed partial class NutritionModule
    {
        static void PerceiveItems(NpcPerceptionContext c)
        { ScavengingSystem.ObserveNpc(c, "food"); }
    }
}
