using System;
using System.Collections.Generic;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class CommunityKnowledgeSystem
    {
        static bool Enabled(RogueGame game) { return game.Session.GamePreset.NpcPersonalitiesEnabled; }
        static PersonalityState State(Actor actor) { return actor.Personality ?? (actor.Personality = new PersonalityState()); }
        internal static string PlaceLabel(Location place)
        {
            Zone building = Zone.BuildingAt(place);
            string label = building == null ? place.Map.Name : building.Name;
            District district = place.Map.District;
            return label + (district == null ? " on " + place.Map.Name : " in district " +
                World.CoordToString(district.WorldPosition.X, district.WorldPosition.Y)) +
                " (" + place.Position.X + "," + place.Position.Y + ")";
        }
        public static KnownCommunity RefreshKnownSurroundings(RogueGame game, Actor owner)
        {
            if (!Enabled(game) || owner.SocialGroup == null) return null;
            SocialGroup group = owner.SocialGroup;
            int turn = owner.Location.Map.LocalTime.TurnCounter;
            KnownCommunity record = State(owner).LearnCommunity(group.Identity, group.CommunityName, turn, "own group");
            if (owner.Community != null) record.IsTribe = true;
            foreach (Actor actor in owner.Location.Map.Actors)
            {
                if (actor != owner && !NpcKnowledgeSystem.Visible(game, owner, actor.Location)) continue;
                if (actor.SocialGroup != group) continue;
                record.Member(new CommunityMember { Id = actor.PersonalityIdentity, Name = actor.UnmodifiedName,
                    Status = actor.IsDead ? "dead" : "seen alive", Turn = turn, Source = "observed" });
                if (actor.PersonalityIdentity == group.LeaderId)
                { record.Leader = actor.UnmodifiedName; record.LeaderTurn = turn; record.LeaderSource = "observed"; }
            }
            XpdBase shelter = owner.Location.Map.XpdBaseAt(owner.Location.Position);
            if (shelter != null && shelter.Owns(owner))
            {
                record.Shelter = PlaceLabel(owner.Location); record.ShelterTurn = turn; record.ShelterSource = "observed claim";
                record.ShelterLocation = CommunityShelterLocation.At(owner.Location);
            }
            return record;
        }

        public static void Observe(NpcObservation observation)
        {
            SignificantEvent e = observation.Source;
            string text;
            string subject = observation.SeesSubject ? e.Subject.UnmodifiedName : "someone";
            string other = observation.SeesOther ? e.Other.UnmodifiedName : "someone";
            switch (e.Kind)
            {
                case "requested_food": text = subject + " asked for food."; break;
                case "requested_medicine": text = subject + " asked for medicine."; break;
                case "community_food_requested": text = subject + " asked for food for a companion."; break;
                case "community_medicine_requested": text = subject + " asked for medicine for a companion."; break;
                case "community_joined": text = subject + " joined the community."; break;
                case "community_left": text = subject + " left the community."; break;
                case "food_promised": text = subject + " promised food to " + other + "."; break;
                case "request_refused": text = subject + " refused " + other + "'s request."; break;
                case "helped": text = other + " gave supplies to " + subject + "."; break;
                case "shared_food": text = subject + " shared food with " + other + "."; break;
                case "shared_medicine": text = subject + " shared medicine with " + other + "."; break;
                case "promise_kept": text = subject + " kept a promise to " + other + "."; break;
                case "traded": case "bartered_food": text = subject + " traded supplies with " + other + "."; break;
                case "base_theft": text = subject + " took supplies from " + other + "'s shelter."; break;
                case "base_claimed": text = subject + " claimed a shelter at " + PlaceLabel(new Location(e.Map, e.Position)) + "."; break;
                case "base_loss": text = subject + " lost a shelter claim at " + PlaceLabel(new Location(e.Map, e.Position)) + "."; break;
                case "death": text = subject + " died."; break;
                case "group_succession": text = subject + " took over after " + other + "."; break;
                case "joined_group": text = subject + " joined " + other + "'s group."; break;
                case "left_group": text = subject + " left " + other + "'s group."; break;
                case "abandoned": text = subject + " left " + other + "'s group."; break;
                default: return;
            }
            Actor owner = observation.Owner;
            SocialGroup subjectGroup = e.Subject == null ? null : e.Subject.SocialGroup;
            SocialGroup otherGroup = e.Other == null ? null : e.Other.SocialGroup;
            var groups = new List<SocialGroup>();
            if (subjectGroup != null) groups.Add(subjectGroup);
            if (otherGroup != null && otherGroup != subjectGroup) groups.Add(otherGroup);
            foreach (SocialGroup group in groups)
            {
                KnownCommunity record = owner.Personality.FindCommunity(group.Identity);
                if (record == null && owner.SocialGroup == group)
                    record = owner.Personality.LearnCommunity(group.Identity, group.CommunityName, e.Turn, "own group");
                if (record == null) continue; // An event does not disclose an unknown affiliation.
                record.Note(new CommunityNote { EventId = e.Id, EventTurn = e.Turn, LearnedTurn = e.Turn,
                    Kind = e.Kind, Text = text, Source = observation.Direct ? "participant" : "witness" });
                if ((e.Kind == "joined_group" || e.Kind == "community_joined") && observation.SeesSubject && subjectGroup == group)
                    record.Member(new CommunityMember { Id = e.Subject.PersonalityIdentity, Name = subject,
                        Status = "joined", Source = observation.Direct ? "participant" : "witness", Turn = e.Turn });
                if ((e.Kind == "abandoned" || e.Kind == "left_group" || e.Kind == "community_left") && observation.SeesSubject && otherGroup == group && record.Members.Exists(m => m.Id == e.Subject.PersonalityIdentity))
                    record.Member(new CommunityMember { Id = e.Subject.PersonalityIdentity, Name = subject,
                        Status = "left", Source = observation.Direct ? "participant" : "witness", Turn = e.Turn });
                if (e.Kind == "community_joined" && observation.SeesSubject && subjectGroup == group)
                    foreach (Actor personal in e.Subject.PartyTree())
                        if (personal != e.Subject && NpcKnowledgeSystem.Visible(observation.Game,owner,personal.Location))
                            record.Member(new CommunityMember { Id=personal.PersonalityIdentity, Name=personal.UnmodifiedName, Status="joined with party", Turn=e.Turn, Source="witness" });
                if (e.Kind == "death" && observation.SeesSubject && subjectGroup == group)
                {
                    record.Member(new CommunityMember { Id = e.Subject.PersonalityIdentity, Name = subject,
                        Status = "died", Source = "witness", Turn = e.Turn });
                    if (group.LeaderId == e.Subject.PersonalityIdentity)
                    { record.Leader = subject + " (died)"; record.LeaderTurn = e.Turn; record.LeaderSource = "witness"; }
                }
                if (e.Kind == "group_succession" && observation.SeesSubject && subjectGroup == group)
                { record.Leader = subject; record.LeaderTurn = e.Turn; record.LeaderSource = "witness"; }
                if (e.Kind == "base_claimed" && observation.SeesSubject && subjectGroup == group)
                { record.Shelter = PlaceLabel(new Location(e.Map, e.Position)); record.ShelterTurn = e.Turn; record.ShelterSource = "witnessed claim";
                    record.ShelterLocation = CommunityShelterLocation.At(new Location(e.Map, e.Position)); }
                if (e.Kind == "base_loss" && observation.SeesSubject && subjectGroup == group)
                { record.Shelter = "Claim lost: " + PlaceLabel(new Location(e.Map, e.Position)); record.ShelterTurn = e.Turn; record.ShelterSource = "witness";
                    record.ShelterLocation = null; }
            }
        }

        public static bool Talk(RogueGame game, Actor speaker, Actor listener, bool allowRequest = true)
        {
            if (!Enabled(game) || speaker.SocialGroup == null || listener.IsSleeping || listener.IsDead ||
                game.Rules.StdDistance(speaker.Location.Position, listener.Location.Position) > listener.AudioRange) return false;
            game.InitializeCommunityHome(speaker);
            CommunityCultureSystem.Talk(game,speaker,listener);
            KnownCommunity known = RefreshKnownSurroundings(game, speaker);
            bool first = State(listener).FindCommunity(known.Identity) == null;
            RogueGame.Sayflags speech = RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_FREE_ACTION | RogueGame.Sayflags.IS_BRIEF;
            game.DoSay(speaker, listener, (first ? "I'm with " : "News from ") + known.Name + ". " +
                (known.Leader == null ? (known.IsTribe ? "Chief: unknown. " : "Leader: unknown. ") : "Last " + (known.IsTribe ? "chief" : "leader") + ": " + known.Leader + " (turn " + known.LeaderTurn + "). ") +
                (known.Shelter == null ? "Shelter: unknown." : "Last shelter: " + known.Shelter + " (turn " + known.ShelterTurn + ")."), speech);
            // Explicitly spoken, dated snapshots, copied rather than shared with the speaker.
            KnownCommunity received = State(listener).LearnCommunity(known.Identity, known.Name,
                speaker.Location.Map.LocalTime.TurnCounter, "introduced by " + speaker.UnmodifiedName);
            string source = "reported by " + speaker.UnmodifiedName;
            received.IsTribe = known.IsTribe;
            if (known.Leader != null && (received.Leader == null || known.LeaderTurn >= received.LeaderTurn))
            { received.Leader = known.Leader; received.LeaderTurn = known.LeaderTurn; received.LeaderSource = source; }
            if (known.Shelter != null && (received.Shelter == null || known.ShelterTurn >= received.ShelterTurn))
            { received.Shelter = known.Shelter; received.ShelterTurn = known.ShelterTurn; received.ShelterSource = source;
                received.ShelterLocation = known.ShelterLocation == null ? null : known.ShelterLocation.Copy(); }
            // V reports the speaker's known roster; retain its details in the dated journal rather than a long speech line.
            foreach (CommunityMember member in known.Members)
            { CommunityMember copy = member.Copy(); copy.Source = source; received.Member(copy); }
            CommunityNote news = null;
            CommunityDiplomacySystem.ShareNews(game, speaker, listener);
            for (int i = known.Notes.Count - 1; i >= 0; i--)
                if (!received.Knows(known.Notes[i]) && CommunityCultureSystem.CanRetellPolicy(speaker, listener, known.Notes[i]) &&
                    (known.Notes[i].Kind.StartsWith("community_") || known.Notes[i].Kind == "group_succession" || known.Notes[i].Kind == "base_loss"))
                { news = known.Notes[i]; break; }
            for (int i = known.Notes.Count - 1; news == null && i >= 0; i--)
                if (!received.Knows(known.Notes[i]) && CommunityCultureSystem.CanRetellPolicy(speaker, listener, known.Notes[i])) { news = known.Notes[i]; break; }
            if (news != null)
            {
                game.DoSay(speaker, listener, "At turn " + news.EventTurn + ": " + news.Text,
                    speech | RogueGame.Sayflags.IS_RUMOR, news.EventId);
                CommunityNote copy = news.Copy(); copy.Reported = true; copy.Source = source;
                copy.LearnedTurn = speaker.Location.Map.LocalTime.TurnCounter; received.Note(copy);
            }
            int turn = speaker.Location.Map.LocalTime.TurnCounter;
            if (allowRequest && game.Rules.IsActorHungry(speaker) && !NpcFoodSupply.HasFood(game, speaker) &&
                turn >= speaker.Personality.NextCommunityRequestTurn && NpcConversation.Pending(listener, speaker) == null)
            {
                speaker.Personality.NextCommunityRequestTurn = turn + 60;
                game.DoSay(speaker, listener, "I'm hungry and have no usable food. Can you bring me food within 180 turns?",
                    speech | RogueGame.Sayflags.IS_REQUEST);
                NpcEvents.Publish(game, "requested_food", speaker, listener);
            }
            else if (allowRequest && speaker.HitPoints < game.Rules.ActorMaxHPs(speaker) / 2 &&
                !speaker.Inventory.HasItemMatching(i => i is djack.RogueSurvivor.Engine.Items.ItemMedicine) &&
                turn >= speaker.Personality.NextCommunityRequestTurn && NpcConversation.Pending(listener, speaker) == null)
            {
                speaker.Personality.NextCommunityRequestTurn = turn + 60;
                game.DoSay(speaker, listener, "I'm badly injured and have no medicine. Can you bring me medicine within 180 turns?", speech | RogueGame.Sayflags.IS_REQUEST);
                NpcEvents.Publish(game, "requested_medicine", speaker, listener);
            }
            else if (allowRequest && OfferCommunityRequest(game, speaker, listener, turn, speech)) { }
            else if (game.Rules.IsActorHungry(speaker) && NpcFoodSupply.HasFood(game, speaker))
                game.DoSay(speaker, listener, "I'm hungry, but I have food to eat.", speech);
            else if (news == null)
                game.DoSay(speaker, listener, "I have no new need or event to report right now.", speech);
            return true;
        }

        static bool OfferCommunityRequest(RogueGame game, Actor speaker, Actor listener, int turn, RogueGame.Sayflags speech)
        {
            if (!listener.IsPlayer || !CommunityMembershipSystem.IsLeader(game, speaker) ||
                turn < speaker.Personality.NextCommunityRequestTurn || NpcConversation.Pending(listener, speaker) != null) return false;
            foreach (Actor member in speaker.CommunityResidents)
            {
                if (member == speaker || member.IsDead || member.InPlayerParty || !NpcKnowledgeSystem.Visible(game, speaker, member.Location)) continue;
                bool food = game.Rules.IsActorHungry(member) && !NpcFoodSupply.HasFood(game, member);
                bool medical = member.HitPoints < game.Rules.ActorMaxHPs(member) / 2 &&
                    !member.Inventory.HasItemMatching(i => i is djack.RogueSurvivor.Engine.Items.ItemMedicine);
                if (!food && !medical) continue;
                speaker.Personality.NextCommunityRequestTurn = turn + 60;
                string need = food ? "food" : "medicine";
                game.DoSay(speaker, listener, member.UnmodifiedName + " needs " + need + ". Give it to " + member.UnmodifiedName +
                    ". Can you deliver within 180 turns?", speech | RogueGame.Sayflags.IS_REQUEST);
                SignificantEvent request = NpcEvents.Publish(game, food ? "community_food_requested" : "community_medicine_requested", speaker, listener);
                listener.Personality.Reactions.RemoveAll(r => r.TargetId == speaker.PersonalityIdentity && r.CauseId == request.Id);
                listener.Personality.Reactions.Add(new NpcReaction(speaker, need + " for " + member.UnmodifiedName + " within 180 turns",
                    request.Id, turn, request.Kind) { CommunityBeneficiary = member.PersonalityIdentity });
                return true;
            }
            return false;
        }

        public static void HearCall(RogueGame game, Actor speaker, Actor listener, bool identified)
        {
            if (!Enabled(game) || speaker.SocialGroup == null) return;
            SocialGroup group = speaker.SocialGroup;
            int turn = speaker.Location.Map.LocalTime.TurnCounter;
            bool first = State(listener).FindCommunity(group.Identity) == null;
            string source = identified ? "introduced by " + speaker.UnmodifiedName : "heard an unidentified speaker";
            KnownCommunity received = State(listener).LearnCommunity(group.Identity, group.CommunityName, turn, source);
            if (identified)
            {
                received.Member(new CommunityMember { Id = speaker.PersonalityIdentity, Name = speaker.UnmodifiedName,
                    Status = "seen alive", Turn = turn, Source = source });
                XpdBase shelter = speaker.Location.Map.XpdBaseAt(speaker.Location.Position);
                if (shelter != null && shelter.Owns(speaker) && (received.Shelter == null || turn >= received.ShelterTurn))
                {
                    received.Shelter = PlaceLabel(speaker.Location); received.ShelterTurn = turn; received.ShelterSource = source;
                    received.ShelterLocation = CommunityShelterLocation.At(speaker.Location);
                }
            }
            if (first && listener.IsPlayer) game.CommunityControlHint();
        }
    }
}
