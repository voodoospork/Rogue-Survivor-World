using System;
using System.Collections.Generic;
using System.Drawing;

namespace djack.RogueSurvivor.Data
{
    // NPC territory follows a stable community identity; player territory remains personal.
    [Serializable]
    class XpdBase
    {
        Actor m_GroupLeader;
        [System.Runtime.Serialization.OptionalField] SocialGroup m_CommunityGroup;
        [System.Runtime.Serialization.OptionalField] Guid m_CommunityIdentity;
        readonly Faction m_Faction;
        readonly List<Point> m_Cells;
        // Null for the original claim and for saves made before linked levels existed.
        readonly XpdBase m_Root;
        Rectangle? m_FoodRoom;
        Rectangle? m_WeaponRoom;

        public IEnumerable<Point> Cells { get { return m_Cells; } }
        public Actor GroupLeader { get { return m_GroupLeader; } }
        public Faction Faction { get { return m_Faction; } }
        public Rectangle? FoodRoom { get { return m_FoodRoom; } }
        public Rectangle? WeaponRoom { get { return m_WeaponRoom; } }
        public XpdBase Root { get { return m_Root ?? this; } }

        public XpdBase(Actor claimant, IEnumerable<Point> cells)
            : this(claimant, cells, null)
        {
        }

        public XpdBase(Actor claimant, IEnumerable<Point> cells, XpdBase linkedBase)
        {
            if (claimant == null || claimant.Model.Abilities.IsUndead)
                throw new ArgumentException("A living actor must claim the base");
            m_GroupLeader = claimant.IsPlayer ? claimant : claimant.GroupRoot;
            BindCommunity();
            m_Faction = claimant.Faction;
            if (linkedBase != null && !linkedBase.Owns(claimant))
                throw new ArgumentException("Actor does not own linked base");
            m_Root = linkedBase == null ? null : linkedBase.Root;
            m_Cells = new List<Point>(cells);
            if (m_Cells.Count == 0) throw new ArgumentException("Base needs cells");
        }

        public bool Owns(Actor actor)
        {
            if (actor == null || actor.Model.Abilities.IsUndead) return false;
            BindCommunity();
            if (m_CommunityIdentity != Guid.Empty)
                return actor.SocialGroup != null && actor.SocialGroup.Identity == m_CommunityIdentity;
            if (m_GroupLeader == null) return false;
            for (Actor parent = actor; parent != null; parent = parent.Leader)
                if (parent == m_GroupLeader) return true;
            return false;
        }

        public Guid CommunityIdentity { get { BindCommunity(); return m_CommunityIdentity; } }
        public SocialGroup CommunityGroup { get { BindCommunity(); return m_CommunityGroup; } }
        void BindCommunity()
        { if (m_GroupLeader != null && !m_GroupLeader.IsPlayer && m_GroupLeader.SocialGroup != null &&
                (m_CommunityIdentity == Guid.Empty || m_CommunityIdentity == m_GroupLeader.SocialGroup.Identity)) {
            m_CommunityGroup = m_GroupLeader.SocialGroup; m_CommunityIdentity = m_CommunityGroup.Identity; } }
        public bool CanManage(Actor actor) { return actor == m_GroupLeader; }
        public void TransferCommunity(Actor previous, Actor successor, Guid identity)
        { if (m_GroupLeader == previous && CommunityIdentity == identity) m_GroupLeader = successor; }
        public void RebindCommunity(Actor chief, Guid identity) { if (CommunityIdentity == identity) m_GroupLeader = chief; }
        internal void EstablishCommunity(Actor chief, SocialGroup group)
        { m_GroupLeader = chief; m_CommunityGroup = group; m_CommunityIdentity = group.Identity; }

        public bool Contains(Point point) { return m_Cells.Contains(point); }
        public bool IsPartOf(XpdBase other) { return other != null && Root == other.Root; }

        public void SetFoodRoom(Rectangle room) { SetRoom(room); m_FoodRoom = room; }
        public void SetWeaponRoom(Rectangle room) { SetRoom(room); m_WeaponRoom = room; }

        void SetRoom(Rectangle room)
        {
            if (room.Width <= 0 || room.Height <= 0) throw new ArgumentException("Empty storage room");
            foreach (Point cell in m_Cells)
                if (room.Contains(cell)) return;
            throw new ArgumentException("Storage room is outside the base");
        }
    }
}
