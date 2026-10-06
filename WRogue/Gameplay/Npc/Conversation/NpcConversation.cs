using System;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    static class NpcConversation
    {
        public static void BroadcastReport(RogueGame game, SignificantEvent source, NpcEventDefinition definition)
        {
            if (definition == null || !definition.AudibleReport || source.Subject == null ||
                source.Subject.Personality == null) return;
            NpcFact fact = source.Subject.Personality.Knowledge.Facts.Find(f => f.EventId == source.Id && f.Kind == source.Kind);
            if (fact == null) return;
            foreach (Actor hearer in source.Map.Actors)
            {
                if (hearer == source.Subject || hearer.IsDead || hearer.IsSleeping ||
                    !hearer.Model.Abilities.IsIntelligent || hearer.Model.Abilities.IsUndead ||
                    game.Rules.StdDistance(hearer.Location.Position, source.Position) > hearer.AudioRange) continue;
                if (hearer.Personality == null) hearer.Personality = new PersonalityState();
                NpcFact spoken = fact.SpeechSnapshot();
                NpcKnowledgeSystem.Hear(game, hearer, source.Subject, spoken);
                if (fact.DiplomacyMessage != null && source.Subject.SocialGroup != null &&
                    fact.DiplomacyMessage.Community == source.Subject.SocialGroup.Identity && game.IdentifiesDiplomacySpeaker(hearer,source.Subject))
                    hearer.Personality.LearnCommunity(fact.DiplomacyMessage.Community,fact.DiplomacyMessage.Name,source.Turn,"heard declaration")
                        .Member(new CommunityMember { Id=source.Subject.PersonalityIdentity, Name=source.Subject.UnmodifiedName, Status="spoke for community",
                            Turn=source.Turn, Source="heard from visible speaker" });
            }
        }

        public static void OfferPlayerReply(RogueGame game, SignificantEvent source, NpcEventDefinition definition)
        {
            Actor player = source.Other != null && source.Other.IsPlayer ? source.Other : game.Player;
            if (definition == null || definition.PlayerReply == null || player == null || !player.IsPlayer ||
                player.IsSleeping || source.Subject == null || source.Subject == player || player.Location.Map != source.Map)
                return;
            bool overheard = player != source.Other;
            if (overheard && (!definition.AudibleReport ||
                game.Rules.StdDistance(player.Location.Position, source.Position) > player.AudioRange)) return;
            if (player.Personality == null) player.Personality = new PersonalityState();
            player.Personality.Reactions.RemoveAll(r => r.Deadline < source.Turn);
            if (player.Personality.Reactions.Count >= 4 ||
                player.Personality.Reactions.Exists(r => r.CauseId == source.Id)) return;
            player.Personality.Reactions.Add(new NpcReaction(source.Subject, definition.PlayerReply.Prompt,
                source.Id, source.Turn, source.Kind, source.StoryId, overheard: overheard));
        }

        public static NpcReaction Pending(Actor player, Actor target)
        {
            if (player == null || player.Personality == null || target == null ||
                !Session.Get.GamePreset.NpcPersonalitiesEnabled) return null;
            int turn = player.Location.Map.LocalTime.TurnCounter;
            player.Personality.Reactions.RemoveAll(r => r.Deadline < turn);
            return player.Personality.Reactions.Find(r => r.TargetId == target.PersonalityIdentity);
        }

        public static void Reply(RogueGame game, Actor player, Actor target, NpcReaction pending, bool yes)
        {
            NpcEventDefinition definition = game.NpcContent.Event(pending.Kind);
            NpcPlayerReply reply = definition == null ? null : definition.PlayerReply;
            player.Personality.Reactions.Remove(pending);
            if (reply == null) return;
            game.DoSay(player, target, yes ? reply.YesText : reply.NoText, RogueGame.Sayflags.IS_STORY,
                pending.CauseId, pending.StoryId);
            Actor beneficiary = ReplyBeneficiary(target, pending);
            NpcEvents.Publish(game, yes ? reply.YesKind : reply.NoKind, player, yes && beneficiary != null ? beneficiary : target, pending.CauseId, pending.StoryId);
            game.DoSay(target, player, yes ? "Thank you. I'll remember." : "I understand.", RogueGame.Sayflags.IS_FREE_ACTION);
        }
        public static Actor ReplyBeneficiary(Actor target, NpcReaction pending)
        {
            if (pending.CommunityBeneficiary == Guid.Empty) return target;
            foreach (Actor member in target.PartyTree()) if (member.PersonalityIdentity == pending.CommunityBeneficiary && !member.IsDead) return member;
            return null;
        }

        public static NpcFact Rumor(Actor speaker, Actor listener)
        {
            if (speaker.Personality == null || !Session.Get.GamePreset.NpcPersonalitiesEnabled) return null;
            int turn = speaker.Location.Map.LocalTime.TurnCounter;
            NpcFact chosen = null;
            foreach (NpcFact fact in speaker.Personality.Knowledge.Facts)
                if (fact.Place.Map != null && fact.Confidence >= 40 && fact.Hops < 3 &&
                    turn - fact.EventTurn <= 2 * WorldTime.TURNS_PER_DAY &&
                    !speaker.Personality.Knowledge.WasTold(fact.EventId, listener.PersonalityIdentity) &&
                    (listener.Personality == null || !listener.Personality.Knowledge.Facts.Exists(known =>
                        known.EventId == fact.EventId && known.Kind == fact.Kind)) &&
                    (chosen == null || fact.EventTurn > chosen.EventTurn)) chosen = fact;
            return chosen;
        }

        public static void ShareRumor(RogueGame game, Actor speaker, Actor listener, NpcFact fact, bool free)
        {
            if ((fact.Kind == "food_cache" || fact.Kind == "medicine_cache") && (fact.SupplyLocation != null || fact.Place.Map != null))
            {
                ScavengingSystem.Share(game, speaker, listener, ScavengingSystem.ReportSnapshot(fact));
                if (!free) game.SpendActorActionPoints(speaker, Rules.BASE_ACTION_COST);
                NpcEvents.Publish(game, "rumor_shared", speaker, listener, fact.EventId, fact.StoryId);
                return;
            }
            string report = NpcRecordDescriptions.Report(game.NpcContent, fact);
            if (fact.DiplomacyEvidence != null)
                report = fact.DiplomacyEvidence.Explanation + "; " + (fact.SubjectName ?? "someone") + " (" +
                    (fact.DiplomacyEvidence.Subject.Name ?? "unknown affiliation") + "), " + (fact.OtherName ?? "someone") + " (" +
                    (fact.DiplomacyEvidence.Other.Name ?? "unknown affiliation") + "), turn " + fact.EventTurn;
            if (fact.DiplomacyMessage != null)
                report = fact.DiplomacyMessage.Name + " reports " + fact.DiplomacyMessage.Stance + " with " + fact.DiplomacyMessage.Target.Name +
                    "; " + fact.DiplomacyMessage.Reason + "; report turn " + fact.DiplomacyMessage.Turn +
                    (fact.DiplomacyMessage.PeaceTurn > 0 ? "; peace ratified turn " + fact.DiplomacyMessage.PeaceTurn : "");
            District district = fact.Place.Map.District;
            string place = district == null ? "near " + fact.Place.Map.Name :
                "in district " + World.CoordToString(district.WorldPosition.X, district.WorldPosition.Y);
            Zone building = Zone.BuildingAt(fact.Place);
            if (building != null) place += ", at the " + Zone.BuildingLabel(building.BuildingKind);
            game.DoSay(speaker, listener, (fact.Source == NpcKnowledgeSource.Told ? "I was told that " :
                fact.Source == NpcKnowledgeSource.Inferred ? "As far as I know, " : "I saw that ") + report +
                " " + place + ".", RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_RUMOR |
                (free ? RogueGame.Sayflags.IS_FREE_ACTION : RogueGame.Sayflags.NONE), fact.EventId, fact.StoryId);
            foreach (Actor hearer in speaker.Location.Map.Actors)
            {
                if (hearer == speaker || hearer.IsDead || hearer.IsSleeping || !hearer.Model.Abilities.IsIntelligent ||
                    hearer.Model.Abilities.IsUndead || game.Rules.StdDistance(hearer.Location.Position, speaker.Location.Position) > hearer.AudioRange) continue;
                if (hearer.Personality == null) hearer.Personality = new PersonalityState();
                NpcKnowledgeSystem.Hear(game, hearer, speaker, fact.SpeechSnapshot());
            }
            int turn = speaker.Location.Map.LocalTime.TurnCounter;
            speaker.Personality.Knowledge.Told(fact.EventId, listener.PersonalityIdentity, turn);
            speaker.Personality.Knowledge.NextTalkTurn = turn + 30;
            NpcEvents.Publish(game, "rumor_shared", speaker, listener, fact.EventId, fact.StoryId);
        }
    }
}
