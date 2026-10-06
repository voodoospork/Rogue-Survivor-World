using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    [Serializable] sealed class FollowerTransition
    {
        public CommunityShelterLocation From, To;
        public int Turn;
        public string Source;
    }
    [Serializable] sealed class FollowerTravelState
    {
        public Guid Leader;
        public CommunityShelterLocation Contact;
        public string Source, Status = "following", Warning;
        public int ContactTurn, StationarySince, SearchUntil = -1, RetryAt, WarningTurn = -1000;
        public bool LeaderRunning, Recalling;
        public Point? SearchTarget;
        public readonly List<Point> Searched = new List<Point>();
        public readonly List<FollowerTransition> Transitions = new List<FollowerTransition>();
        public void Learn(Actor leader, int turn, string source)
        {
            bool changed = Contact == null || !Contact.Matches(leader.Location.Map, leader.Location.Position);
            if (changed) { StationarySince = leader.Location.Map.LocalTime.TurnCounter; RetryAt = 0; }
            Contact = PointAt(leader.Location); Leader = leader.PersonalityIdentity;
            ContactTurn = turn; Source = source; LeaderRunning = leader.IsRunning;
            SearchUntil = -1; SearchTarget = null; Searched.Clear();
            if (changed) Warning = null;
        }
        public void Transition(Location from, Location to, int turn, string source)
        {
            var a = PointAt(from); var b = PointAt(to);
            if (Transitions.Count > 0 && Transitions[Transitions.Count - 1].Turn == turn &&
                Transitions[Transitions.Count - 1].From.Matches(from.Map, from.Position)) return;
            Transitions.Add(new FollowerTransition { From = a, To = b, Turn = turn, Source = source });
            while (Transitions.Count > 16) Transitions.RemoveAt(0);
            Contact = b.Copy(); ContactTurn = turn; Source = source; RetryAt = 0;
            SearchUntil = -1; SearchTarget = null; Searched.Clear();
        }
        public static CommunityShelterLocation PointAt(Location place)
        { return new CommunityShelterLocation { MapSeed = place.Map.Seed, MapName = place.Map.Name,
            District = place.Map.District == null ? (Point?)null : place.Map.District.WorldPosition, Anchor = place.Position }; }
    }
    [Serializable] sealed class FollowerReport
    {
        public Guid Identity;
        public string Name, Status, Location, Source;
        public int Turn;
    }
    partial class Actor
    {
        [OptionalField] List<FollowerReport> m_FollowerReports;
        public IList<FollowerReport> FollowerReports { get { return m_FollowerReports ?? (m_FollowerReports = new List<FollowerReport>()); } }
        public void RecordFollower(FollowerReport report)
        {
            var old = FollowerReports as List<FollowerReport>;
            old.RemoveAll(r => r.Identity == report.Identity); old.Add(report);
            while (old.Count > 64) old.RemoveAt(0);
        }
    }
}
