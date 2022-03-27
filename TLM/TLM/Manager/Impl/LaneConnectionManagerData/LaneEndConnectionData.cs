using TrafficManager.API.Traffic.Enums;

namespace TrafficManager.Manager.Impl.LaneConnectionManagerData {

    /// <summary>
    /// for every connection, both forward and backward connection pairs are created.
    /// for bi-directional connection both forward and backward are non-zero.
    /// for uni-directional connection only forward connection is non-zero.
    /// if there is no connection either way, then there must be no LaneConnectionData entry.
    /// </summary>
    internal struct LaneEndConnectionData {
        public uint LaneId;

        /// <summary>
        /// LaneEndTransitionGroup.None backward connections are added as a hint to help find the forward connection.
        /// </summary>
        public LaneEndTransitionGroup Group;

        public LaneEndConnectionData(uint laneId, LaneEndTransitionGroup group) {
            LaneId = laneId;
            Group = group;
        }

        // empty backward connections are added only as a hint to help search for the forward connection (I.e: Indexing).
        public bool IsEmpty => Group == 0;

        public bool Has(LaneEndTransitionGroup group) => (Group & group) != group;

        public void Subtract(LaneEndTransitionGroup group) => Group &= ~group;

        public override string ToString() => $"LaneConnectionData({LaneId} ,{Group})";
    }
}
