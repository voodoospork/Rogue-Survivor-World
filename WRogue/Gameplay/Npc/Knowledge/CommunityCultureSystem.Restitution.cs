using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Items;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static partial class CommunityCultureSystem
    {
        public static bool HandlesTheft(RogueGame game, XpdBase claim)
        { return game.Session.GamePreset.NpcPersonalitiesEnabled && claim.GroupLeader != null && Eligible(claim.GroupLeader); }
        public static SignificantEvent Theft(RogueGame game, Actor thief, XpdBase claim, Point position, Item item, int units, bool tampered, long cause, string story)
        {
            SocialGroup group = claim.GroupLeader.SocialGroup;
            var witnesses = thief.Location.Map.Actors.Where(a => a != thief && !a.IsDead && !a.IsSleeping && claim.Owns(a) &&
                NpcKnowledgeSystem.Visible(game,a,new Location(thief.Location.Map,position))).ToList();
            string resource = tampered ? "defense" : item is ItemFood ? "food" : item is ItemMedicine ? "medicine" : "item";
            var e = new SignificantEvent(tampered ? "community_defense_tampered" : "base_theft",thief,claim.GroupLeader,thief.Location.Map,position,
                game.Session.WorldTime.TurnCounter,false,causeId:cause,storyId:story) { Units=units,ModelId=item.Model.ID,Resource=resource };
            if (witnesses.Count == 0) return e;
            Ensure(game,witnesses[0]);
            var home=CommunityShelterLocation.At(new Location(thief.Location.Map,claim.Cells.First()));
            int clock=thief.Location.Map.LocalTime.TurnCounter;
            var c=StoredCase(group,thief.PersonalityIdentity,home);
            if(c==null || c.Kind=="trespass" || c.Departed || clock-c.LastIncidentClock>=30)
            { c=new CommunityCase { Episode=game.Session.NextPersonalityEventId(),Person=thief.PersonalityIdentity,PersonName=thief.UnmodifiedName,
                Home=home,Kind=tampered?"defense":"theft",LastIncidentClock=clock };group.Culture.Cases.Add(c);group.Culture.Bound(); }
            c.LastIncidentClock=clock;
            c.UpdateId=game.Session.NextPersonalityEventId();
            // Carrying away an already recorded disabled defense is part of that same repair debt.
            bool recordedDefense=!tampered&&item is ItemTrap&&!((ItemTrap)item).IsActivated&&
                c.Losses.Exists(l=>l.Tampered&&!l.Repaired&&l.ModelId==item.Model.ID&&l.Position==position);
            var loss=c.Losses.Find(l=>l.ModelId==item.Model.ID && l.Tampered==tampered && (!tampered||l.Position==position));
            if(!recordedDefense) {
                if(loss==null) { loss=new CommunityLoss { ModelId=item.Model.ID,Resource=resource,Tampered=tampered,Position=position };c.Losses.Add(loss); }
                loss.Units+=units;
            }
            // Rapid repeated stack operations are one encounter, but its real loss is conserved.
            e.CombatEpisode=c.Episode;
            e.CultureCase=Report(group,c,game.Session.WorldTime.TurnCounter,"witness");
            foreach(Actor witness in witnesses)
            {
                witness.Personality=witness.Personality??new PersonalityState();
                witness.Personality.LearnCase(e.CultureCase);
                var local=LocalCase(witness,group.Identity,thief.PersonalityIdentity);
                var policy=KnownPolicy(witness);
                bool repeat=group.Culture.Cases.Any(old=>old.Person==c.Person && old.Episode!=c.Episode && !old.Resolved && old.HasLoss && witness.Personality.CommunityCases.Contains(old.Episode));
                local.Enforce=policy!=null&&policy.Enforcement>=70 || repeat || c.DemandClock>=0;
                if(local.Enforce)
                {
                    c.Enforce = true;
                    local.WarnedBy=witness.PersonalityIdentity;
                    SpeakCase(game,witness,thief,local,"Stop taking or tampering with " + group.CommunityName + "'s supplies! Leave our home!",true);
                }
                c.UpdateId=Math.Max(c.UpdateId,local.UpdateId);
            }
            e.CultureCase=Report(group,c,game.Session.WorldTime.TurnCounter,"witness");
            return e;
        }
        public static void ObserveEvent(NpcObservation o)
        {
            if(o.Source.CultureCase!=null && o.Owner.SocialGroup!=null && o.Owner.SocialGroup.Identity==o.Source.CultureCase.Community &&
                (o.Direct || o.SeesSubject))
            {
                var report=o.Source.CultureCase.Copy();
                if(report.Case.Attribution==null)report.Case.Attribution=o.DiplomacyEvidence;
                LearnReport(o.Owner,report,o.Direct?"participant":"witness");
                var fact=o.Owner.Personality.Knowledge.Facts.Find(f=>f.EventId==o.Source.Id&&f.Kind==o.Source.Kind);
                if(fact!=null)fact.CultureCase=report.Copy();
            }
            if(o.Source.Kind=="community_restitution_requested"&&o.Source.Other==o.Owner&&NpcIntentSystem.Enabled(o.Owner))
                NpcReplies.Reply(o.Owner,o.Source.Subject,o.Source,
                    PersonalitySystem.Bias(o.Owner,DecisionKind.Law)+PersonalitySystem.Bias(o.Owner,DecisionKind.Compassion)>=10?
                    "community_restitution_promised":"community_restitution_refused",
                    "I'll try to return matching supplies.","I will not replace them.");
            if(o.Source.Kind=="community_restitution_paid" && o.Source.CultureCase!=null) ApplyRepayment(o.Owner,o.Source.CultureCase);
            Experience(o.Owner,o.Source.CombatEpisode==0?o.Source.Id:o.Source.CombatEpisode,o.Source.Kind,o.DiplomacyEvidence,o.Source.CultureCase);
        }
        public static void Hear(Actor observer,NpcFact fact,string source)
        {
            if(observer.IsSleeping||observer.IsDead||fact.Confidence<60)return;
            if(fact.CultureCase!=null) { LearnReport(observer,fact.CultureCase,source); if(fact.Kind=="community_restitution_paid")ApplyRepayment(observer,fact.CultureCase); }
            Experience(observer,fact.DiplomacyEvidence==null?fact.EventId:fact.DiplomacyEvidence.Episode,fact.Kind,fact.DiplomacyEvidence,fact.CultureCase);
        }
        public static void NormalizeEvent(RogueGame game,SignificantEvent source)
        {
            if(source.Kind!="community_restitution_refused"&&source.Kind!="community_restitution_promised")return;
            Actor owner=source.Other;if(owner==null||owner.SocialGroup==null||source.Subject==null)return;
            var c=LocalCase(owner,owner.SocialGroup.Identity,source.Subject.PersonalityIdentity);
            if(c==null||!c.HasLoss||c.RequestId!=source.CauseId)return;
            c.Promised=source.Kind=="community_restitution_promised";c.Refused=!c.Promised;if(c.Refused)c.Enforce=true;
            var stored=owner.SocialGroup.Culture.Cases.Find(x=>x.Episode==c.Episode);
            if(stored!=null)owner.SocialGroup.Culture.Cases.Remove(stored);owner.SocialGroup.Culture.Cases.Add(c.Copy());owner.SocialGroup.Culture.Bound();
            source.CombatEpisode=c.Episode;source.CultureCase=Report(owner.SocialGroup,c,game.Session.WorldTime.TurnCounter,"actual reply");
            SpeakCase(game,owner,source.Subject,c,c.Promised ? "Your promise keeps the same response deadline. Actual replacement is still owed." : "Your refusal leaves the property dispute unresolved.");
        }
        static void Experience(Actor observer,long id,string kind,DiplomacyEvidence evidence,KnownCommunityCase report)
        {
            if(!Eligible(observer)||observer.PersonalityIdentity!=observer.SocialGroup.LeaderId||observer.SocialGroup.Culture==null || id<=0)return;
            Guid group=observer.SocialGroup.Identity;
            if((report==null||report.Community!=group)&& (evidence==null||evidence.Subject.Group!=group&&evidence.Other.Group!=group))return;
            var c=observer.SocialGroup.Culture;if(id<=c.ReviewedEvent||c.Experiences.Contains(id))return;
            int good=kind=="helped"||kind=="promise_kept"?2:0;
            int loss=(kind=="community_killing"||kind=="community_injury")&&evidence!=null&&evidence.Subject.Group==group?2:0;
            int wrong=kind=="base_theft"||kind=="community_defense_tampered"||kind=="community_trespass_defied"||kind=="promise_broken"?1:0;
            if(good+loss+wrong==0)return;
            c.Experiences.Add(id);c.Cooperation=Math.Min(10,c.Cooperation+good);c.Loss=Math.Min(10,c.Loss+loss);c.Violations=Math.Min(10,c.Violations+wrong);c.Bound();
        }
        public static KnownCommunityCase Repayment(Actor actor,Actor target,Item item,RogueGame game)
        { return Repayment(actor,target,item,game.Rules); }
        public static KnownCommunityCase Repayment(Actor actor,Actor target,Item item,Rules rules)
        {
            if(target.SocialGroup==null||target.Personality==null || !Usable(rules,item,actor.Location.Map.LocalTime.TurnCounter))return null;
            var report=target.Personality.CaseReports.LastOrDefault(r=>r.Community==target.SocialGroup.Identity && r.Case.Person==actor.PersonalityIdentity &&
                r.Case.Losses.Exists(l=>!l.Tampered&&l.ModelId==item.Model.ID&&l.Remaining>0));
            if(report==null && actor.Personality!=null)
            {
                // The offender can explain an already learned debt to the current chief in person.
                if(target.PersonalityIdentity==target.SocialGroup.LeaderId)report=actor.Personality.CaseReports.LastOrDefault(r=>r.Community==target.SocialGroup.Identity&&
                    r.Case.Person==actor.PersonalityIdentity&&r.Case.Losses.Exists(l=>!l.Tampered&&l.ModelId==item.Model.ID&&l.Remaining>0));
            }
            return report;
        }
        static bool Usable(Rules rules,Item item,int turn)
        {
            var food=item as ItemFood;if(food!=null)return !rules.IsFoodSpoiled(food,turn);
            var medical=item as ItemMedicine;if(medical!=null)return medical.Healing>0||medical.InfectionCure>0||medical.SleepBoost>0||medical.StaminaBoost>0||medical.SanityCure>0;
            var light=item as ItemLight;if(light!=null)return light.Batteries>0;
            var tracker=item as ItemTracker;if(tracker!=null)return tracker.Batteries>0;
            var scent=item as ItemSprayScent;if(scent!=null)return scent.SprayQuantity>0;
            var paint=item as ItemSprayPaint;if(paint!=null)return paint.PaintQuantity>0;
            return !(item is ItemTrap);
        }
        public static int CompleteGift(RogueGame game,Actor actor,Actor target,Item gift,int received,KnownCommunityCase report)
        {
            if(report==null||received<=0)return 0;
            var c=report.Case.Copy();int repaid=0;
            foreach(var l in c.Losses.Where(l=>!l.Tampered&&l.ModelId==gift.Model.ID))
            { int n=Math.Min(received-repaid,l.Remaining);l.Repaid+=n;repaid+=n;if(repaid==received)break; }
            if(repaid==0)return 0;c.Resolved=!c.HasLoss;if(c.Resolved)c.Enforce=false;
            var known=actor.Personality==null?null:actor.Personality.Knowledge.Person(target.PersonalityIdentity);
            if(known!=null&&gift is ItemFood)known.LossUnits=Math.Max(0,known.LossUnits-repaid);
            var group=target.SocialGroup;var stored=group.Culture==null?null:group.Culture.Cases.Find(x=>x.Episode==c.Episode);
            if(stored!=null){group.Culture.Cases.Remove(stored);group.Culture.Cases.Add(c.Copy());}
            SpeakCase(game,target,actor,c,c.Resolved ? "The recorded supplies have been replaced. That loss is settled; existing feuds and personal disputes still need peace." :
                "Received " + repaid + " replacement unit(s). Still owed: " + DebtText(c) + ".");
            PersonalitySystem.Report(game,new SignificantEvent("community_restitution_paid",actor,target,target.Location.Map,target.Location.Position,
                game.Session.WorldTime.TurnCounter,causeId:c.Episode) { CombatEpisode=c.Episode,Units=repaid,ModelId=gift.Model.ID,
                    CultureCase=Report(group,c,game.Session.WorldTime.TurnCounter,"actual replacement received") });return repaid;
        }
        static void ApplyRepayment(Actor owner,KnownCommunityCase report)
        {
            if(!CommunityDiplomacySystem.IsLeader(owner)||owner.SocialGroup.Identity!=report.Community||owner.SocialGroup.Diplomacy==null)return;
            foreach(var relation in owner.SocialGroup.Diplomacy.Relations)
            {
                var incident=relation.Incidents.Find(i=>i.Episode==report.Case.Episode);if(incident==null)continue;
                int total=report.Case.Losses.Sum(l=>l.Tampered?1:l.Units),remaining=report.Case.Losses.Sum(l=>l.Remaining);
                int credit=total==0?0:15*(total-remaining)/total;
                int delta=Math.Max(0,credit-incident.RestitutionCredit);incident.RestitutionCredit+=delta;
                relation.Grievance=Math.Max(0,relation.Grievance-delta);
                int restore=Math.Min(delta,Math.Max(0,incident.TrustPenalty-incident.TrustRestored));
                incident.TrustRestored+=restore;relation.Trust=Math.Min(100,relation.Trust+restore);
                incident.Resolved=report.Case.Resolved;relation.Revision++;
                owner.Personality.LearnDiplomacy(CommunityDiplomacySystem.ReportedMessage(owner,relation,report.Turn),report.Turn,"actual restitution report",90);
            }
        }
        public static void RepairDefense(RogueGame game,Actor actor,Item dropped)
        {
            var trap=dropped as ItemTrap;var claim=actor.Location.Map.XpdBaseAt(actor.Location.Position);
            if(trap==null||!trap.IsActivated||claim==null||claim.GroupLeader==null||claim.GroupLeader.SocialGroup==null||actor.Personality==null)return;
            var group=claim.GroupLeader.SocialGroup;
            var report=actor.Personality.CaseReports.LastOrDefault(r=>r.Community==group.Identity&&r.Case.Person==actor.PersonalityIdentity&&r.Case.Home.Matches(actor.Location.Map,actor.Location.Position)&&
                r.Case.Losses.Exists(l=>l.Tampered&&!l.Repaired&&l.ModelId==trap.Model.ID&&l.Position==actor.Location.Position));
            if(report==null)return;
            var c=report.Case.Copy();c.Losses.Find(l=>l.Tampered&&!l.Repaired&&l.ModelId==trap.Model.ID&&l.Position==actor.Location.Position).Repaired=true;
            trap.SetBaseOwner(claim);c.Resolved=!c.HasLoss;if(c.Resolved)c.Enforce=false;
            var witness=actor.Location.Map.Actors.FirstOrDefault(a=>!a.IsDead&&!a.IsSleeping&&claim.Owns(a)&&NpcKnowledgeSystem.Visible(game,a,actor.Location));
            if(witness==null)return;
            var stored=group.Culture.Cases.Find(x=>x.Episode==c.Episode);if(stored!=null) { group.Culture.Cases.Remove(stored);group.Culture.Cases.Add(c.Copy()); }
            SpeakCase(game,witness,actor,c,"The defense was actually restored at its original position. This repair settles only its recorded damage.");
            PersonalitySystem.Report(game,new SignificantEvent("community_restitution_paid",actor,witness,actor.Location.Map,actor.Location.Position,
                game.Session.WorldTime.TurnCounter,causeId:c.Episode) { CultureCase=Report(group,c,game.Session.WorldTime.TurnCounter,"witnessed repair") });
        }
    }
}
