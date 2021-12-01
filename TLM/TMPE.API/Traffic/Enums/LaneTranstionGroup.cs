namespace TrafficManager.API.Traffic.Enums {
    using System;

    [Flags]
    public enum LaneTranstionGroup {
        None = 0,
        Track = 1, // e.g: tram, trolley, metro, train,
        Normal = 2, // everything else
        Both = Track | Normal,
    }
}
