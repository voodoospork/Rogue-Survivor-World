using System;
using System.Drawing;
using System.IO;
using System.Linq;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityAggressionScenario
{
    static Actor chief, member, victim;
    static ScenarioWorld Setup(int seed, CommunityAggression aggression = CommunityAggression.Raiders)
    {
        var w = CommunityAdaptationScenario.Arena(seed);
        var group = CommunityAdaptationScenario.Found(w, out chief, out member);
        group.Culture.Aggression = aggression; group.Culture.AggressionInitialized = true;
        // Isolate the founding briefing from later first-contact tests.
        w.Game.Player.IsSleeping = true;
        group.Culture.Revision++; CommunityCultureSystem.SayPolicy(w.Game, chief, member, true);
        w.Game.Player.IsSleeping = false;
        foreach (var food in chief.Inventory.Items.OfType<ItemFood>().ToArray()) chief.Inventory.RemoveAllQuantity(food);
        var bat = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT); chief.Inventory.AddAll(bat); w.Game.DoEquipItem(chief, bat);
        victim = NpcIntentSupport.Actor(w, "Outsider", 7, 4);
        CommunityMembershipScenario.Fov(w); return w;
    }
    static ActorAction Turn(ScenarioWorld w, Actor actor)
    {
        actor.ActionPoints = Rules.BASE_ACTION_COST; var action = actor.Controller.GetAction(w.Game);
        Check.Equal(true, action != null && w.Try(action), "ordinary controller selects a legal action"); return action;
    }
    static void Begin(ScenarioWorld w)
    { chief.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, w.Try(new ActionCommunityAggression(chief, w.Game, victim)), "production predation begins");
        Check.Equal(0, chief.ActionPoints, "attack commitment spends exactly one action"); }
    static void Unarm(ScenarioWorld w, Actor actor)
    {
        foreach (var item in actor.Inventory.Items.Where(i => i is ItemMeleeWeapon || i is ItemRangedWeapon || i is ItemGrenade).ToArray()) {
            if (item.IsEquipped) w.Game.DoUnequipItem(actor, item);
            actor.Inventory.RemoveAllQuantity(item);
        }
    }
    static string Heard(Actor actor)
    { return String.Join("\n", actor.Personality.HeardJournal.Select(e => e.Text)); }
    static void Introduction(ScenarioWorld w)
    {
        var player = w.Game.Player; w.Map.PlaceActorAt(player, new Point(7, 6)); CommunityMembershipScenario.Fov(w);
        CommunityEncounterSystem.ObserveContact(w.Game, chief, new[] { player });
        chief.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, w.Try(new ActionCommunityCall(chief, w.Game, player)), "production introduction is legal");
        var known = player.Personality.FindCommunity(chief.SocialGroup.Identity);
        Check.Equal(true, known != null && known.Policy != null, "introduction teaches the spoken claim");
        Check.Equal(false, known.Policy.AggressionKnown, "introduction does not disclose hidden aggression");
        Check.Equal(false, Heard(player).Contains("attack all outsiders") || Heard(player).Contains("vulnerable outsiders"), "heard introduction contains no predatory confession");
        Check.Equal(true, CommunityAggressionSystem.PredatoryPolicy(chief), "cover does not change real policy");
    }
    public static void Register()
    {
        ScenarioRunner.Add("npc/community-aggression-raider-cover", () => Setup(5820), Introduction);
        ScenarioRunner.Add("npc/community-aggression-outsider-killer-cover", () => Setup(5821, CommunityAggression.AttackOutsiders), Introduction);
        ScenarioRunner.Add("npc/community-aggression-controller-cover", () => Setup(5829, CommunityAggression.AttackOutsiders), w => {
            w.Map.PlaceActorAt(victim, new Point(23, 4)); w.Map.PlaceActorAt(w.Game.Player, new Point(7, 6));
            CommunityMembershipScenario.Fov(w);
            Check.Equal(true, Turn(w, chief) is ActionCommunityCall, "ordinary controller introduces a cover before its first attack");
            Check.Equal(false, w.Game.Player.Personality.FindCommunity(chief.SocialGroup.Identity).Policy.AggressionKnown, "first contact conceals aggression");
            CommunityAdaptationScenario.Time(w, 361);
            Check.Equal(true, Turn(w, chief) is ActionCommunityAggression, "introduction does not replace the attack policy");
            Check.Equal(true, chief.IsAggressorOf(w.Game.Player), "next turn initiates real aggression");
        });
        ScenarioRunner.Add("npc/community-aggression-conversation-cover", () => Setup(5822), w => {
            w.Map.PlaceActorAt(w.Game.Player, new Point(7, 6)); CommunityMembershipScenario.Fov(w);
            var own = chief.Personality.FindCommunity(chief.SocialGroup.Identity);
            own.Note(new CommunityNote { EventId = 123456, Kind = "community_policy", EventTurn = 360,
                LearnedTurn = 360, Text = CommunityCultureSystem.KnownPolicy(chief).Rules, Source = "private agreement" });
            for (int i = 0; i < 3; i++) Check.Equal(true, CommunityKnowledgeSystem.Talk(w.Game, chief, w.Game.Player, false), "V community report succeeds");
            // An audible outsider also hears the same public cover when the addressee is another outsider.
            CommunityCultureSystem.SayPolicy(w.Game, chief, victim, true);
            CommunityCultureSystem.SayPolicy(w.Game, chief, member, true);
            string heard = Heard(w.Game.Player);
            Check.Equal(false, heard.Contains("vulnerable outsiders") || heard.Contains("attack all outsiders"), "V and retold policy notes do not leak true intent");
            var known = w.Game.Player.Personality.FindCommunity(own.Identity);
            Check.Equal(false, known.Policy.AggressionKnown, "journal stores only the reported cover");
            Check.Equal(true, known.Notes.All(n => !n.Text.Contains("vulnerable outsiders")), "private policy notes are not copied into the journal");
        });
        ScenarioRunner.Add("npc/community-aggression-cover-persists", () => Setup(5823), w => {
            Introduction(w); string claim = CommunityCultureSystem.SpokenRules(chief);
            Check.Equal(claim, CommunityCultureSystem.SpokenRules(member), "members use one community cover");
            var tone = CommunityEncounterSystem.IntroductionTone(w.Game, chief, w.Game.Player);
            string file = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "predator-cover.dat");
            var id = chief.PersonalityIdentity; BinarySaveStore.Save(file, Session.Get);
            w = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(file)); chief = NpcIntentSupport.Find(w.Map, id);
            Check.Equal(claim, CommunityCultureSystem.SpokenRules(chief), "cover survives reload");
            Check.Equal(tone, CommunityEncounterSystem.IntroductionTone(w.Game, chief, w.Game.Player), "displayed attitude survives reload");
        });
        ScenarioRunner.Add("npc/community-aggression-cover-keeps-evidence", () => Setup(5824), w => {
            Introduction(w); w.Map.PlaceActorAt(w.Game.Player, new Point(3, 5)); Begin(w);
            var player = w.Game.Player; var record = player.Personality.FindCommunity(chief.SocialGroup.Identity);
            Check.Equal(true, record.Policy.AggressionKnown, "actual attack exposes aggression after the false introduction");
            Check.Equal(CommunityAggression.Raiders, record.Policy.Aggression, "heard attack records real threat");
            chief.SocialGroup.Culture.Revision++; CommunityCultureSystem.SayPolicy(w.Game, chief, player, true);
            Check.Equal(true, record.Policy.AggressionKnown, "later cover does not erase incriminating evidence");
            Check.Equal(CommunityAggression.Raiders, record.Policy.Aggression, "journal retains prior threat");
        });
        ScenarioRunner.Add("npc/community-aggression-unarmed-targets", () => Setup(5825, CommunityAggression.AttackOutsiders), w => {
            Unarm(w, chief);
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "two unarmed survivors may still fight");
            var pistol = new ItemRangedWeapon(w.Game.GameItems.PISTOL); victim.Inventory.AddAll(pistol);
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "hidden backpack weapon is not detected");
            w.Game.DoEquipItem(victim, pistol);
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "even outsider killers avoid armed survivors while unarmed");
            Check.Equal(false, new ActionCommunityAggression(chief, w.Game, victim).IsLegal(), "production action enforces the restriction");
            pistol.Ammo = 0;
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "opponent's empty firearm still looks armed");
            var empty = new ItemRangedWeapon(w.Game.GameItems.PISTOL) { Ammo = 0 }; chief.Inventory.AddAll(empty); w.Game.DoEquipItem(chief, empty);
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "own empty gun cannot justify a barehanded charge");
            empty.Ammo = 1;
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "own loaded firearm permits the ordinary policy");
            Unarm(w, chief);
            var bat = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT); chief.Inventory.AddAll(bat); w.Game.DoEquipItem(chief, bat);
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "armed attacker keeps existing policy");
        });
        ScenarioRunner.Add("npc/community-aggression-unarmed-raider", () => Setup(5826), w => {
            Unarm(w, chief); victim.HitPoints = w.Game.Rules.ActorMaxHPs(victim) / 3;
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "unarmed raider can select a vulnerable unarmed target");
            var bat = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT); victim.Inventory.AddAll(bat); w.Game.DoEquipItem(victim, bat);
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "unarmed raider refuses armed target");
        });
        ScenarioRunner.Add("npc/community-aggression-disarmed-pursuit", () => Setup(5827), w => {
            Begin(w); Unarm(w, chief);
            var bat = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT); victim.Inventory.AddAll(bat); w.Game.DoEquipItem(victim, bat);
            w.Map.PlaceActorAt(victim, new Point(4, 5)); int hp = victim.HitPoints;
            for (int i = 1; i <= 3; i++) { CommunityAdaptationScenario.Time(w, 360 + i); Turn(w, chief); }
            Check.Equal(hp, victim.HitPoints, "disarmed hunter does not attack an armed target");
            Check.Equal(true, w.Game.Rules.GridDistance(chief.Location.Position, victim.Location.Position) > 1, "ordinary controller physically retreats");
        });
        ScenarioRunner.Add("npc/community-aggression-unarmed-cornered-feud", () => Setup(5828, CommunityAggression.Guarded), w => {
            Unarm(w, chief); w.Map.PlaceActorAt(chief, new Point(3, 3)); w.Map.PlaceActorAt(victim, new Point(4, 3));
            var bat = new ItemMeleeWeapon(w.Game.GameItems.BASEBALLBAT); victim.Inventory.AddAll(bat); w.Game.DoEquipItem(victim, bat);
            for (int x = 2; x <= 4; x++) for (int y = 2; y <= 4; y++)
                if (new Point(x, y) != chief.Location.Position && new Point(x, y) != victim.Location.Position) w.SetTile(x, y, '#');
            w.Game.DoMakeAggression(chief, victim); int hp = victim.HitPoints;
            Check.Equal(true, w.Game.Rules.IsKnownEnemy(chief, victim), "existing hostility remains in force");
            Check.Equal(true, Turn(w, chief) is ActionWait, "cornered unarmed member waits instead of attacking");
            Check.Equal(hp, victim.HitPoints, "armed enemy takes no barehanded hit");
        });
        ScenarioRunner.Add("npc/community-aggression-founding", () => CommunityAdaptationScenario.Arena(5800), w => {
            var founder = NpcIntentSupport.Actor(w, "Founder", 4, 4);
            int[] counts = new int[3];
            for (int n = 0; n < 100; n++) {
                var bytes = new byte[16]; bytes[1] = (byte)n;
                var group = new SocialGroup(founder, new Guid(bytes));
                CommunityCultureSystem.Initialize(w.Game, group, new[] { founder });
                counts[(int)group.Culture.Aggression]++;
                var old = group.Culture.Aggression;
                CommunityCultureSystem.Initialize(w.Game, group, new[] { founder });
                Check.Equal(old, group.Culture.Aggression, "initial policy is stable");
            }
            Check.Equal(60, counts[0], "neutral enforcement cultures have guarded outcomes");
            Check.Equal(25, counts[1], "neutral enforcement cultures include raiders");
            Check.Equal(15, counts[2], "neutral enforcement cultures include outsider killers");
            Console.WriteLine("Controlled identity distribution: guarded/raiders/outsider killers = " + String.Join("/", counts));
        });
        ScenarioRunner.Add("npc/community-aggression-on-sight", () => Setup(5801, CommunityAggression.AttackOutsiders), w => {
            NpcIntentSupport.Food(w, chief, 4);
            var pistol = new ItemRangedWeapon(w.Game.GameItems.PISTOL); victim.Inventory.AddAll(pistol); w.Game.DoEquipItem(victim, pistol);
            Check.Equal(false, w.Game.Rules.IsKnownEnemy(chief, victim), "neutral stranger needs no prior feud");
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "kill-on-sight policy ignores supplies and intimidating weapon");
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, member), "own residents are safe");
            var action = Turn(w, chief);
            Check.Equal(true, action is ActionCommunityAggression, "actual civilian controller initiates unprovoked attack");
            Check.Equal(true, chief.IsAggressorOf(victim), "attacker is responsible for aggression");
            Check.Equal(true, victim.IsSelfDefenceFrom(chief), "victim receives normal self-defense");
            Check.Equal(true, w.Game.Rules.IsMurder(chief, victim), "killing an innocent outsider remains murder");
            Check.Equal(false, w.Game.Rules.IsMurder(victim, chief), "defensive killing is lawful under existing rules");
            Check.Equal(false, member.IsAggressorOf(victim), "no telepathic group hostility");
        });
        ScenarioRunner.Add("npc/community-aggression-opportunity", () => Setup(5802), w => {
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "armed low-stock raider targets isolated unarmed outsider");
            NpcIntentSupport.Food(w, chief, 3);
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "well-supplied raider leaves neutral stranger alone");
            foreach (var food in chief.Inventory.Items.OfType<ItemFood>().ToArray()) chief.Inventory.RemoveAllQuantity(food);
            NpcIntentSupport.Food(w, victim, 6);
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "hidden victim backpack does not change opportunity");
            var pistol = new ItemRangedWeapon(w.Game.GameItems.PISTOL); victim.Inventory.AddAll(pistol); w.Game.DoEquipItem(victim, pistol);
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "visible firearm deters opportunist, even when ammo is concealed");
        });
        ScenarioRunner.Add("npc/community-aggression-groups-and-visibility", () => Setup(5803), w => {
            var companion = NpcIntentSupport.Actor(w, "Nearby stranger", 8, 4);
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "nearby visible strangers make attack too risky");
            w.Map.PlaceActorAt(companion, new Point(23, 4));
            Check.Equal(true, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "hidden companion is not tracked");
            w.Map.PlaceActorAt(victim, new Point(21, 4));
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "wall prevents attack commitment");
            Check.Equal(false, w.Game.Rules.IsKnownEnemy(chief, victim), "unseen target is not made an enemy");
        });
        ScenarioRunner.Add("npc/community-aggression-orders-and-needs", () => Setup(5804), w => {
            ((OrderableAI)chief.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, chief.Location));
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "orders prevent voluntary robbery");
            ((OrderableAI)chief.Controller).SetOrder(null); chief.SleepPoints = 0;
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "sleepy opportunist declines attack");
            chief.SleepPoints = w.Game.Rules.ActorMaxSleep(chief); chief.HitPoints = w.Game.Rules.ActorMaxHPs(chief) / 2;
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "serious wounds deter opportunist");
            chief.HitPoints = w.Game.Rules.ActorMaxHPs(chief); chief.IsSleeping = true;
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "sleeping owner cannot attack");
            chief.IsSleeping = false; w.Game.Player.AddFollower(chief);
            Check.Equal(false, CommunityAggressionSystem.CanAttack(w.Game, chief, victim), "player's personal party is excluded");
        });
        ScenarioRunner.Add("npc/community-aggression-guarded", () => Setup(5805, CommunityAggression.Guarded), w => {
            Check.Equal(null, CommunityAggressionSystem.Select(w.Game, chief, new[] { victim }), "guarded group retains previous neutral behavior");
            Check.Equal(false, new ActionCommunityAggression(chief, w.Game, victim).IsLegal(), "no unprovoked attack under guarded policy");
        });
        ScenarioRunner.Add("npc/community-aggression-adjacent", () => Setup(5806), w => {
            w.Map.PlaceActorAt(victim, new Point(4, 5)); int hp = victim.HitPoints;
            Begin(w);
            Check.Equal(true, chief.IsAggressorOf(victim), "adjacent ordinary attack marks aggressor");
            Check.Equal(true, victim.HitPoints <= hp, "normal combat resolution governs damage");
            Check.Equal(true, NpcIntentSupport.HasEvent(victim, "attack"), "victim has real attack evidence");
        });
        ScenarioRunner.Add("npc/community-aggression-loot-and-return", () => Setup(5807), w => {
            w.Map.PlaceActorAt(victim, new Point(6, 4)); victim.HitPoints = 1;
            var food = NpcIntentSupport.Food(w, victim, 3); w.Game.DoDropItem(victim, food);
            Begin(w);
            for (int n = 1; n <= 38 && (!victim.IsDead || CommunityAggressionSystem.Food(w.Game, chief) == 0); n++) {
                CommunityAdaptationScenario.Time(w, 360 + n); Turn(w, chief);
            }
            Check.Equal(true, victim.IsDead, "real controller and ordinary combat kill target");
            Check.Equal(true, chief.MurdersCounter > 0, "unprovoked killing retains murder consequences");
            Check.Equal(3, CommunityAggressionSystem.Food(w.Game, chief), "raider physically collects the real dropped stack");
            Check.Equal(0, w.Map.GroundInventories.Sum(i => i.Items.OfType<ItemFood>().Sum(f => f.Quantity)), "taking does not duplicate supplies");
            for (int n = 40; n < 85 && chief.Predation != null; n++) {
                CommunityAdaptationScenario.Time(w, 360 + n); Turn(w, chief);
            }
            Check.Equal(null, chief.Predation, "raider finishes bounded physical withdrawal");
            Check.Equal(true, w.Game.Rules.GridDistance(chief.Location.Position, new Point(4, 4)) <= 1, "raider returns to departure area");
        });
        ScenarioRunner.Add("npc/community-aggression-no-tracking-and-timeout", () => Setup(5808), w => {
            Begin(w); var last = chief.Predation.LastSeen;
            w.Map.PlaceActorAt(chief, new Point(7, 5)); w.Map.PlaceActorAt(victim, new Point(21, 4));
            CommunityAggressionSystem.Update(w.Game, chief, new Actor[0]);
            Check.Equal(last, chief.Predation.LastSeen, "unseen relocation does not update prey location");
            CommunityAdaptationScenario.Time(w, 401); CommunityAggressionSystem.Update(w.Game, chief, new Actor[0]);
            Check.Equal(true, chief.Predation.Returning, "unsuccessful hunt has finite duration");
            Check.Equal(true, chief.IsAggressorOf(victim), "withdrawal does not forgive aggression");
            CommunityAdaptationScenario.Time(w, 462); CommunityAggressionSystem.Update(w.Game, chief, new Actor[0]);
            Check.Equal(null, chief.Predation, "uncompleted return has finite duration");
        });
        ScenarioRunner.Add("npc/community-aggression-save-and-rules", () => Setup(5809), w => {
            w.Map.PlaceActorAt(w.Game.Player, new Point(3, 5));
            Begin(w); var id = chief.PersonalityIdentity; var prey = victim.PersonalityIdentity;
            var record = w.Game.Player.Personality.FindCommunity(chief.SocialGroup.Identity);
            Check.Equal(true, record != null && record.Policy != null && record.Policy.Aggression == CommunityAggression.Raiders, "heard threat gives dated policy to nearby player");
            Check.Equal(true, record.Policy.Rules.Contains("vulnerable outsiders"), "journal rule explains predation");
            int deadline = chief.Predation.Deadline, cooldown = chief.NextPredationClock;
            string file = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "aggression-active.dat");
            BinarySaveStore.Save(file, Session.Get); w = NpcIntentSupport.Restore(w, BinarySaveStore.Load<Session>(file));
            chief = NpcIntentSupport.Find(w.Map, id); victim = NpcIntentSupport.Find(w.Map, prey);
            Check.Equal(CommunityAggression.Raiders, chief.SocialGroup.Culture.Aggression, "culture policy survives format5");
            Check.Equal(prey, chief.Predation.Target, "saved hunt retains target identity");
            Check.Equal(deadline, chief.Predation.Deadline, "hunt clock persists"); Check.Equal(cooldown, chief.NextPredationClock, "cooldown persists");
            Check.Equal(true, chief.IsAggressorOf(victim), "aggression persists");
        });
        ScenarioRunner.Add("npc/community-aggression-migration", () => Setup(5810, CommunityAggression.Guarded), w => {
            var group = chief.SocialGroup; var policy = CommunityCultureSystem.KnownPolicy(chief);
            group.Culture.AggressionInitialized = false; group.Culture.Enforcement = 80; policy.AggressionKnown = false;
            var outsiderRecord = victim.Personality.LearnCommunity(group.Identity, group.CommunityName, 360, "old policy report");
            outsiderRecord.Policy = policy.Copy();
            int actors = w.Map.CountActors, food = w.Map.Actors.Sum(NpcIntentSupport.FoodUnits);
            CommunityCultureSystem.Ensure(w.Game, chief);
            Check.Equal(CommunityAggression.AttackOutsiders, group.Culture.Aggression, "old forceful culture gains stable aggression policy");
            Check.Equal(true, CommunityCultureSystem.KnownPolicy(chief).AggressionKnown, "chief initializes own policy");
            Check.Equal(false, outsiderRecord.Policy.AggressionKnown, "foreign prior testimony stays unchanged until heard");
            Check.Equal(actors, w.Map.CountActors, "migration creates no actors"); Check.Equal(food, w.Map.Actors.Sum(NpcIntentSupport.FoodUnits), "migration creates no supplies");
            Begin(w);
            Check.Equal(true, outsiderRecord.Policy.AggressionKnown, "actual heard threat can supersede old complete policy");
            Check.Equal(CommunityAggression.AttackOutsiders, outsiderRecord.Policy.Aggression, "old observer learns newly spoken aggression");
        });
        ScenarioRunner.Add("npc/community-aggression-responsive", () => Setup(5811, CommunityAggression.AttackOutsiders), w => {
            Session.Get.GamePreset.Simulation = SimulationMode.ResponsiveNpcs;
            Check.Equal(true, Turn(w, chief) is ActionCommunityAggression, "responsive controller still attacks observed outsider");
        });
        ScenarioRunner.Add("npc/community-aggression-adaptive", () => Setup(5812, CommunityAggression.AttackOutsiders), w => {
            Session.Get.GamePreset.Simulation = SimulationMode.AdaptiveWorld;
            Check.Equal(true, Turn(w, chief) is ActionCommunityAggression, "adaptive active controller still attacks observed outsider");
        });
    }
}
