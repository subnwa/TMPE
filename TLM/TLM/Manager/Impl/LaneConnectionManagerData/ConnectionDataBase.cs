namespace TrafficManager.Manager.Impl.LaneConnectionManagerData {
    using CSUtil.Commons;
    using System.Collections.Generic;
    using System.Diagnostics;
    using TrafficManager.API.Traffic.Enums;
    using TrafficManager.Util;
    using TrafficManager.Util.Extensions;

    internal class ConnectionDataBase : Dictionary<LaneEnd, LaneEndConnectionCollection> {
        public ConnectionDataBase() : base(LaneEnd.LaneIdStartNodeComparer) { }

        internal bool IsConnectedTo(uint sourceLaneId, uint targetLaneId, ushort nodeId, LaneEndTransitionGroup group) =>
            IsConnectedTo(sourceLaneId, targetLaneId, sourceLaneId.ToLane().IsStartNode(nodeId), group);

        /// <param name="sourceStartNode">start node for the segment of the source lane</param>
        internal bool IsConnectedTo(uint sourceLaneId, uint targetLaneId, bool sourceStartNode, LaneEndTransitionGroup group) {
            LaneEnd key = new(sourceLaneId, sourceStartNode);
            if (this.TryGetValue(key, out var collection)) {
                var targets = collection.Connections;
                int n = targets?.Length ?? 0; 
                for (int i = 0; i < n; ++i) {
                    if (targets[i].LaneId == targetLaneId && targets[i].Has(group)) {
                        return true;
                    }
                }
            }
            return false;
        }

        private bool IsConnectionEmpty(uint sourceLaneId, uint targetLaneId, ushort nodeId) =>
            IsConnectionEmpty(sourceLaneId, targetLaneId, sourceLaneId.ToLane().IsStartNode(nodeId));

        private bool IsConnectionEmpty(uint sourceLaneId, uint targetLaneId, bool sourceStartNode) {
            LaneEnd key = new(sourceLaneId, sourceStartNode);
            if (this.TryGetValue(key, out var collection)) {
                var targets = collection.Connections;
                int n = targets?.Length ?? 0;
                for (int i = 0; i < n; ++i) {
                    if (targets[i].LaneId == targetLaneId) {
                        return targets[i].IsEmpty;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Adds a connection from source to target lane at the give node
        /// </summary>
        internal void ConnectTo(uint sourceLaneId, uint targetLaneId, ushort nodeId, LaneEndTransitionGroup group) {
            var forwad = AddConnection(sourceLaneId, targetLaneId, nodeId, group); //forward
            var backward =AddConnection(targetLaneId, sourceLaneId, nodeId, 0); //backward
            forwad.HasOutoingCached |= group;
            backward.HasIncomingCached |= group;
        }

        /// <summary>
        /// makes a uni-directional connection
        /// </summary>
        /// <c>false</c> for backward connection</param>
        private LaneEndConnectionCollection AddConnection(uint sourceLaneId, uint targetLaneId, ushort nodeId, LaneEndTransitionGroup group) {
            LaneEnd key = new(sourceLaneId, nodeId);
            LaneEndConnectionData newConnection = new(targetLaneId, group);
            if(this.TryGetValue(key, out var collection)) {
                collection.Append(newConnection);
                return collection;
            } else {
                return this[key] = new(newConnection);
            }
        }

        /// <summary>removes the connection from source to target lane at the given node</summary>
        internal bool Disconnect(uint sourceLaneId, uint targetLaneId, ushort nodeId, LaneEndTransitionGroup group) {
            bool connectionExisted = DisableConnection(sourceLaneId, targetLaneId, nodeId, group);
            if (connectionExisted) {
                // remove redundant connections:
                bool forwardIsEmpty = IsConnectionEmpty(sourceLaneId, targetLaneId, nodeId);
                bool backwardIsEmpty = IsConnectionEmpty(targetLaneId, sourceLaneId, nodeId);
                if (forwardIsEmpty && backwardIsEmpty) {

                    RemoveConnection(targetLaneId, sourceLaneId, nodeId);
                    RemoveConnection(sourceLaneId, targetLaneId, nodeId);
                }

                RefreshCache(sourceLaneId, nodeId);
                RefreshCache(targetLaneId, nodeId);
            }
            return connectionExisted;
        }

        /// <summary>
        /// disables a single connection
        /// </summary>
        /// <returns><c>true</c> if any connection was disabled. <c>false</c> otherwise. </returns>
        private bool DisableConnection(uint sourceLaneId, uint targetLaneId, ushort nodeId, LaneEndTransitionGroup group) {
            LaneEnd key = new(sourceLaneId, nodeId);
            if (this.TryGetValue(key, out var collection)) {
                var targets = collection.Connections;
                int n = targets?.Length ?? 0;
                for (int i = 0; i < n; ++i) {
                    if (targets[i].LaneId == targetLaneId && targets[i].Has(group)) {
                        bool connectionExisted = targets[i].Has(group);
                        targets[i].Subtract(group);
                        return connectionExisted;
                    }
                }
            }

            return false;
        }

        private void RemoveConnection(uint sourceLaneId, uint targetLaneId, ushort nodeId) {
            bool sourceStartNode = sourceLaneId.ToLane().IsStartNode(nodeId);
            RemoveConnection(sourceLaneId, targetLaneId, sourceStartNode);
        }

        /// <summary>
        /// removes a single connection
        /// </summary>
        /// <param name="sourceStartNode">start node for the segment of the source lane</param>
        private void RemoveConnection(uint sourceLaneId, uint targetLaneId, bool sourceStartNode) {
            bool ret = false;
            LaneEnd key = new(sourceLaneId, sourceStartNode);
            if (this.TryGetValue(key, out var collection)) {
                collection.RemoveConnection(targetLaneId);
                if(collection.Length == 0) {
                    this.Remove(key);
                }
            }
        }
        
        /// <summary>
        /// removes all connections from and to the given lane.
        /// </summary>
        internal void RemoveConnections(uint laneId) {
            RemoveConnections(laneId, true);
            RemoveConnections(laneId, false);
        }

        internal void RemoveConnections(uint laneId, bool startNode) {
            LaneEnd key = new LaneEnd(laneId, startNode);
            if (this.TryGetValue(key, out var collection)) {
                ushort nodeId = laneId.ToLane().GetNodeId(startNode);
                for (int i = 0; i < collection.Length; ++i) {
                    ref var connection = ref collection.Connections[i];
                    uint laneId2 = connection.LaneId;
                    RemoveConnection(laneId2, laneId, nodeId);

                    // refresh cash for backward lane:
                    connection.Group = 0;
                    RefreshCache(laneId2, nodeId);

                }

                this.Remove(key);
            }
        }

        internal void RefreshCache(uint laneId, ushort nodeId) {
            LaneEnd key = new(laneId, nodeId);

            //outgoing connections
            if (this.TryGetValue(key, out var collection)) {
                LaneEndTransitionGroup outgoing = 0;
                LaneEndTransitionGroup incoming = 0;
                for (int i = 0; i < collection.Length; ++i) {
                    ref var connection = ref collection.Connections[i];
                    outgoing |= connection.Group;

                    // search for backward connection
                    LaneEnd backwardKey = new(connection.LaneId, nodeId);
                    if (this.TryGetValue(backwardKey, out var collection2)) {
                        for (int j = 0; j < collection2.Length; ++j) {
                            ref var backwardConnection = ref collection2.Connections[i];
                            if (backwardConnection.LaneId == laneId) {
                                incoming |= backwardConnection.Group;
                            }
                        }
                    }
                }
                collection.HasOutoingCached = outgoing;
                collection.HasIncomingCached = incoming;
            }
        }

        [Conditional("DEBUG")]
        internal void PrintDebugInfo() {
            for(uint sourceLaneId = 0; sourceLaneId < NetManager.instance.m_laneCount; ++sourceLaneId) {
                ref NetLane netLane = ref sourceLaneId.ToLane();

                ushort segmentId = netLane.m_segment;
                ref NetSegment netSegment = ref segmentId.ToSegment();

                var laneStart = new LaneEnd(sourceLaneId, true);
                var laneEnd = new LaneEnd(sourceLaneId, false);
                if (this.ContainsKey(laneStart) || this.ContainsKey(laneEnd)) {

                    Log.Info($"Lane {sourceLaneId}: valid? {netLane.IsValidWithSegment()}, seg. valid? {netSegment.IsValid()}");

                    foreach (bool startNode in new bool[] { false, true }) {
                        LaneEnd key = new(sourceLaneId, startNode);
                        if (this.TryGetValue(key, out var targets)) {
                            ushort nodeId = netSegment.GetNodeId(startNode);
                            ref NetNode netNode = ref nodeId.ToNode();
                            Log.Info($"\tstartNode:{startNode} ({nodeId}, seg. {segmentId}): valid? {netNode.IsValid()}");
                            for (int i = 0; i < targets.Length; ++i) {
                                var target = targets.Connections[i];
                                ref NetLane netLaneOfConnection = ref target.LaneId.ToLane();
                                Log.Info($"\t\tEntry {i}: {target} (valid? {netLaneOfConnection.IsValidWithSegment()})");
                            }
                        }
                    }
                }
            }
        }
    }
}
