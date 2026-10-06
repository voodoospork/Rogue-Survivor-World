using System;
using System.Collections.Generic;
using System.Drawing;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Gameplay;
using djack.RogueSurvivor.Gameplay.Personality;

namespace djack.RogueSurvivor.Engine
{
    partial class RogueGame
    {
        List<Message> m_PendingEncounterSpeech;

        string FollowerVoicePrefix(Actor speaker)
        {
            string[] directions = { "north", "northeast", "east", "southeast", "south", "southwest", "west", "northwest" };
            var offset = new Point(speaker.Location.Position.X - m_Player.Location.Position.X,
                speaker.Location.Position.Y - m_Player.Location.Position.Y);
            string direction = directions[Direction.ApproximateFromVector(offset).Index];
            int distance = m_Rules.GridDistance(speaker.Location.Position, m_Player.Location.Position);
            string range = distance <= 3 ? "nearby" : distance <= 7 ? "not far away" : "in the distance";
            return speaker.UnmodifiedName + " shouts from the " + direction + ", " + range + ": ";
        }

        internal void PresentEncounterSpeech()
        {
            if (m_PendingEncounterSpeech == null || m_PendingEncounterSpeech.Count == 0) return;
            foreach (Message message in m_PendingEncounterSpeech) m_MessageManager.PresentAgain(message, MAX_MESSAGES);
            m_PendingEncounterSpeech.Clear();
            StopMouseMove();
        }
        // Called only by new-world generation, after the player's starting shelter is chosen.
        internal int EstablishInitialCommunities(Map startMap)
        {
            if (!m_Session.GamePreset.InitialCommunities || !m_Session.GamePreset.Bases ||
                !m_Session.GamePreset.NpcPersonalitiesEnabled || startMap == null) return 0;
            EstablishLocalPoliceUnits();
            List<Map> maps = new List<Map> { startMap };
            foreach (Map map in WorldMaps())
                if (map != startMap && map.District != null && map == map.District.EntryMap &&
                    startMap.District != null && Math.Abs(map.District.WorldPosition.X - startMap.District.WorldPosition.X) +
                    Math.Abs(map.District.WorldPosition.Y - startMap.District.WorldPosition.Y) == 1) maps.Add(map);
            int established = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Map map in maps)
            {
                List<Actor> available = new List<Actor>();
                foreach (Actor actor in map.Actors)
                    if (!actor.IsPlayer && !actor.IsDead && !actor.IsUnique && !actor.HasLeader && actor.CountFollowers == 0 &&
                        actor.Faction == m_GameFactions.TheCivilians && actor.Model.Abilities.IsIntelligent &&
                        !actor.Model.Abilities.IsUndead) available.Add(actor);
                foreach (Zone zone in map.Zones)
                {
                    if (established == 3 || available.Count < 3) break;
                    if (zone.BuildingKind != BuildingKind.House && zone.BuildingKind != BuildingKind.Apartments) continue;
                    if (m_Player != null && m_Player.Location.Map == map && zone.Bounds.Contains(m_Player.Location.Position)) continue;
                    Actor leader = available[0];
                    List<Point> cells = null, places = null;
                    Point origin = leader.Location.Position;
                    for (int y = zone.Bounds.Top; y < zone.Bounds.Bottom && cells == null; y++)
                        for (int x = zone.Bounds.Left; x < zone.Bounds.Right && cells == null; x++)
                        {
                            Point point = new Point(x, y);
                            if (!map.IsInBounds(point) || !map.IsWalkable(x, y) || !map.GetTileAt(point).IsInside ||
                                map.GetActorAt(point) != null || map.XpdBaseAt(point) != null) continue;
                            map.PlaceActorAt(leader, point);
                            string reason;
                            List<Point> preview = XpdBasePlanner.Preview(map, leader, m_Rules, out reason);
                            map.PlaceActorAt(leader, origin);
                            if (preview == null || (m_Player != null && m_Player.Location.Map == map && preview.Contains(m_Player.Location.Position))) continue;
                            List<Point> free = preview.FindAll(p => zone.Bounds.Contains(p) && map.GetTileAt(p).IsInside &&
                                map.IsWalkable(p.X, p.Y) && map.GetActorAt(p) == null);
                            if (free.Count < 3) continue;
                            cells = preview; places = free;
                        }
                    if (cells == null) continue;
                    Actor second = available[1], third = available[2];
                    map.PlaceActorAt(leader, places[0]); map.PlaceActorAt(second, places[1]); map.PlaceActorAt(third, places[2]);
                    while (m_Rules.ActorMaxFollowers(leader) < 2)
                        leader.Sheet.SkillTable.AddOrIncreaseSkill((int)Skills.IDs.LEADERSHIP);
                    leader.AddFollower(second); leader.AddFollower(third);
                    string name = leader.UnmodifiedName + "'s circle";
                    if (!names.Add(name)) { name += " (" + (established + 1) + ")"; names.Add(name); }
                    leader.SocialGroup.CommunityName = name;
                    XpdBase shelter = new XpdBase(leader, cells);
                    map.AddXpdBase(shelter);
                    // Give existing storage/supply behavior a room; no supplies are created.
                    shelter.SetFoodRoom(XpdBasePlanner.RoomAt(map, shelter, places[0]));
                    InitializeCommunityHome(leader);
                    foreach (Actor member in new[] { leader, second, third })
                        CommunityKnowledgeSystem.RefreshKnownSurroundings(this, member);
                    available.RemoveRange(0, 3); established++;
                }
                if (established == 3) break;
            }
            return established;
        }

        internal void BeginCommunityConversation() { ClearMessages(); }

        internal void CommunityControlHint()
        {
            AddCommunitySpeech(String.Format("Community: {0} talks; {1} then Tab shows dated community reports.",
                s_KeyBindings.Get(PlayerCommand.TALK), s_KeyBindings.Get(PlayerCommand.HEARD_JOURNAL)),
                Color.LightSkyBlue, true);
            AddCommunitySpeech(String.Format("Hover an inventory item, press {0}, then choose a direction to give. {1}, then Y/N answers requests.",
                s_KeyBindings.Get(PlayerCommand.GIVE_ITEM), s_KeyBindings.Get(PlayerCommand.TALK)),
                Color.LightSkyBlue, true);
        }

        internal void AddCommunitySpeech(string text, Color color, bool presentNextTurn = false)
        {
            // Fit the actual message area, and keep earlier lines rather than clearing the whole exchange.
            int width = LOCATIONPANEL_X - MESSAGES_X - 8 -
                m_UI.UI_BoldTextWidth(m_Session.WorldTime.TurnCounter + " ");
            while (text.Length > 0)
            {
                int length = text.Length;
                while (length > 1 && m_UI.UI_BoldTextWidth(text.Substring(0, length)) > width) length--;
                if (length < text.Length)
                {
                    int space = text.LastIndexOf(' ', length - 1, length);
                    if (space > 0) length = space;
                }
                if (m_MessageManager.Count >= MAX_MESSAGES) m_MessageManager.RemoveFirstMessage();
                Message message = new Message(text.Substring(0, length), m_Session.WorldTime.TurnCounter, color);
                AddMessage(message);
                if (presentNextTurn)
                {
                    if (m_PendingEncounterSpeech == null) m_PendingEncounterSpeech = new List<Message>();
                    m_PendingEncounterSpeech.Add(message);
                    if (m_PendingEncounterSpeech.Count > MAX_MESSAGES) m_PendingEncounterSpeech.RemoveAt(0);
                }
                text = text.Substring(length).TrimStart();
            }
        }

        IEnumerable<string> KnownAffiliationLines(Actor actor)
        {
            if (m_Player == null || m_Player.Personality == null) yield break;
            KnownCommunity latest = null;
            CommunityMember member = null;
            foreach (KnownCommunity known in m_Player.Personality.Communities)
                foreach (CommunityMember candidate in known.Members)
                    if (candidate.Id == actor.PersonalityIdentity && (member == null || candidate.Turn > member.Turn ||
                        (candidate.Turn == member.Turn && (!(candidate.Status ?? "").StartsWith("left") || (member.Status ?? "").StartsWith("left")))))
                    { latest = known; member = candidate; }
            if (member == null) yield break;
            yield return "Last known community: " + latest.Name + ((member.Status ?? "").StartsWith("left") ? " (left)" : "");
            yield return "Turn " + member.Turn + " | " + member.Source;
        }

        IEnumerable<string> LastReportedShelterLines(Map map, Point position)
        {
            if (m_Player == null || m_Player.Personality == null) yield break;
            foreach (KnownCommunity known in m_Player.Personality.Communities)
                if (known.ShelterLocation != null && known.ShelterLocation.Matches(map, position))
                {
                    yield return "Last reported home of " + known.Name;
                    yield return "Turn " + known.ShelterTurn + " | " + known.ShelterSource;
                }
        }

        string ClaimedSupplyWarning(Map map, Point position, Item item = null)
        {
            if (m_Player == null || map == null || (item != null && item.LastDroppedBy == m_Player)) return null;
            XpdBase shelter = map.XpdBaseAt(position);
            return shelter == null || shelter.Owns(m_Player) ? null :
                "Claimed supplies—taking them may provoke the owners.";
        }

        string GroundInventoryTitle()
        {
            Map map = m_Player.Location.Map;
            Inventory items = map.GetItemsAt(m_Player.Location.Position);
            return items != null && !items.IsEmpty && ClaimedSupplyWarning(map, m_Player.Location.Position) != null ?
                "Claimed supplies" : "Items on ground";
        }
    }
}
