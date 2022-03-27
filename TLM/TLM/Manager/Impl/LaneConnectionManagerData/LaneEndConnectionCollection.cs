namespace TrafficManager.Manager.Impl.LaneConnectionManagerData {
    using CSUtil.Commons;
    using System.Collections.Generic;
    using TrafficManager.API.Traffic.Enums;
    using TrafficManager.Util;
    using TrafficManager.Util.Extensions;

    internal class LaneEndConnectionCollection {
        public LaneEndConnectionData []Connections;
        public LaneEndTransitionGroup HasIncomingCached;
        public LaneEndTransitionGroup HasOutoingCached;
        public LaneEndTransitionGroup HasConnectionCached =>
            HasOutoingCached | HasIncomingCached;
        public int Length => Connections?.Length ?? 0;

        public LaneEndConnectionCollection(LaneEndConnectionData connection) {
            Connections = new[] { connection };
        }

        public void Append(LaneEndConnectionData newConnection) {
            if (Connections == null) {
                Connections = new[] { newConnection };
            } else {
                for (int i = 0; i < Connections.Length; ++i) {
                    if (Connections[i].LaneId == newConnection.LaneId) {
                        Connections[i].Group |= newConnection.Group;
                        return;
                    }
                }
                Connections.Append(newConnection);
            }
        }

        public void RemoveConnection(uint targetLaneId) {
            if (Connections != null) {
                var newConnections = new List<LaneEndConnectionData>(Connections.Length);
                for (int i = 0; i < Connections.Length; ++i) {
                    if (Connections[i].LaneId != targetLaneId) {
                        newConnections.Add(Connections[i]);
                    } 
                }

                if (newConnections.Count == 0)
                    Connections = null;
                else
                    Connections = newConnections.ToArray();
            }
        }

        public void PrintDebugInfo() {
            string strConnections = Connections == null ? "<null>" : "LaneEndConnectionData[5]";
            Log.Info($"\tLaneEndConnectionCollection: {strConnections} HasOutoingCached={HasOutoingCached} HasIncomingCached={HasIncomingCached}");
            for (int i = 0; i < Length; ++i) {
                var target = Connections[i];
                ref NetLane netLaneOfConnection = ref target.LaneId.ToLane();
                Log.Info($"\t\tEntry {i}: {target} (valid? {netLaneOfConnection.IsValidWithSegment()})");
            }
        }
    }
}
