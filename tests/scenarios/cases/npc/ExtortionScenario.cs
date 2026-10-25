using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

static class ExtortionScenario
{
    static Actor robber, victim, member;
    static void Equip(ScenarioWorld w, Actor a, bool gun = false)
    {
        Item item = gun ? (Item)new ItemRangedWeapon(w.Game.GameItems.PISTOL) { Ammo = 3 } : new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT);
        Check.Equal(true, a.Inventory.AddAll(item), "fixture weapon fits"); w.Game.DoEquipItem(a, item);
    }
    static ScenarioWorld Rob(int seed)
    {
        var w = CommunityAdaptationScenario.Arena(seed);
        robber = NpcIntentSupport.Actor(w, "Robber", 3, 4); victim = NpcIntentSupport.Actor(w, "Victim", 4, 4);
        Equip(w, robber, true); NpcIntentSupport.Food(w, victim, 4); CommunityMembershipScenario.Fov(w); return w;
    }
    static ActorAction Turn(ScenarioWorld w, Actor a)
    { a.ActionPoints = Rules.BASE_ACTION_COST; var action = a.Controller.GetAction(w.Game);
        Check.Equal(true, action != null && w.Try(action), "ordinary controller performs legal action"); return action; }
    static void Demand(ScenarioWorld w, RobberyGoods goods = RobberyGoods.Food)
    { robber.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionRobbery(robber, w.Game, victim, goods)), "production robbery is legal");
        Check.Equal(0, robber.ActionPoints, "demand costs one action"); }
    static void Answer(ScenarioWorld w, bool yes)
    { victim.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionAnswerRobbery(victim, w.Game, yes)), "production robbery answer is legal");
        Check.Equal(0, victim.ActionPoints, "answer costs one action"); }
    static void Know(ScenarioWorld w, Actor observer, Actor chief)
    {
        var known = observer.Personality.LearnCommunity(chief.SocialGroup.Identity, chief.SocialGroup.CommunityName, 360, "controlled observed introduction");
        known.Leader = chief.UnmodifiedName; known.LeaderTurn = 360; known.LeaderSource = "controlled observed introduction";
        foreach (var a in chief.CommunityResidents)
            known.Member(new CommunityMember { Id = a.PersonalityIdentity, Name = a.UnmodifiedName, Status = "seen alive", Turn = 360, Source = "controlled observed introduction" });
    }
    static ScenarioWorld Tribute(int seed)
    {
        var w = CommunityAdaptationScenario.Arena(seed);
        var group = CommunityAdaptationScenario.Found(w, out robber, out member);
        group.Culture.Aggression = CommunityAggression.Raiders; group.Culture.AggressionInitialized = true; group.Culture.Enforcement = 80;
        w.Game.Player.IsSleeping = true; group.Culture.Revision++; CommunityCultureSystem.SayPolicy(w.Game, robber, member, true); w.Game.Player.IsSleeping = false;
        Equip(w, robber, true); Equip(w, member); NpcIntentSupport.Food(w, robber, 3);
        victim = NpcIntentSupport.Actor(w, "Neighbour chief", 6, 4);
        victim.AddFollower(NpcIntentSupport.Actor(w, "Neighbour resident", 7, 4));
        victim.SocialGroup.CommunityName = "Neighbour refuge";
        victim.SocialGroup.Home = new CommunityHomeState { Place = CommunityShelterLocation.At(victim.Location) };
        CommunityCultureSystem.Initialize(w.Game, victim.SocialGroup, victim.PartyTree().ToArray());
        victim.SocialGroup.Culture.Aggression = CommunityAggression.Guarded;
        NpcIntentSupport.Food(w, victim, 8);
        Know(w, robber, victim); Know(w, robber, robber); Know(w, victim, robber); Know(w, victim, victim);
        w.Map.PlaceActorAt(robber, new Point(5, 5)); CommunityMembershipScenario.Fov(w); return w;
    }
    static void TributeDemand(ScenarioWorld w)
    { robber.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionTribute(robber, w.Game, victim)), "production tribute demand legal");
        Check.Equal(0, robber.ActionPoints, "tribute demand costs an action"); }
    static CommunityTribute Ledger() { return robber.SocialGroup.Tributes.Last(); }
    static void TributeAnswer(ScenarioWorld w, bool yes)
    { victim.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, w.Try(new ActionTribute(victim, w.Game, robber, true, yes)), "production tribute answer legal");
        Check.Equal(0, victim.ActionPoints, "tribute answer costs an action"); }
    static string Save(ScenarioWorld w, string name)
    { string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), name + ".dat"); BinarySaveStore.Save(path, Session.Get); return path; }
    public static void Register()
    {
        ScenarioRunner.Add("npc/robbery-surrender", () => Rob(6000), w => {
            int hp = victim.HitPoints, total = NpcIntentSupport.FoodUnits(victim) + NpcIntentSupport.FoodUnits(robber);
            Demand(w); Check.Equal(hp, victim.HitPoints, "demand inflicts no free damage");
            Check.Equal(true, victim.IsSelfDefenceFrom(robber), "victim may defend against coercion");
            Check.Equal(true, Turn(w, victim) is ActionAnswerRobbery, "victim independently answers before combat");
            Check.Equal(2, NpcIntentSupport.FoodUnits(robber), "actual requested quantity changes hands");
            Check.Equal(total, NpcIntentSupport.FoodUnits(victim) + NpcIntentSupport.FoodUnits(robber), "food conserved");
            Check.Equal(false, robber.IsAggressorOf(victim), "compliance ends new personal threat");
            Check.Equal(null, robber.Robbing, "no repeated demand state");
            Check.Equal(false, NpcIntentSupport.HasEvent(robber, "helped"), "coercion earns no aid reward");
            Check.Equal(false, new ActionRobbery(robber, w.Game, victim, RobberyGoods.Food).IsLegal(), "half-day cooldown prevents repeat farming");
        });
        ScenarioRunner.Add("npc/robbery-refusal-combat", () => Rob(6001), w => {
            Demand(w); Answer(w, false);
            Check.Equal(true, robber.IsAggressorOf(victim), "refused armed robber remains aggressor");
            Check.Equal(true, robber.Predation != null, "favorable refusal starts bounded real hunt");
            Check.Equal(4, NpcIntentSupport.FoodUnits(victim), "refusal transfers nothing");
            int hp = victim.HitPoints;
            for (int n = 0; n < 10 && victim.HitPoints == hp; n++) Turn(w, robber);
            Check.Equal(true, victim.IsDead || victim.HitPoints < hp, "ordinary combat can follow refusal");
        });
        ScenarioRunner.Add("npc/robbery-weapon-transfer", () => Rob(6002), w => {
            Equip(w, victim); Item weapon = victim.GetEquippedWeapon();
            Demand(w, RobberyGoods.Weapon); Answer(w, true);
            Check.Equal(null, victim.GetEquippedWeapon(), "weapon surrender unequips through normal hook");
            Check.Equal(true, robber.Inventory.Contains(weapon), "same real weapon changes owner");
            Check.Equal(false, weapon.IsEquipped, "victim gear is not silently equipped by robber");
        });
        ScenarioRunner.Add("npc/robbery-hidden-and-visible-gear", () => Rob(6003), w => {
            int before = RobberySystem.VisibleStrength(w.Game, victim);
            var gun = new ItemRangedWeapon(w.Game.GameItems.PISTOL) { Ammo = 0 }; victim.Inventory.AddAll(gun);
            Check.Equal(before, RobberySystem.VisibleStrength(w.Game, victim), "hidden backpack gun gives no information");
            w.Game.DoEquipItem(victim, gun); int armed = RobberySystem.VisibleStrength(w.Game, victim);
            Check.Equal(true, armed > before, "equipped gun deters robbery"); gun.Ammo = 8;
            Check.Equal(armed, RobberySystem.VisibleStrength(w.Game, victim), "hidden enemy ammunition is not read");
            var own = (ItemRangedWeapon)robber.GetEquippedWeapon(); own.Ammo = 0;
            Check.Equal(false, new ActionRobbery(robber, w.Game, victim, RobberyGoods.Food).IsLegal(), "own empty gun cannot threaten armed victim");
        });
        ScenarioRunner.Add("npc/robbery-capacity", () => Rob(6004), w => {
            robber.Inventory = new Inventory(0); // Controlled capacity fixture.
            Demand(w); Answer(w, true);
            Check.Equal(4, NpcIntentSupport.FoodUnits(victim), "full recipient destroys no goods");
            Check.Equal(0, NpcIntentSupport.FoodUnits(robber), "full recipient receives no fictional loot");
        });
        ScenarioRunner.Add("npc/robbery-expiry-and-entrusted", () => Rob(6005), w => {
            foreach (var food in victim.Inventory.Items.ToArray()) victim.Inventory.RemoveAllQuantity(food);
            var spoiled = new ItemFood(w.Game.GameItems.GROCERIES, 0); victim.Inventory.AddAll(spoiled);
            var cargo = NpcIntentSupport.Food(w, victim, 3); cargo.TradeCargo = new TradeCargoTag { Shipment = Guid.NewGuid() };
            Demand(w); Answer(w, true);
            Check.Equal(0, NpcIntentSupport.FoodUnits(robber), "spoiled goods and entrusted freight cannot satisfy robbery");
            Check.Equal(true, victim.Inventory.Contains(cargo) && cargo.Quantity == 3, "custody lot intact");
        });
        ScenarioRunner.Add("npc/robbery-flee-and-expire", () => Rob(6006), w => {
            Demand(w); w.Map.PlaceActorAt(victim, new Point(22, 4));
            RobberySystem.Update(w.Game, robber); RobberySystem.Update(w.Game, victim);
            Check.Equal(null, robber.Robbing, "leaving ends face-to-face demand"); Check.Equal(null, victim.BeingRobbed, "victim is free of stale input");
            Check.Equal(null, robber.Predation, "unseen escape creates no tracking objective");
            Check.Equal(4, NpcIntentSupport.FoodUnits(victim), "escape transfers no goods");
        });
        ScenarioRunner.Add("npc/robbery-timeout", () => Rob(6007), w => {
            Demand(w); CommunityAdaptationScenario.Time(w, 360 + RobberySystem.ResponseTurns);
            RobberySystem.Update(w.Game, robber);
            Check.Equal(null, robber.Robbing, "response window bounded"); Check.Equal(null, victim.BeingRobbed, "timeout clears local paired demand");
            Check.Equal(true, robber.Predation != null, "visible strong robber can escalate unanswered threat");
        });
        ScenarioRunner.Add("npc/robbery-saved-demand", () => Rob(6008), w => {
            Demand(w); Guid aid = robber.PersonalityIdentity, bid = victim.PersonalityIdentity;
            w = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(Save(w, "robbery-pending")));
            robber = NpcIntentSupport.Find(w.Map, aid); victim = NpcIntentSupport.Find(w.Map, bid);
            Check.Equal(robber.Robbing.Id, victim.BeingRobbed.Id, "saved demand identity preserved");
            Answer(w, true); Check.Equal(2, NpcIntentSupport.FoodUnits(robber), "real transfer works after reload");
        });
        ScenarioRunner.Add("npc/robbery-player-menu", () => Rob(6009), w => {
            robber.Controller = new PlayerController(); w.SetPlayer(robber); CommunityMembershipScenario.Fov(w);
            var ui = (ScenarioUI)w.Game.UI; ui.QueueWaitKey(Keys.D7); ui.QueueWaitKey(Keys.D1);
            robber.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, Check.Call(w.Game, "HandlePlayerTalk", robber), "V/7 invokes production robbery menu");
            Check.Equal(true, victim.BeingRobbed != null, "player creates real pending demand");
            Check.Equal(0, robber.ActionPoints, "player threat costs one turn");
        });
        ScenarioRunner.Add("npc/robbery-player-answer-cancel", () => Rob(6010), w => {
            victim.Controller = new PlayerController(); w.SetPlayer(victim); CommunityMembershipScenario.Fov(w);
            var longWait = typeof(RogueGame).GetField("m_IsPlayerLongWait", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var stopped = typeof(RogueGame).GetField("m_IsPlayerLongWaitForcedStop", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            longWait.SetValue(w.Game, true);
            Demand(w); Check.Equal(true, stopped.GetValue(w.Game), "demand interrupts player's long wait for a real response");
            longWait.SetValue(w.Game, false);
            var ui = (ScenarioUI)w.Game.UI; ui.QueueWaitKey(Keys.Escape); victim.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(false, Check.Call(w.Game, "HandlePlayerTalk", victim), "player can cancel answer and fight or flee");
            Check.Equal(Rules.BASE_ACTION_COST, victim.ActionPoints, "cancel costs no action");
            Check.Equal(4, NpcIntentSupport.FoodUnits(victim), "player inventory is never taken without consent");
            ui.QueueWaitKey(Keys.Y); Check.Equal(true, Check.Call(w.Game, "HandlePlayerTalk", victim), "V answers threat even though robber is now an enemy");
            Check.Equal(2, NpcIntentSupport.FoodUnits(victim), "only accepted requested quantity is handed over");
        });
        ScenarioRunner.Add("npc/robbery-desperation-and-controller", () => Tribute(6011), w => {
            foreach (var food in robber.Inventory.Items.OfType<ItemFood>().ToArray()) robber.Inventory.RemoveAllQuantity(food);
            Check.Equal(true, RobberySystem.Autonomous(w.Game, robber, victim), "low-stock raider considers robbery");
            NpcIntentSupport.Food(w, robber, 4); Check.Equal(false, RobberySystem.Autonomous(w.Game, robber, victim), "well-stocked healthy raider has no robbery need");
            foreach (var food in robber.Inventory.Items.OfType<ItemFood>().ToArray()) robber.Inventory.RemoveAllQuantity(food);
            // Isolate personal robbery from chief-to-chief tribute.
            victim.SocialGroup.Home = null;
            ActorAction action = null;
            for (int n = 0; n < 4 && robber.Robbing == null; n++) action = Turn(w, robber);
            Check.Equal(true, action is ActionRobbery, "ordinary raider threatens before attacking");
            Check.Equal(true, victim.BeingRobbed != null, "ordinary controller creates victim response window");
        });
        ScenarioRunner.Add("npc/tribute-agreement-payment", () => Tribute(6020), w => {
            int total = NpcIntentSupport.FoodUnits(robber) + NpcIntentSupport.FoodUnits(victim);
            TributeDemand(w); Check.Equal(false, Ledger().Accepted, "demand is not automatic agreement");
            Check.Equal(false, Object.ReferenceEquals(Ledger(), victim.SocialGroup.Tributes.Single()), "two communities keep independent records");
            Check.Equal(true, Turn(w, victim) is ActionTribute, "ordinary chief independently answers tribute demand");
            Check.Equal(true, Ledger().Accepted, "outmatched chief agrees"); Check.Equal(360 + WorldTime.TURNS_PER_DAY, Ledger().Due, "next daily installment dated");
            Check.Equal(total, NpcIntentSupport.FoodUnits(robber) + NpcIntentSupport.FoodUnits(victim), "tribute conserves real goods");
            Check.Equal(true, CommunityTributeSystem.Truce(robber, victim), "compliance buys stated truce");
            Check.Equal(false, RobberySystem.Autonomous(w.Game, robber, victim), "collector does not also rob payer");
            Check.Equal(false, new ActionTribute(victim, w.Game, robber, true).IsLegal(), "cannot collect twice before next day");
        });
        ScenarioRunner.Add("npc/tribute-partial-and-recurring", () => Tribute(6021), w => {
            TributeDemand(w); var food = victim.Inventory.Items.OfType<ItemFood>().Single(); food.Quantity = 2;
            TributeAnswer(w, true); Check.Equal(true, Ledger().Remaining > 0, "partial payment retains balance");
            Check.Equal(1, NpcIntentSupport.FoodUnits(victim), "chief retains last food unit");
            int remaining = Ledger().Remaining; NpcIntentSupport.Food(w, victim, remaining + 4); TributeAnswer(w, true);
            int paidDue = Ledger().Due; Check.Equal(360 + WorldTime.TURNS_PER_DAY, paidDue, "full partial settlement schedules next day");
            CommunityAdaptationScenario.Time(w, paidDue); int before = NpcIntentSupport.FoodUnits(robber); TributeAnswer(w, true);
            Check.Equal(before + Ledger().Units, NpcIntentSupport.FoodUnits(robber), "new installment physically paid");
            Check.Equal(paidDue + WorldTime.TURNS_PER_DAY, Ledger().Due, "recurring period advances once");
        });
        ScenarioRunner.Add("npc/tribute-refusal-feud", () => Tribute(6022), w => {
            TributeDemand(w); int total = NpcIntentSupport.FoodUnits(victim); TributeAnswer(w, false);
            Check.Equal(true, Ledger().Ended, "chief may refuse economic submission"); Check.Equal(total, NpcIntentSupport.FoodUnits(victim), "refusal transfers nothing");
            Check.Equal(true, robber.SocialGroup.Diplomacy.Find(PoliticalIdentity.Of(victim.SocialGroup)).Feud, "forceful collector declares actual feud");
            Check.Equal(true, victim.Personality.FindDiplomacy(robber.SocialGroup.Identity, PoliticalIdentity.Of(victim.SocialGroup)).Message.Feud, "present payer hears declaration");
        });
        ScenarioRunner.Add("npc/tribute-gear-and-knowledge", () => Tribute(6023), w => {
            Check.Equal(true, CommunityTributeSystem.CanDemand(w.Game, robber, victim), "visible force superiority permits demand");
            victim.Personality.Communities.Clear(); // Collector does not gain opponent's hidden knowledge.
            Equip(w, victim, true); Equip(w, victim.Followers.First(), true);
            Check.Equal(false, CommunityTributeSystem.CanDemand(w.Game, robber, victim), "visibly stronger neighbour deters tribute");
            w.Game.DoUnequipItem(victim, victim.GetEquippedWeapon()); w.Game.DoUnequipItem(victim.Followers.First(), victim.Followers.First().GetEquippedWeapon());
            Check.Equal(true, CommunityTributeSystem.CanDemand(w.Game, robber, victim), "backpack guns are not detected");
            robber.Personality.Communities.Remove(robber.Personality.FindCommunity(victim.SocialGroup.Identity));
            Check.Equal(false, CommunityTributeSystem.CanDemand(w.Game, robber, victim), "unknown affiliation supplies no tribute target");
        });
        ScenarioRunner.Add("npc/tribute-no-remote-tax", () => Tribute(6024), w => {
            TributeDemand(w); TributeAnswer(w, true); int due = Ledger().Due;
            int own = NpcIntentSupport.FoodUnits(robber), other = NpcIntentSupport.FoodUnits(victim);
            w.Map.PlaceActorAt(victim, new Point(22, 4)); CommunityAdaptationScenario.Time(w, due + CommunityTributeSystem.CollectionGrace + 100);
            CommunityTributeSystem.Select(w.Game, robber, new Actor[0]);
            Check.Equal(own, NpcIntentSupport.FoodUnits(robber), "unseen community pays no remote goods");
            Check.Equal(other, NpcIntentSupport.FoodUnits(victim), "absent payer stock unchanged");
            Check.Equal(false, Ledger().Ended, "clock alone does not create telepathic refusal or feud");
            Check.Equal(false, new ActionTribute(victim, w.Game, robber, true).IsLegal(), "distant payment impossible");
        });
        ScenarioRunner.Add("npc/tribute-missed-collection", () => Tribute(6025), w => {
            TributeDemand(w); TributeAnswer(w, true); int due = Ledger().Due;
            CommunityAdaptationScenario.Time(w, due + CommunityTributeSystem.CollectionGrace);
            robber.ActionPoints = Rules.BASE_ACTION_COST;
            var action = CommunityTributeSystem.Select(w.Game, robber, new[] { victim });
            Check.Equal(false, Ledger().Ended, "selecting an action does not resolve agreement");
            Check.Equal(true, action != null && w.Try(action), "physical collection expiry is an executed action");
            Check.Equal(true, Ledger().Ended, "missed installment may end agreement on contact");
        });
        ScenarioRunner.Add("npc/tribute-saved-accounts", () => Tribute(6026), w => {
            TributeDemand(w); TributeAnswer(w, true); Guid aid = robber.PersonalityIdentity, bid = victim.PersonalityIdentity;
            w = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(Save(w, "tribute-agreement")));
            robber = NpcIntentSupport.Find(w.Map, aid); victim = NpcIntentSupport.Find(w.Map, bid);
            Check.Equal(Ledger().Id, victim.SocialGroup.Tributes.Single().Id, "format-5 agreement survives reload");
            Check.Equal(false, Object.ReferenceEquals(Ledger(), victim.SocialGroup.Tributes.Single()), "saved accounts remain independent");
            Check.Equal(true, CommunityTributeSystem.Truce(robber, victim), "saved truce survives reload");
        });
        ScenarioRunner.Add("npc/tribute-autonomous-and-bounded", () => Tribute(6027), w => {
            Check.Equal(true, Turn(w, robber) is ActionTribute, "ordinary chief initiates tribute action");
            Check.Equal(true, victim.SocialGroup.Tributes.Count == 1, "single incoming account");
            Check.Equal(false, new ActionTribute(robber, w.Game, victim).IsLegal(), "duplicate demands blocked");
            ((OrderableAI)victim.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, victim.Location));
            Check.Equal(false, new ActionTribute(victim, w.Game, robber, true).IsLegal(), "personal orders prevent autonomous community payment");
        });
        ScenarioRunner.Add("npc/robbery-community-victim", () => Tribute(6030), w => {
            Demand(w); Check.Equal(true, Turn(w, victim) is ActionAnswerRobbery, "unarmed resident can surrender before generic armed-threat flight");
            Check.Equal(null, victim.BeingRobbed, "community victim completes response");
        });
        ScenarioRunner.Add("npc/robbery-starvation-risk", () => Tribute(6031), w => {
            victim.SocialGroup.Home = null;
            foreach (var food in robber.Inventory.Items.OfType<ItemFood>().ToArray()) robber.Inventory.RemoveAllQuantity(food);
            Equip(w, victim, true); w.Map.PlaceActorAt(victim.Followers.First(), new Point(22, 4));
            Check.Equal(false, RobberySystem.Autonomous(w.Game, robber, victim), "low reserves alone do not justify equal armed opposition");
            robber.FoodPoints = 0;
            Check.Equal(true, RobberySystem.Autonomous(w.Game, robber, victim), "starvation raises tolerated risk without knowing victim ammo");
        });
        ScenarioRunner.Add("npc/robbery-violence-retains-blame", () => Rob(6032), w => {
            Demand(w); robber.HitPoints--;
            Check.Equal(false, new ActionAnswerRobbery(victim, w.Game, true).IsLegal(), "combat interrupts peaceful surrender window");
            RobberySystem.Update(w.Game, robber);
            Check.Equal(true, robber.IsAggressorOf(victim), "later injury cannot erase original aggressor status");
            Check.Equal(4, NpcIntentSupport.FoodUnits(victim), "interrupted demand takes no goods");
        });
        ScenarioRunner.Add("npc/tribute-grace-and-report", () => Tribute(6033), w => {
            TributeDemand(w); victim.Inventory.Items.OfType<ItemFood>().Single().Quantity = 2; TributeAnswer(w, true);
            Check.Equal(null, CommunityTributeSystem.Select(w.Game, victim, new[] { robber }), "short payer may gather supplies during promised grace");
            w.Map.PlaceActorAt(w.Game.Player, new Point(4, 5)); CommunityMembershipScenario.Fov(w);
            var ui = (ScenarioUI)w.Game.UI; ui.QueueWaitKey(Keys.D5); w.Game.Player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, Check.Call(w.Game, "HandleCommunityRelations", w.Game.Player, robber), "V relations offers actual tribute report");
            Check.Equal(true, w.Game.Player.Personality.FindCommunity(robber.SocialGroup.Identity).Notes.Any(n =>
                n.Kind == "community_tribute" && n.Reported && n.Text.Contains("Account updated turn")), "dated reported account enters player journal");
        });
        ScenarioRunner.Add("npc/tribute-unanswered-expiry", () => Tribute(6034), w => {
            TributeDemand(w); CommunityAdaptationScenario.Time(w, 360 + CommunityTributeSystem.ResponseTurns);
            Check.Equal(false, new ActionTribute(victim, w.Game, robber, true).IsLegal(), "unanswered terms cannot be accepted after deadline");
            robber.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, w.Try(new ActionTribute(robber, w.Game, victim, expire: true)), "collector can end expired unanswered demand");
            Check.Equal(true, Ledger().Ended, "unanswered demand retires");
            Check.Equal(false, robber.SocialGroup.Diplomacy != null && robber.SocialGroup.Diplomacy.Relations.Any(r => r.Feud), "silence alone is not refusal or instant feud");
        });
        ScenarioRunner.Add("npc/tribute-visible-approach", () => Tribute(6035), w => {
            w.Map.PlaceActorAt(robber, new Point(3, 4)); w.Map.PlaceActorAt(victim, new Point(7, 6));
            Check.Equal(false, new ActionTribute(robber, w.Game, victim).IsLegal(), "tribute requires actual contact");
            for (int n = 0; n < 12 && robber.SocialGroup.Tributes.Count == 0; n++) Turn(w, robber);
            Check.Equal(true, robber.SocialGroup.Tributes.Any(), "chief walks to visible weaker chief and delivers demand");
            Check.Equal(true, w.Game.Rules.GridDistance(robber.Location.Position, victim.Location.Position) <= 1, "no remote demand");
        });
    }
}
