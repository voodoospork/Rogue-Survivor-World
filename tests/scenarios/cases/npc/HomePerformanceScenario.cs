using System;
using System.Collections.Generic;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Gameplay;

// This identical fixture also compiles against the preserved installed membership build.
static class HomePerformanceScenario
{
    public static void Register()
    {
        ScenarioRunner.Add("npc/home-responsiveness",()=>TownScenarioFactory.Arena(4985,new string('.',40),new string('.',40),new string('.',40),new string('.',40),new string('.',40),new string('.',40)),w=>{
            Session.Get.GamePreset=GamePreset.World(); Session.Get.GamePreset.InitialCommunities=true;
            var organic=typeof(GamePreset).GetField("OrganicCommunities"); if(organic!=null) organic.SetValue(Session.Get.GamePreset,false);
            NpcIntentSupport.Player(w,35,5);
            Actor leader=NpcIntentSupport.Actor(w,"Leader",4,2),member=NpcIntentSupport.Actor(w,"Resident",5,2);
            leader.AddFollower(member); leader.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
            var cells=new List<Point>(); for(int x=3;x<9;x++) for(int y=1;y<5;y++) { cells.Add(new Point(x,y)); w.Map.GetTileAt(x,y).IsInside=true; }
            w.Map.AddZone(new Zone("Home",new Rectangle(3,1,6,4)) { BuildingKind=BuildingKind.House }); w.Map.AddXpdBase(new XpdBase(leader,cells));
            w.Game.InitializeCommunityHome(leader); w.Game.ScheduleCommunityWatch(leader);
            PerformanceBenchmarks.Measure("active-home civilian selection, two residents",1000,()=>member.Controller.GetAction(w.Game));
        });
    }
}
