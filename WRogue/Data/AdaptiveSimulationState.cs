using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using djack.RogueSurvivor.Engine;

namespace djack.RogueSurvivor.Data
{
    [Serializable] sealed class AdaptiveIdentityState
    {
        public Guid Namespace;
        public long Sequence;
        public readonly Dictionary<int, int> ModelCounts = new Dictionary<int, int>();
        internal static AdaptiveIdentityState Create(string scope)
        {
            using (var sha = SHA256.Create()) {
                byte[] bytes = new byte[16]; Array.Copy(sha.ComputeHash(Encoding.UTF8.GetBytes(scope)), bytes, 16);
                return new AdaptiveIdentityState { Namespace = new Guid(bytes) };
            }
        }
        internal Guid NewIdentity()
        {
            byte[] key = new byte[24]; Namespace.ToByteArray().CopyTo(key, 0); BitConverter.GetBytes(++Sequence).CopyTo(key, 16);
            using (var sha = SHA256.Create()) { byte[] bytes = new byte[16]; Array.Copy(sha.ComputeHash(key), bytes, 16); return new Guid(bytes); }
        }
        internal int NextModelCount(int model)
        { int count; ModelCounts.TryGetValue(model, out count); ModelCounts[model] = ++count; return count; }
    }
    [Serializable] sealed class AdaptiveMapState
    {
        public Map Map;
        public DiceRoller Random;
        public int TargetTurn, NextReviewTurn;
        [System.Runtime.Serialization.OptionalField] public int WorldTurn;
        [System.Runtime.Serialization.OptionalField] public bool HasWorldTurn;
        public long QuietTurns, DetailedTurns;
        [System.Runtime.Serialization.OptionalField] public bool DetailedInProgress;
        [System.Runtime.Serialization.OptionalField] public AdaptiveIdentityState Identities;
        [NonSerialized] internal int QuietUntil, QuietStamp;
        [NonSerialized] internal long QuietGeometry, QuietOccupancy;
    }
    [Serializable] sealed class AdaptiveDistrictState
    {
        public readonly List<AdaptiveMapState> Maps = new List<AdaptiveMapState>();
        public DiceRoller EventRandom;
        public int EventsThrough = -1;
        [System.Runtime.Serialization.OptionalField] public AdaptiveIdentityState Identities;
        [System.Runtime.Serialization.OptionalField] public List<AdaptiveArrival> Arrivals;
    }
    [Serializable] sealed class AdaptiveArrival
    {
        public Actor Actor;
        public Location From;
        public int DueTurn;
    }
}
