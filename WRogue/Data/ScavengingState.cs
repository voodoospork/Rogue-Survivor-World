using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace djack.RogueSurvivor.Data
{
    // Saved knowledge contains identifiers and geometry, never live actors or maps.
    [Serializable] sealed class SupplyObservation
    {
        public long EventId;
        public int ObservedTurn, LearnedTurn, Confidence, Units;
        public bool Confirmed, Claimed;
        public string Source, ClaimName;
        public SupplyObservation Copy() { return (SupplyObservation)MemberwiseClone(); }
    }
    [Serializable] sealed class SupplyLead
    {
        public Guid Id = Guid.NewGuid();
        public string Resource, Label;
        public bool SiteOnly;
        public CommunityShelterLocation Place;
        public readonly List<SupplyObservation> Observations = new List<SupplyObservation>();
        public SupplyObservation Latest { get { return Observations.Count == 0 ? null : Observations[Observations.Count - 1]; } }
        public SupplyLead Copy()
        {
            var copy = new SupplyLead { Id = Id, Resource = Resource, Label = Label, SiteOnly = SiteOnly, Place = Place.Copy() };
            foreach (var observation in Observations) copy.Observations.Add(observation.Copy());
            return copy;
        }
    }
    [Serializable] sealed class ScavengingTrip
    {
        public Guid Id = Guid.NewGuid();
        public string Resource, Beneficiary, Outcome;
        public long CommitmentId;
        public int DepartedTurn, ArrivedTurn = -1, CompletedTurn = -1, Deadline;
        public int DepartureFood, DepartureMedicine, FinalFood, FinalMedicine;
        public SupplyLead Destination;
        public CommunityShelterLocation Return;
        public bool Returned, Cancelled;
    }
    [Serializable] sealed class ScavengingState
    {
        public readonly List<SupplyLead> Leads = new List<SupplyLead>();
        public readonly List<ScavengingTrip> Completed = new List<ScavengingTrip>();
        public ScavengingTrip Active;
        public void Record(string resource, string label, bool siteOnly, CommunityShelterLocation place, SupplyObservation observation)
        {
            var lead = Leads.Find(l => l.Resource == resource && l.SiteOnly == siteOnly && SameMap(l.Place, place) &&
                (siteOnly ? l.Place.BuildingBounds == place.BuildingBounds : l.Place.Anchor == place.Anchor));
            if (lead != null && lead.Latest != null)
            {
                var latest = lead.Latest;
                bool changedDirect = observation.Confidence == 100 && latest.Confidence == 100 && observation.Source == "seen by you" &&
                    (latest.Units != observation.Units || latest.Claimed != observation.Claimed || latest.ClaimName != observation.ClaimName);
                if (latest.ObservedTurn > observation.ObservedTurn || (latest.ObservedTurn == observation.ObservedTurn &&
                    (latest.Confidence > observation.Confidence || latest.Confidence == observation.Confidence &&
                    !changedDirect && observation.EventId <= latest.EventId))) return;
            }
            if (lead == null) { lead = new SupplyLead { Resource = resource, Label = label, SiteOnly = siteOnly, Place = place.Copy() }; Leads.Add(lead); }
            else { Leads.Remove(lead); Leads.Add(lead); }
            // Refresh an unchanged witnessed stock sighting without filling the history with identical rows.
            var last = lead.Latest;
            if (last != null && last.Source == observation.Source && last.Units == observation.Units &&
                last.Confirmed == observation.Confirmed && last.Claimed == observation.Claimed && last.Confidence == 100 && observation.Confidence == 100)
                lead.Observations.Remove(last);
            lead.Observations.Add(observation.Copy());
            if (lead.Observations.Count > 4) lead.Observations.RemoveAt(0);
            if (Leads.Count > 64) Leads.RemoveAt(0);
        }
        public static bool SameMap(CommunityShelterLocation a, CommunityShelterLocation b)
        { return a != null && b != null && a.MapSeed == b.MapSeed && a.MapName == b.MapName && a.District == b.District; }
        public void Complete(ScavengingTrip trip)
        { Completed.Add(trip); while (Completed.Count > 16) Completed.RemoveAt(0); Active = null; }
    }
    sealed partial class PersonalityState
    {
        [OptionalField] ScavengingState m_Scavenging;
        public ScavengingState Scavenging { get { return m_Scavenging ?? (m_Scavenging = new ScavengingState()); } }
        public bool HasScavenging { get { return m_Scavenging != null; } }
    }
}
