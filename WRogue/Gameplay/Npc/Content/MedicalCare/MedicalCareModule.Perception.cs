using System;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;
namespace djack.RogueSurvivor.Gameplay.Personality
{
    sealed partial class MedicalCareModule
    {
        static void PerceiveItems(NpcPerceptionContext c)
        { ScavengingSystem.ObserveNpc(c, "medicine"); }
    }
}
