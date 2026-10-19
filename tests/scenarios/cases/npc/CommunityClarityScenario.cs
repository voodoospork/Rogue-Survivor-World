using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;
using djack.RogueSurvivor.Engine.Items;
using djack.RogueSurvivor.Engine.MapObjects;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.AI;
using djack.RogueSurvivor.Gameplay.Personality;

static class CommunityClarityScenario
{
    static ScenarioWorld Arena(int seed)
    {
        ScenarioWorld world = TownScenarioFactory.Arena(seed,
            "................................", "................................", "................................", "................................", "................................");
        Session.Get.GamePreset = GamePreset.World();
        Session.Get.UniqueActors.TheSewersThing = new UniqueActor();
        RogueGame.KeyBindings.ResetToDefaults();
        return world;
    }
    static void Fov(ScenarioWorld world) { Check.Call(world.Game, "UpdatePlayerFOV", world.Game.Player); world.Game.ComputeViewRect(world.Game.Player.Location.Position); }
    static void Time(ScenarioWorld world, int turn) { world.Map.LocalTime.TurnCounter = turn; Session.Get.WorldTime.TurnCounter = turn; }
    static Actor Group(ScenarioWorld world, string trait = null)
    {
        Actor leader = trait == null ? NpcIntentSupport.Actor(world, "Mira", 4, 1) : NpcIntentSupport.Actor(world, "Mira", 4, 1, trait);
        Actor member = NpcIntentSupport.Actor(world, "Sam", 5, 1);
        leader.AddFollower(member); leader.SocialGroup.CommunityName = "Mira's circle";
        return leader;
    }
    static string Heard(Actor player)
    {
        string result = ""; foreach (HeardJournalEntry entry in player.Personality.HeardJournal) result += entry.Text + "\n"; return result;
    }
    static string Describe(ScenarioWorld world, string method, params object[] args)
    { return String.Join("\n", (string[])Check.Call(world.Game, method, args)); }
    static string Messages(ScenarioWorld world)
    {
        MessageManager manager = (MessageManager)typeof(RogueGame).GetField("m_MessageManager", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(world.Game);
        string result = ""; foreach (djack.RogueSurvivor.Data.Message entry in manager.History) result += entry.Text + "\n"; return result;
    }
    static void SpeechFits(ScenarioWorld world)
    {
        ScenarioUI ui = (ScenarioUI)world.Game.UI; ui.DrawnStrings.Clear();
        Check.Call(world.Game, "DrawMessages");
        int panel = (int)typeof(RogueGame).GetField("LOCATIONPANEL_X", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        int left = (int)typeof(RogueGame).GetField("MESSAGES_X", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        foreach (string line in ui.DrawnStrings)
            Check.Equal(true, left + ui.UI_BoldTextWidth(line) < panel, "rendered community messages fit before location panel");
    }
    internal static Point InventorySlot(ScenarioWorld world, int slot)
    {
        int x = (int)typeof(RogueGame).GetField("INVENTORYPANEL_X", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        int y = (int)typeof(RogueGame).GetField("INVENTORYPANEL_Y", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        int columns = (int)typeof(RogueGame).GetField("INVENTORY_SLOTS_PER_LINE", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
        return (Point)Check.Call(world.Game, "InventorySlotToScreen", x, y, slot % columns, slot / columns);
    }

    internal static Keys DirectionKey(Point from, Point to)
    {
        Direction direction = Direction.FromVector(to.X - from.X, to.Y - from.Y);
        Keys[] keys = { Keys.NumPad8, Keys.NumPad9, Keys.NumPad6, Keys.NumPad3, Keys.NumPad2, Keys.NumPad1, Keys.NumPad4, Keys.NumPad7 };
        for (int i = 0; i < 8; i++) if (Direction.COMPASS[i] == direction) return keys[i];
        throw new Exception("not an adjacent direction");
    }

    internal static List<Point> WalkingPath(ScenarioWorld world, Point goal, int range)
    {
        var path = TryWalkingPath(world, goal, range);
        if (path == null) throw new Exception("no ordinary walking/door route to encounter");
        return path;
    }

    internal static List<Point> TryWalkingPath(ScenarioWorld world, Point goal, int range)
    {
        Point start = world.Game.Player.Location.Position;
        var queue = new Queue<Point>(); var parent = new Dictionary<Point, Point>(); queue.Enqueue(start); parent[start] = start;
        while (queue.Count > 0)
        {
            Point point = queue.Dequeue();
            if (world.Game.Rules.GridDistance(point, goal) <= range)
            {
                var path = new List<Point>();
                while (point != start) { path.Add(point); point = parent[point]; }
                path.Reverse(); return path;
            }
            foreach (Direction dir in Direction.COMPASS)
            {
                Point next = point + dir;
                if (parent.ContainsKey(next) || !world.Map.IsInBounds(next) || !world.Map.GetTileAt(next).Model.IsWalkable || world.Map.GetActorAt(next) != null) continue;
                MapObject obj = world.Map.GetMapObjectAt(next);
                if (obj != null && !obj.IsWalkable && (!(obj is DoorWindow) || ((DoorWindow)obj).BarricadePoints > 0)) continue;
                parent[next] = point; queue.Enqueue(next);
            }
        }
        return null;
    }

    static void Walk(ScenarioWorld world, Point destination, int range)
    {
        foreach (Point next in WalkingPath(world, destination, range))
        {
            for (int attempt = 0; attempt < 3 && world.Game.Player.Location.Position != next; attempt++)
            {
                Actor player = world.Game.Player; player.ActionPoints = Rules.BASE_ACTION_COST;
                Check.Equal(true, world.Bump(player, Direction.FromVector(next.X - player.Location.Position.X, next.Y - player.Location.Position.Y)), "fresh-world route uses legal movement or door action");
                Time(world, Session.Get.WorldTime.TurnCounter + 1); Fov(world);
            }
            Check.Equal(next, world.Game.Player.Location.Position, "fresh-world route actually advances");
        }
    }

    internal static void AmbientTurn(ScenarioWorld world)
    {
        int turn = world.Map.LocalTime.TurnCounter;
        world.Game.Player.ActionPoints = 0;
        Type flags = typeof(RogueGame).GetNestedType("SimFlags", BindingFlags.NonPublic);
        int actors = 0; foreach (Actor actor in world.Map.Actors) actors++;
        for (int step = 0; step < actors * 8 + 20 && world.Map.LocalTime.TurnCounter == turn; step++)
            Check.Call(world.Game, "AdvancePlay", new[] { typeof(Map), flags }, world.Map, Enum.ToObject(flags, 0));
        Check.Equal(turn + 1, world.Map.LocalTime.TurnCounter, "ordinary map scheduler executes NPC turns and advances metabolism");
        Session.Get.WorldTime.TurnCounter = world.Map.LocalTime.TurnCounter;
        Fov(world);
    }

    // A generated world, with actual walking, NPC actions, V/G input and a return visit; no added supplies.
    public static void FreshEncounter(RogueGame game, Actor leader)
    {
        ScenarioWorld world = new ScenarioWorld(4921, game.Player.Location.Map, game);
        Actor player = game.Player; Point departure = player.Location.Position;
        string name = leader.SocialGroup.CommunityName;
        if (NpcIntentSupport.FoodUnits(player) == 0)
        {
            ItemFood supplies = null; Point source = Point.Empty; int distance = Int32.MaxValue;
            for (int x = 0; x < world.Map.Width; x++) for (int y = 0; y < world.Map.Height; y++)
            {
                Point point = new Point(x, y); Inventory inventory = world.Map.GetItemsAt(point);
                if (inventory == null || world.Map.XpdBaseAt(point) != null) continue;
                foreach (Item item in inventory.Items)
                    if (item is ItemFood && game.Rules.GridDistance(departure, point) < distance)
                    { supplies = (ItemFood)item; source = point; distance = game.Rules.GridDistance(departure, point); }
            }
            Check.Equal(true, supplies != null, "fresh world contains generated unclaimed food");
            Walk(world, source, 1); player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Try(new ActionTakeItem(player, game, source, supplies)), "fresh survivor gathers existing nearby food");
        }
        Walk(world, leader.Location.Position, 1);
        // Recheck greetings with every map actor and ordinary survival upkeep running.
        for (int turn = 0; turn < 3; turn++) AmbientTurn(world);
        Check.Call(game, "PresentEncounterSpeech");
        if (game.Rules.GridDistance(player.Location.Position, leader.Location.Position) > 1) Walk(world, leader.Location.Position, 1);
        Fov(world);
        for (int attempt = 0; attempt < 40 && (player.Personality == null || player.Personality.FindCommunity(leader.SocialGroup.Identity) == null); attempt++)
        {
            leader.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.NpcTurn(leader), "generated leader executes actual NPC turn");
            Time(world, Session.Get.WorldTime.TurnCounter + 1); Fov(world);
            if (game.Rules.GridDistance(player.Location.Position, leader.Location.Position) > 1) Walk(world, leader.Location.Position, 1);
        }
        Check.Equal(true, player.Personality != null && player.Personality.FindCommunity(leader.SocialGroup.Identity) != null, "generated community introduces itself naturally");
        string homeRule=CommunityCultureSystem.SpokenRules(leader);
        Check.Equal(true, Heard(player).Contains("Leave our supplies alone")||homeRule.Length>0&&Heard(player).Contains(homeRule), "fresh encounter communicates its concrete home rule");
        SpeechFits(world);
        Check.Equal(true, Describe(world, "DescribeActor", leader).Contains("Last known community: " + name), "fresh encounter makes affiliation recognizable");
        ScenarioUI ui = (ScenarioUI)game.UI;
        int adjacent = 0;
        foreach (Actor actor in world.Map.Actors)
            if (actor != player && !actor.IsDead && !actor.IsSleeping && actor.Model.Abilities.IsIntelligent &&
                game.Rules.GridDistance(actor.Location.Position, player.Location.Position) <= 1 && !game.Rules.AreEnemies(player, actor)) adjacent++;
        if (adjacent > 1) ui.QueueKey(DirectionKey(player.Location.Position, leader.Location.Position));
        player.ActionPoints = Rules.BASE_ACTION_COST;
        if (NpcConversation.Pending(player, leader) == null && CommunityMembershipSystem.IsLeader(game, leader)) ui.QueueWaitKey(Keys.D1);
        Check.Equal(true, Check.Call(game, "HandlePlayerTalk", player), "fresh encounter uses V input");
        SpeechFits(world);
        ItemFood gift = null; int slot = -1;
        for (int i = 0; i < player.Inventory.CountItems; i++)
        {
            string reason;
            if (player.Inventory[i] is ItemFood && game.Rules.CanActorGiveItemTo(player, leader, player.Inventory[i], out reason))
            { gift = (ItemFood)player.Inventory[i]; slot = i; break; }
        }
        Check.Equal(true, gift != null, "generated food can be given without creating resources");
        int before = NpcIntentSupport.FoodUnits(leader);
        ui.QueueKey(DirectionKey(player.Location.Position, leader.Location.Position)); player.ActionPoints = Rules.BASE_ACTION_COST;
        Check.Equal(true, Check.Call(game, "HandlePlayerGiveItem", player, InventorySlot(world, slot)), "fresh encounter gives actual generated supplies through G input");
        Check.Equal(true, NpcIntentSupport.FoodUnits(leader) > before, "giving changes actual community supplies");
        Walk(world, departure, 0);
        leader.Controller.GetAction(game); // Observe absence before the return.
        Time(world, Session.Get.WorldTime.TurnCounter + 61);
        Walk(world, leader.Location.Position, 1); Fov(world);
        adjacent = 0;
        foreach (Actor actor in world.Map.Actors)
            if (actor != player && !actor.IsDead && !actor.IsSleeping && actor.Model.Abilities.IsIntelligent &&
                game.Rules.GridDistance(actor.Location.Position, player.Location.Position) <= 1 && !game.Rules.AreEnemies(player, actor)) adjacent++;
        if (adjacent > 1) ui.QueueKey(DirectionKey(player.Location.Position, leader.Location.Position));
        player.ActionPoints = Rules.BASE_ACTION_COST;
        if (NpcConversation.Pending(player, leader) == null && CommunityMembershipSystem.IsLeader(game, leader)) ui.QueueWaitKey(Keys.D1);
        Check.Equal(true, Check.Call(game, "HandlePlayerTalk", player), "fresh-world return uses V input");
        Check.Equal(true, player.Personality.FindCommunity(leader.SocialGroup.Identity).Notes.Exists(n => n.Kind == "shared_food"), "return retains a dated real supply change");
        Console.WriteLine("Fresh World clarity playthrough: walked from starting shelter, heard " + name + " introduce its home and supplies, talked with V, gave generated food with G, walked away and returned for a dated report.");
    }

    public static void Register()
    {
        ScenarioRunner.Add("npc/community-greeting-busy-job", () => Arena(4940), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 25, 1);
            Actor leader = Group(world, "organized"); Actor hungry = world.Map.GetActorAt(5, 1);
            leader.Personality.AddTrait(new TraitInstance("loyal")); hungry.Personality.AddTrait(new TraitInstance("sociable"));
            Actor collector = NpcIntentSupport.Actor(world, "Lee", 4, 2, "generous", "scavenger"); leader.AddFollower(collector);
            hungry.FoodPoints = Session.Get.GamePreset.HungerPoints - 1;
            NpcIntentSupport.Turn(world, hungry);
            world.Map.DropItemAt(new ItemFood(world.Game.GameItems.CANNED_FOOD) { Quantity = 3 }, new Point(7, 2));
            NpcIntentSupport.Turn(world, leader);
            Check.Equal(true, leader.SocialGroup.Plan != null, "ordinary group job is active before encounter");
            world.Map.PlaceActorAt(player, new Point(1, 1)); Fov(world);
            NpcIntentSupport.Turn(world, leader);
            Check.Equal(true, Heard(player).Contains("Mira's circle"), "routine group work cannot starve first encounter greeting");
        });
        ScenarioRunner.Add("npc/community-greeting-after-chatter", () => Arena(4941), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor leader = Group(world); Fov(world);
            NpcIntentSupport.Turn(world, leader);
            for (int i = 0; i < 12; i++) world.Game.AddMessage(new djack.RogueSurvivor.Data.Message("Other survivor activity " + i, 0));
            ScenarioUI ui = (ScenarioUI)world.Game.UI; ui.QueueKey(Keys.NumPad5);
            Check.Call(world.Game, "HandlePlayerActor", player);
            ui.DrawnStrings.Clear(); Check.Call(world.Game, "DrawMessages");
            Check.Equal(true, String.Join(" ", ui.DrawnStrings).Contains("Mira's circle"), "greeting survives intervening NPC messages until player can read it");
        });
        ScenarioRunner.Add("npc/community-witnessed-defense", () => Arena(4942), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor leader = Group(world, "brave");
            Item weapon = new ItemMeleeWeapon(world.Game.GameItems.BASEBALLBAT);
            leader.Inventory.AddAll(weapon); world.Game.DoEquipItem(leader, weapon);
            world.Map.AddXpdBase(new XpdBase(leader, new[] { player.Location.Position, leader.Location.Position }));
            ItemFood stock = new ItemFood(world.Game.GameItems.CANNED_FOOD); world.Map.DropItemAt(stock, player.Location.Position);
            Fov(world); player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Try(new ActionTakeItem(player, world.Game, player.Location.Position, stock)), "outsider takes actually claimed food");
            Check.Equal(true, world.Game.Rules.AreEnemies(player, leader), "awake owner identifies witnessed theft");
            int hp = player.HitPoints;
            for (int turn = 0; turn < 16 && player.HitPoints == hp; turn++)
            { Time(world, turn); Fov(world); leader.ActionPoints = Rules.BASE_ACTION_COST; Check.Equal(true, world.NpcTurn(leader), "owner performs actual defensive action"); }
            Check.Equal(true, player.HitPoints < hp, "armed owner actually attacks after witnessed theft");
        });
        string[] traits = { "sociable", null, "suspicious", null };
        string[] phrases = { "Come talk.", "Looking for someone?", "Keep your distance.", "Stay back!" };
        for (int i = 0; i < traits.Length; i++)
        {
            int tone = i;
            ScenarioRunner.Add("npc/community-call-" + new[] { "welcoming", "neutral", "wary", "hostile" }[i], () => Arena(4930 + tone), world =>
            {
                Actor player = NpcIntentSupport.Player(world, 1, 1);
                Actor leader = Group(world, traits[tone]);
                Actor sleeper = NpcIntentSupport.Actor(world, "Sleeper", 4, 2);
                sleeper.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
                sleeper.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
                sleeper.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LIGHT_SLEEPER);
                sleeper.IsSleeping = true;
                if (tone == 3) world.Game.DoMakeAggression(player, leader);
                Fov(world);
                NpcIntentSupport.Turn(world, leader);
                Check.Equal(true, Heard(player).Contains(phrases[tone]), "actual NPC controller chooses disposition call");
                Check.Equal(true, Heard(player).Contains("Mira's circle"), "call names the group");
                Check.Equal(tone != 3, sleeper.IsSleeping, "only hostile warning invokes real loud-noise waking");
                Check.Equal(tone == 3, world.Game.Rules.AreEnemies(player, leader), "wary speech does not cause aggression");
                KnownCommunity known = player.Personality.FindCommunity(leader.SocialGroup.Identity);
                Check.Equal(1, known.Members.Count, "call identifies only the visible speaker, not the roster");
                Check.Equal(null, known.Leader, "call does not disclose leader metadata");
                Check.Equal(null, known.Shelter, "a wandering member does not announce a hidden shelter");
                Check.Equal(true, Describe(world, "DescribeActor", leader).Contains("Last known community: Mira's circle"), "inspection uses learned affiliation");
            });
        }
        ScenarioRunner.Add("npc/community-call-cooldown", () => Arena(4934), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor leader = Group(world);
            leader.AddFollower(NpcIntentSupport.Actor(world, "Lee", 6, 1));
            Actor member = world.Map.GetActorAt(5, 1); Fov(world);
            NpcIntentSupport.Turn(world, leader);
            Check.Equal(false, member.Controller.GetAction(world.Game) is ActionCommunityCall, "another member cannot duplicate greeting");
            for (int turn = 1; turn <= 65; turn++)
            {
                Time(world, turn);
                Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "continuous contact does not repeat after cooldown");
            }
            world.Map.PlaceActorAt(player, new Point(25, 1)); Time(world, 66);
            leader.Controller.GetAction(world.Game);
            world.Map.PlaceActorAt(player, new Point(1, 1)); Time(world, 96); Fov(world);
            NpcIntentSupport.Turn(world, leader);
            Check.Equal(96, leader.SocialGroup.CommunityContact.LastCallTurn, "return after absence produces one new greeting");
            Time(world, 97); world.Game.DoMakeAggression(player, leader);
            NpcIntentSupport.Turn(world, leader);
            Check.Equal(true, Heard(player).Contains("Stay back!"), "new hostility gets an extra warning before cooldown expires");
            Check.Equal(false, member.Controller.GetAction(world.Game) is ActionCommunityCall, "hostility warning is shared across group");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "clarity-cooldown-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            ScenarioWorld saved = NpcIntentSupport.Restore(world, BinarySaveStore.Load<Session>(path));
            Actor savedLeader = NpcIntentSupport.Find(saved.Map, leader.PersonalityIdentity);
            Check.Equal(97, savedLeader.SocialGroup.CommunityContact.LastCallTurn, "group call cooldown persists");
            Check.Equal(false, savedLeader.Controller.GetAction(saved.Game) is ActionCommunityCall, "reload does not repeat warning");
            SocialGroup savedGroup = savedLeader.SocialGroup;
            CommunityContactState savedContact = savedGroup.CommunityContact;
            Actor attacker = saved.Game.GameActors.Zombie.CreateAnonymous(saved.Game.GameFactions.TheUndeads, 0);
            saved.Map.PlaceActorAt(attacker, new Point(4, 0)); savedLeader.HitPoints = 1;
            for (int strike = 0; strike < 30 && !savedLeader.IsDead; strike++)
            {
                attacker.ActionPoints = Rules.BASE_ACTION_COST;
                Check.Equal(true, saved.Try(new ActionMeleeAttack(attacker, saved.Game, savedLeader)), "actual combat exercises cooldown succession");
            }
            Check.Equal(true, savedLeader.IsDead, "actual combat kills the calling leader");
            saved.Map.RemoveActor(attacker);
            Actor savedMember = NpcIntentSupport.Find(saved.Map, member.PersonalityIdentity);
            Check.Same(savedGroup, savedMember.SocialGroup, "succession preserves the calling group identity");
            Check.Same(savedContact, savedMember.SocialGroup.CommunityContact, "succession preserves shared cooldown state");
            Check.Equal("Mira's circle", savedMember.SocialGroup.CommunityName, "succession preserves the announced name");
            Check.Equal(false, savedMember.Controller.GetAction(saved.Game) is ActionCommunityCall, "succession cannot repeat the encounter warning");
        });
        ScenarioRunner.Add("npc/community-call-boundaries", () => Arena(4935), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor leader = Group(world);
            leader.IsSleeping = true;
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "sleeping members do not initiate calls");
            leader.IsSleeping = false;
            player.IsSleeping = true;
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "sleeping player receives no call");
            player.IsSleeping = false; world.Map.PlaceActorAt(player, new Point(25, 1)); Fov(world);
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "distant player receives no call");
            world.Map.PlaceActorAt(player, new Point(1, 1)); player.AudioRangeMod = -player.Sheet.BaseAudioRange;
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "call requires player hearing range");
            player.AudioRangeMod = 0;
            // Clear the visible tiles: the NPC can see a player who cannot identify this speaker.
            world.Map.SetViewAndMarkVisited(new System.Collections.Generic.HashSet<Point> { player.Location.Position });
            NpcIntentSupport.Turn(world, leader);
            KnownCommunity known = player.Personality.FindCommunity(leader.SocialGroup.Identity);
            Check.Equal(0, known.Members.Count, "unseen speaker does not disclose actor identity");
            Check.Equal(null, known.Shelter, "unseen speaker does not disclose precise shelter");
            Check.Equal("someone", player.Personality.HeardJournal[player.Personality.HeardJournal.Count - 1].Speaker, "unseen caller stays unidentified in journal");
            Check.Equal(false, Describe(world, "DescribeActor", leader).Contains("Last known community"), "hearing a group name does not identify an unseen actor");
            Actor lone = NpcIntentSupport.Actor(world, "Lone", 2, 2);
            Check.Equal(false, lone.Controller.GetAction(world.Game) is ActionCommunityCall, "ungrouped actors have no community call");
            player.AddFollower(lone);
            Check.Equal(false, lone.Controller.GetAction(world.Game) is ActionCommunityCall, "player's own group is excluded");
        });
        ScenarioRunner.Add("npc/community-call-minimum-interval", () => Arena(4938), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor leader = Group(world); Fov(world); NpcIntentSupport.Turn(world, leader);
            world.Map.PlaceActorAt(player, new Point(25, 1)); Time(world, 1); leader.Controller.GetAction(world.Game);
            world.Map.PlaceActorAt(player, new Point(1, 1)); Time(world, 31); Fov(world);
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "absence alone cannot bypass 60-turn minimum");
            Time(world, 60); NpcIntentSupport.Turn(world, leader);
            Check.Equal(60, leader.SocialGroup.CommunityContact.LastCallTurn, "pending return greeting waits for minimum interval");
        });
        ScenarioRunner.Add("npc/community-claimed-pickup-unwitnessed", () => Arena(4939), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor leader = Group(world);
            XpdBase home = new XpdBase(leader, new[] { new Point(1,1), new Point(4,1) }); world.Map.AddXpdBase(home);
            ItemFood stock = new ItemFood(world.Game.GameItems.CANNED_FOOD); world.Map.DropItemAt(stock, player.Location.Position);
            Check.Equal(true, Describe(world, "DescribeItemLong", stock, false, 0).Contains("taking them may provoke"), "unidentified shelter still warns about claimed supplies");
            Check.Equal(false, Describe(world, "DescribeItemLong", stock, false, 0).Contains("Mira's circle"), "ownership warning does not disclose unknown group identity");
            world.Map.PlaceActorAt(leader, new Point(25,1)); Actor member = world.Map.GetActorAt(5,1); world.Map.PlaceActorAt(member, new Point(26,1));
            player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, Check.Call(world.Game, "OnLMBItem", world.Map.GetItemsAt(player.Location.Position), stock), "actual pickup remains available without confirmation");
            Check.Equal(false, world.Game.Rules.AreEnemies(player, leader), "unwitnessed theft does not invent a witness");
            Check.Equal(false, world.Game.Rules.AreEnemies(player, member), "unseen followers do not become omniscient enemies");
        });
        ScenarioRunner.Add("npc/community-call-priorities", () => Arena(4936), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 1, 1); Actor leader = Group(world); Fov(world);
            leader.StaminaPoints = 0;
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "rest takes priority");
            NpcIntentSupport.Turn(world, leader);
            leader.StaminaPoints = world.Game.Rules.ActorMaxSTA(leader);
            leader.FoodPoints = 0; ItemFood food = NpcIntentSupport.Food(world, leader, 1);
            NpcIntentSupport.Turn(world, leader);
            Check.Equal(false, leader.Inventory.Contains(food), "urgent eating takes priority over greeting");
            leader.FoodPoints = world.Game.Rules.ActorMaxFood(leader);
            Actor guard = world.Map.GetActorAt(5, 1);
            ((OrderableAI)guard.Controller).SetOrder(new ActorOrder(ActorTasks.GUARD, guard.Location));
            Check.Equal(false, guard.Controller.GetAction(world.Game) is ActionCommunityCall, "active follower orders take priority");
            NpcIntentSupport.Turn(world, guard);
            ((OrderableAI)guard.Controller).SetOrder(null);
            ItemGrenadePrimed grenade = new ItemGrenadePrimed(world.Game.GameItems.GRENADE_PRIMED);
            grenade.FuseTimeLeft = 1; world.Map.DropItemAt(grenade, leader.Location.Position);
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "escaping a primed grenade takes priority");
            world.Game.DoMakeAggression(player, leader);
            ActorAction escape = leader.Controller.GetAction(world.Game);
            Check.Equal(false, escape is ActionCommunityCall, "escaping a primed grenade takes priority over a hostile warning");
            Check.Equal(Activity.FLEEING_FROM_EXPLOSIVE, leader.Activity, "NPC selects emergency escape");
            Point danger = leader.Location.Position; leader.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, world.Try(escape), "NPC performs legal emergency escape");
            Check.Equal(false, danger == leader.Location.Position, "NPC actually moves away from the grenade");
            world.Map.RemoveItemAt(grenade, danger);
            world.Map.PlaceActorAt(player, leader.Location.Position + Direction.W); Fov(world);
            Check.Equal(false, leader.Controller.GetAction(world.Game) is ActionCommunityCall, "adjacent combat takes priority over warning");
        });
        string[] giveCases = { "keyboard-hover", "cancel-and-retry", "empty-direction" };
        for (int i = 0; i < giveCases.Length; i++)
        {
            int variant = i;
            ScenarioRunner.Add("npc/community-give-" + giveCases[i], () => Arena(4943 + variant), world =>
            {
                Actor player = NpcIntentSupport.Player(world, 3, 1); Actor leader = Group(world);
                leader.FoodPoints = 0;
                ItemFood gift = new ItemFood(world.Game.GameItems.CANNED_FOOD);
                player.Inventory.AddAll(gift);
                ScenarioUI ui = (ScenarioUI)world.Game.UI;
                ui.MousePosition = InventorySlot(world, player.Inventory.CountItems - 1) + new Size(8, 8);
                player.ActionPoints = Rules.BASE_ACTION_COST;
                Point start = player.Location.Position;
                ui.QueueKey(Keys.G);
                if (variant == 1) { ui.QueueKey(Keys.Escape); ui.QueueKey(Keys.G); }
                if (variant == 2) ui.QueueKey(Keys.NumPad8);
                ui.QueueKey(Keys.NumPad6);
                // Let the old broken path finish so failure reports the missing transfer.
                ui.QueueWaitKey(Keys.Escape); ui.QueueKey(Keys.NumPad5);
                Check.Call(world.Game, "HandlePlayerActor", player);
                Check.Equal(false, player.Inventory.Contains(gift), "G transfers the hovered item through the real player input loop");
                Check.Equal(true, leader.Inventory.Contains(gift), "chosen adjacent recipient receives the gift");
                Check.Equal(start, player.Location.Position, "recipient direction is consumed by giving without movement");
                Check.Equal(0, player.ActionPoints, "giving costs exactly one action");
                Check.Equal(true, String.Join(" ", ui.DrawnStrings).Contains("Giving "), "give mode is displayed before choosing direction");
                if (variant == 2) Check.Equal(true, Messages(world).Contains("Noone there."), "empty direction reports failure and stays in give mode");
            });
        }
        ScenarioRunner.Add("npc/community-clarity-input-and-labels", () => Arena(4937), world =>
        {
            Actor player = NpcIntentSupport.Player(world, 3, 1); Actor leader = Group(world);
            world.Map.AddZone(new Zone("Mira's house", new Rectangle(2, 0, 6, 4)) { BuildingKind = BuildingKind.House });
            XpdBase home = new XpdBase(leader, new[] { new Point(3,1), new Point(4,1), new Point(5,1) }); world.Map.AddXpdBase(home);
            ItemFood stock = new ItemFood(world.Game.GameItems.CANNED_FOOD); world.Map.DropItemAt(stock, player.Location.Position);
            Fov(world); NpcIntentSupport.Turn(world, leader);
            SpeechFits(world);
            Check.Equal(true, Heard(player).Contains("Leave our supplies alone"), "member announces ownership at home");
            Check.Equal(true, Describe(world, "DescribeItemLong", stock, false, 0).Contains("taking them may provoke"), "ground-item tooltip warns before pickup");
            Check.Equal("Claimed supplies", Check.Call(world.Game, "GroundInventoryTitle"), "ground panel prominently identifies claimed supplies");
            string homeBefore = Describe(world, "DescribeStuffAt", world.Map, new Point(6, 2));
            Check.Equal(true, homeBefore.Contains("Last reported home of Mira's circle"), "reported building is identified on inspection");
            int ap = player.ActionPoints;
            Check.Equal(false, Check.Call(world.Game, "HandlePlayerGiveItem", player, Point.Empty), "G without inventory hover returns safely");
            Check.Equal(ap, player.ActionPoints, "missing selection costs no action");
            Check.Equal(true, Messages(world).Contains("Hover an item in your inventory"), "missing selection explains giving controls");
            SpeechFits(world);
            leader.FoodPoints = 0; ScenarioUI ui = (ScenarioUI)world.Game.UI;
            player.ActionPoints = Rules.BASE_ACTION_COST;
            ui.QueueWaitKey(Keys.D1);
            Check.Equal(true, Check.Call(world.Game, "HandlePlayerTalk", player), "real V input opens community conversation");
            SpeechFits(world);
            Check.Equal(true, NpcConversation.Pending(player, leader) != null, "talk exposes actual need");
            ui.QueueWaitKey(Keys.Y); player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, Check.Call(world.Game, "HandlePlayerTalk", player), "real Y input promises help");
            ItemFood gift = NpcIntentSupport.Food(world, player, 1); ui.QueueKey(Keys.NumPad6); player.ActionPoints = Rules.BASE_ACTION_COST;
            Check.Equal(true, Check.Call(world.Game, "HandlePlayerGiveItem", player, InventorySlot(world, 0)), "real hovered inventory and direction give supplies");
            Check.Equal(0, player.ActionPoints, "giving costs one player action");
            Check.Equal(true, NpcIntentSupport.HasEvent(leader, "promise_kept"), "real giving input fulfills promise");
            string affiliation = Describe(world, "DescribeActor", leader);
            homeBefore = Describe(world, "DescribeStuffAt", world.Map, new Point(6, 2));
            world.Map.RemoveXpdBase(home);
            Check.Equal(homeBefore, Describe(world, "DescribeStuffAt", world.Map, new Point(6, 2)), "inspection does not learn unseen shelter loss");
            Actor other = NpcIntentSupport.Actor(world, "Other", 28, 1); other.AddFollower(leader);
            Check.Equal(affiliation.Contains("Last known community: Mira's circle"), Describe(world, "DescribeActor", leader).Contains("Last known community: Mira's circle"), "inspection does not learn unseen affiliation change");
            string path = Path.Combine(Environment.GetEnvironmentVariable("ROGUE_COMMUNITY_ARTIFACTS"), "clarity-labels-format-5.dat");
            BinarySaveStore.Save(path, Session.Get);
            ScenarioWorld saved = NpcIntentSupport.Restore(world, BinarySaveStore.Load<Session>(path));
            Check.Equal(true, Describe(saved, "DescribeStuffAt", saved.Map, new Point(6, 2)).Contains("Last reported home of Mira's circle"), "dated location snapshot survives reload");
            ui = (ScenarioUI)saved.Game.UI; ui.QueueWaitKey(Keys.Tab); ui.QueueWaitKey(Keys.Escape);
            Check.Call(saved.Game, "HandleHeardJournal");
            Check.Equal(true, String.Join("\n", ui.DrawnStrings).Contains("Communities"), "J/Tab retains detailed reports");
        });
    }
}
