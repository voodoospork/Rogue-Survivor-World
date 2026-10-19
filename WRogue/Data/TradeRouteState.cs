using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    // Physical lot metadata. The sender's record never shares this mutable marker.
    [Serializable] sealed class TradeCargoTag
    {
        public Guid Shipment, Courier, Sender, Recipient;
        public string SenderName, RecipientName;
        public bool Released;
    }

    // Identifiers and heard snapshots only; no Actor, Map, Inventory or Item references.
    [Serializable] sealed class TradeShipment
    {
        public Guid Id, Sender, Courier, Recipient;
        public string SenderName, CourierName, RecipientName, Resource, CargoName, PaymentName;
        public int CargoModel, PaymentModel, Units, PaymentUnits, Created, Due, Delivered, Returned;
        public int LastReceipt = -1, TimelyDelivered, Reported = -1;
        public string Status = "In transit", Report;
        public CommunityShelterLocation Destination, Origin;
        public TradeShipment Copy()
        {
            var copy = (TradeShipment)MemberwiseClone();
            copy.Destination = Destination.Copy(); copy.Origin = Origin.Copy(); return copy;
        }
    }

    [Serializable] sealed class TradeVisit
    {
        public Guid Person;
        public string Name, Text;
        public int Turn;
        public CommunityShelterLocation Place;
    }

    [Serializable] sealed class TradeRouteState
    {
        public TradeShipment Active;
        public TradeCargoTag Cargo;
        public readonly List<TradeShipment> History = new List<TradeShipment>();
        public readonly List<TradeShipment> Issued = new List<TradeShipment>();
        public readonly List<TradeVisit> Visits = new List<TradeVisit>();
        public void Visit(TradeVisit visit)
        {
            Visits.Add(visit); while (Visits.Count > 32) Visits.RemoveAt(0);
        }
        public void Close()
        {
            History.Add(Active); while (History.Count > 16) History.RemoveAt(0);
            if (Cargo != null) Cargo.Released = true;
            Active = null; Cargo = null;
        }
    }

    sealed partial class PersonalityState
    {
        [OptionalField] TradeRouteState m_TradeRoutes;
        public bool HasTradeRoutes { get { return m_TradeRoutes != null; } }
        public TradeRouteState TradeRoutes { get { return m_TradeRoutes ?? (m_TradeRoutes = new TradeRouteState()); } }
    }
}
