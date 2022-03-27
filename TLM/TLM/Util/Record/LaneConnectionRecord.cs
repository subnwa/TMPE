namespace TrafficManager.Util.Record {
    using CSUtil.Commons;
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using TrafficManager.API.Traffic.Enums;
    using TrafficManager.Manager.Impl;
    using TrafficManager.State;
    using TrafficManager.Util.Extensions;
    using static TrafficManager.Util.Shortcuts;

    [Serializable]
    public class LaneConnectionRecord : IRecordable {
        public uint LaneId;
        public byte LaneIndex;
        public bool StartNode;

        private uint[] connections_;
        LaneEndTransitionGroup[] groups_;

        private static LaneConnectionManager connMan => LaneConnectionManager.Instance;

        public void Record() {
            var connections = connMan.GetLaneConnections(LaneId, StartNode);
            connections_ = connections.Select(item => item.Key).ToArray();
            groups_ = connections.Select(item => item.Value).ToArray();
        }

        public void Restore() {
            connMan.RemoveLaneConnections(LaneId, StartNode);
            if(connections_ != null) {
                for (int i = 0; i < connections_.Length; ++i) {
                    LaneEndTransitionGroup group = LaneEndTransitionGroup.All;
                    if (groups_ != null && i < groups_.Length) {
                        group = groups_[i];
                    }
                    connMan.AddLaneConnection(LaneId, connections_[i], StartNode, group);
                }
            }
        }

        public void Transfer(Dictionary<InstanceID, InstanceID> map) {
            uint MappedLaneId(uint originalLaneID) {
                var originalLaneInstanceID = new InstanceID { NetLane = originalLaneID };
                if (map.TryGetValue(originalLaneInstanceID, out var ret))
                    return ret.NetLane;
                Log._Debug($"Could not map lane:{originalLaneID}. this is expected if move it has not copied all segment[s] from an intersection");
                return 0;
            }

            connMan.RemoveLaneConnections(LaneId, StartNode);

            var mappedLaneId = MappedLaneId(LaneId);
            if (mappedLaneId == 0)
                return;

            if (connections_ != null) {
                for (int i = 0; i < connections_.Length; ++i) {
                    LaneEndTransitionGroup group = LaneEndTransitionGroup.All;
                    if (groups_ != null && i < groups_.Length) {
                        group = groups_[i];
                    }
                    uint mappedTargetLaneId = MappedLaneId(connections_[i]);
                    connMan.AddLaneConnection(LaneId, mappedTargetLaneId, StartNode, group);
                }
            }
        }

        public static List<LaneConnectionRecord> GetLanes(ushort nodeId) {
            var ret = new List<LaneConnectionRecord>();
            ref NetNode node = ref nodeId.ToNode();
            ExtSegmentManager extSegmentManager = ExtSegmentManager.Instance;
            for (int i = 0; i < 8; ++i) {
                ushort segmentId = node.GetSegment(i);
                if (segmentId == 0) {
                    continue;
                }
                ref NetSegment netSegment = ref segmentId.ToSegment();
                NetInfo netInfo = netSegment.Info;
                if (netInfo == null) {
                    continue;
                }

                foreach (LaneIdAndIndex laneIdAndIndex in extSegmentManager.GetSegmentLaneIdsAndLaneIndexes(segmentId)) {
                    NetInfo.Lane laneInfo = netInfo.m_lanes[laneIdAndIndex.laneIndex];
                    bool match = (laneInfo.m_laneType & LaneConnectionManager.LANE_TYPES) != 0 &&
                                 (laneInfo.m_vehicleType & LaneConnectionManager.VEHICLE_TYPES) != 0;
                    if (!match) {
                        continue;
                    }

                    var laneData = new LaneConnectionRecord {
                        LaneId = laneIdAndIndex.laneId,
                        LaneIndex = (byte)laneIdAndIndex.laneIndex,
                        StartNode = (bool)extSegmentManager.IsStartNode(segmentId, nodeId),
                    };
                    ret.Add(laneData);
                }
            }
            return ret;
        }

        public byte[] Serialize() => SerializationUtil.Serialize(this);

    }
}
