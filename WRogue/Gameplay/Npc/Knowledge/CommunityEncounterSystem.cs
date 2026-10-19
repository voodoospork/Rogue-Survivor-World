using System.Collections.Generic;
using djack.RogueSurvivor.Data;
using djack.RogueSurvivor.Engine;
using djack.RogueSurvivor.Engine.Actions;

namespace djack.RogueSurvivor.Gameplay.Personality
{
    enum CommunityTone { Welcoming, Neutral, Wary, Hostile }

    static class CommunityEncounterSystem
    {
        static Actor VisiblePlayer(RogueGame game, Actor owner, IList<Actor> visible)
        {
            if (!NpcIntentSystem.Enabled(owner) || owner.IsSleeping || !owner.Model.Abilities.CanTalk ||
                owner.SocialGroup == null || visible == null) return null;
            foreach (Actor player in visible)
                if (player.IsPlayer && !player.IsDead && !player.IsSleeping && !player.Model.Abilities.IsUndead &&
                    player.SocialGroup != owner.SocialGroup && NpcKnowledgeSystem.Visible(game, owner, player.Location)) return player;
            return null;
        }

        // Observe even when equipment, orders or survival prevent a call this turn.
        public static void ObserveContact(RogueGame game, Actor owner, IList<Actor> visible)
        {
            Actor player = VisiblePlayer(game, owner, visible);
            if (player == null) return;
            game.InitializeCommunityHome(owner);
            int turn = game.Session.WorldTime.TurnCounter;
            SocialGroup group = owner.SocialGroup;
            CommunityContactState contact = group.CommunityContact;
            if (contact == null || contact.PlayerId != player.PersonalityIdentity)
                group.CommunityContact = contact = new CommunityContactState { PlayerId = player.PersonalityIdentity };
            else if (turn - contact.LastSeenTurn >= 30)
            { contact.NeedsGreeting = true; contact.HostileWarningIssued = false; }
            contact.LastSeenTurn = turn;
        }

        internal static CommunityTone Tone(RogueGame game, Actor speaker, Actor player)
        {
            if (game.Rules.IsKnownEnemy(speaker, player)) return CommunityTone.Hostile;
            int score = NpcValues.KnownAttitude(speaker, player.PersonalityIdentity, game.NpcContent.Personalities) +
                PersonalitySystem.Bias(speaker, DecisionKind.Group, registry: game.NpcContent.Personalities) +
                PersonalitySystem.Bias(speaker, DecisionKind.Compassion, registry: game.NpcContent.Personalities) / 2;
            score += CommunityCultureSystem.GreetingModifier(speaker);
            return score >= 10 ? CommunityTone.Welcoming : score <= -10 ? CommunityTone.Wary : CommunityTone.Neutral;
        }

        internal static bool CanCall(RogueGame game, Actor speaker, Actor player)
        {
            if (player == null || !NpcIntentSystem.Enabled(speaker) || speaker.IsSleeping ||
                !speaker.Model.Abilities.CanTalk || player.IsSleeping || player.IsDead || player.Model.Abilities.IsUndead ||
                speaker.SocialGroup == null || speaker.SocialGroup == player.SocialGroup ||
                !NpcKnowledgeSystem.Visible(game, speaker, player.Location) ||
                game.Rules.GridDistance(speaker.Location.Position, player.Location.Position) > 6 ||
                game.Rules.StdDistance(speaker.Location.Position, player.Location.Position) > player.AudioRange) return false;
            CommunityContactState contact = speaker.SocialGroup.CommunityContact;
            if (contact == null || contact.PlayerId != player.PersonalityIdentity) return false;
            bool hostile = Tone(game, speaker, player) == CommunityTone.Hostile;
            if (hostile && contact.Called && !contact.NeedsGreeting && !contact.LastCallHostile && !contact.HostileWarningIssued)
                return true;
            return contact.NeedsGreeting && (!contact.Called || game.Session.WorldTime.TurnCounter - contact.LastCallTurn >= 60);
        }

        public static ActorAction Select(RogueGame game, Actor owner, IList<Actor> visible, bool hostileOnly)
        {
            Actor player = VisiblePlayer(game, owner, visible);
            if (player == null || !CanCall(game, owner, player)) return null;
            bool hostile = Tone(game, owner, player) == CommunityTone.Hostile;
            if (hostile != hostileOnly) return null;
            if (game.Rules.IsActorStarving(owner) || game.Rules.IsActorTired(owner) || game.Rules.IsActorSleepy(owner)) return null;
            if (hostile)
            {
                // A distant adversary can receive one warning; immediate threats retain priority.
                if (owner.HitPoints < game.Rules.ActorMaxHPs(owner) || owner.Infection > 0 || game.Rules.IsActorHungry(owner)) return null;
                foreach (Actor actor in visible)
                    if (game.Rules.IsKnownEnemy(owner, actor) && (actor != player ||
                        game.Rules.GridDistance(owner.Location.Position, actor.Location.Position) <= 1)) return null;
            }
            return new ActionCommunityCall(owner, game, player);
        }

        internal static void Call(RogueGame game, Actor speaker, Actor player)
        {
            if (!CanCall(game, speaker, player)) return;
            CommunityTone tone = Tone(game, speaker, player);
            SocialGroup group = speaker.SocialGroup;
            CommunityContactState contact = group.CommunityContact;
            contact.Called = true; contact.NeedsGreeting = false;
            contact.LastCallTurn = game.Session.WorldTime.TurnCounter;
            contact.LastCallHostile = tone == CommunityTone.Hostile;
            if (contact.LastCallHostile) contact.HostileWarningIssued = true;
            string name = group.CommunityName;
            string text = tone == CommunityTone.Welcoming ? "Hello! I'm with " + name + ". Come talk." :
                tone == CommunityTone.Wary ? "We're " + name + ". Keep your distance." :
                tone == CommunityTone.Hostile ? "Stay back! We're " + name + "!" : "We're " + name + ". Looking for someone?";
            XpdBase shelter = speaker.Location.Map.XpdBaseAt(speaker.Location.Position);
            string rule = CommunityCultureSystem.SpokenRules(speaker);
            if (rule.Length > 0) text += " " + rule;
            else if (shelter != null && shelter.Owns(speaker)) text += " This is our shelter. Leave our supplies alone.";
            DiplomacyMessage news = null;
            if (speaker.Personality != null) foreach (KnownDiplomacy known in speaker.Personality.KnownDiplomacy)
                if (known.Message.Community == group.Identity && known.Message.Target.Group != System.Guid.Empty &&
                    (known.Message.Feud || known.Message.PeaceTurn > 0) && (news == null || known.Message.Turn > news.Turn))
                    news = known.Message.Copy();
            if (news != null)
            {
                text += " Last report: " + (news.Feud ? "blood feud" : "peace agreed") + " with " + news.Target.Name +
                    ", turn " + news.Turn + ". Ask our leader for the account.";
                // The short call states no incident details. Teach precisely that spoken snapshot.
                news.Cause = null; news.Reason = "stance reported in community call; details not supplied";
            }
            RogueGame.Sayflags flags = RogueGame.Sayflags.IS_STORY | RogueGame.Sayflags.IS_COMMUNITY_CALL;
            if (tone == CommunityTone.Hostile) flags |= RogueGame.Sayflags.IS_SHOUT | RogueGame.Sayflags.IS_DANGER;
            game.DoSay(speaker, player, text, flags);
            if(rule.Length>0)CommunityCultureSystem.HearCallRules(game,speaker);
            if (news != null) CommunityDiplomacySystem.Publish(game, speaker, news, news.Feud ? "community_feud" : "community_peace");
        }
    }
}
