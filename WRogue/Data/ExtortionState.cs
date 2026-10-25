using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    enum RobberyGoods { Food, Medicine, Weapon }
    [Serializable] sealed class RobberyDemand
    {
        public Guid Id, Robber, Victim;
        public RobberyGoods Goods;
        public int Units, Expires, Pressure, RobberHP, VictimHP;
        public RobberyDemand Copy() { return (RobberyDemand)MemberwiseClone(); }
    }
    partial class Actor
    {
        [OptionalField] internal RobberyDemand Robbing, BeingRobbed;
        [OptionalField] internal int NextRobberyWorldTurn;
    }
    // Each community keeps its own dated account. Only a face-to-face exchange
    // updates both accounts; no remote stock or membership links are stored.
    [Serializable] sealed class CommunityTribute
    {
        public Guid Id, Collector, Payer;
        public string CollectorName, PayerName;
        public int Units, Remaining, Due, GraceUntil, Updated;
        public bool Accepted, Ended;
        public CommunityTribute Copy() { return (CommunityTribute)MemberwiseClone(); }
    }
    partial class SocialGroup
    {
        [OptionalField] List<CommunityTribute> m_Tributes;
        [OptionalField] internal int NextTributeWorldTurn;
        internal bool HasTributeAccounts { get { return m_Tributes != null && m_Tributes.Count > 0; } }
        internal List<CommunityTribute> Tributes { get { return m_Tributes ?? (m_Tributes = new List<CommunityTribute>()); } }
    }
}
