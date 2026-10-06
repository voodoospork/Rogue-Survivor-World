using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    // No Actor, Map or SocialGroup references: viewing a record cannot reveal hidden changes.
    [Serializable]
    sealed class CommunityNote
    {
        public long EventId;
        public int EventTurn, LearnedTurn;
        public string Kind, Text, Source;
        public bool Reported;
        public CommunityNote Copy() { return (CommunityNote)MemberwiseClone(); }
    }
    [Serializable]
    sealed class CommunityMember
    {
        public Guid Id;
        public string Name, Status, Source;
        public int Turn;
        public CommunityMember Copy() { return (CommunityMember)MemberwiseClone(); }
    }
    [Serializable]
    sealed class KnownCommunity
    {
        public Guid Identity;
        public string Name, Leader, LeaderSource, Shelter, ShelterSource;
        public string NameSource;
        public int NameTurn;
        public int LeaderTurn, ShelterTurn;
        [OptionalField] public CommunityShelterLocation ShelterLocation;
        public readonly List<CommunityMember> Members = new List<CommunityMember>();
        public readonly List<CommunityNote> Notes = new List<CommunityNote>();
        public void Member(CommunityMember member)
        {
            CommunityMember old = Members.Find(m => m.Id == member.Id);
            if (old != null && old.Turn > member.Turn) return;
            if (old != null && old.Turn == member.Turn && (old.Status == "died" || old.Status == "dead") && member.Status == "seen alive") return;
            if (old != null) Members.Remove(old);
            Members.Add(member);
            if (Members.Count > 16) Members.RemoveAt(0);
        }
        public bool Knows(CommunityNote note) { return Notes.Exists(n => n.EventId == note.EventId && n.Kind == note.Kind); }
        public void Note(CommunityNote note)
        {
            if (Knows(note)) return;
            Notes.Add(note);
            if (Notes.Count > 32) Notes.RemoveAt(0);
        }
    }
    [Serializable]
    sealed class CommunityShelterLocation
    {
        public int MapSeed;
        public string MapName;
        public Point? District;
        public Point Anchor;
        public Rectangle? BuildingBounds;
        public bool Matches(Map map, Point position)
        {
            return map != null && map.Seed == MapSeed && map.Name == MapName &&
                (map.District == null ? !District.HasValue : District == map.District.WorldPosition) &&
                (BuildingBounds.HasValue ? BuildingBounds.Value.Contains(position) : Anchor == position);
        }
        public CommunityShelterLocation Copy() { return (CommunityShelterLocation)MemberwiseClone(); }
        public static CommunityShelterLocation At(Location place)
        {
            Zone building = Zone.BuildingAt(place);
            return new CommunityShelterLocation { MapSeed = place.Map.Seed, MapName = place.Map.Name,
                District = place.Map.District == null ? (Point?)null : place.Map.District.WorldPosition,
                Anchor = place.Position, BuildingBounds = building == null ? (Rectangle?)null : building.Bounds };
        }
    }
    sealed partial class PersonalityState
    {
        [OptionalField] List<KnownCommunity> m_Communities;
        [OptionalField] internal int NextCommunityRequestTurn;
        public IList<KnownCommunity> Communities { get { return m_Communities ?? (m_Communities = new List<KnownCommunity>()); } }
        public KnownCommunity FindCommunity(Guid id) { return m_Communities == null ? null : m_Communities.Find(c => c.Identity == id); }
        public KnownCommunity LearnCommunity(Guid id, string name, int turn = 0, string source = "known affiliation")
        {
            KnownCommunity record = FindCommunity(id);
            if (record != null) return record;
            record = new KnownCommunity { Identity = id, Name = name, NameTurn = turn, NameSource = source };
            Communities.Add(record);
            if (Communities.Count > 32) Communities.RemoveAt(0);
            return record;
        }
    }
}
