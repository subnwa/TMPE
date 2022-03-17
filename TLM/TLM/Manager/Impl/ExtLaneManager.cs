using CSUtil.Commons;
using TrafficManager.API.Manager;
using TrafficManager.Util.Extensions;

namespace TrafficManager.Manager.Impl {
    internal class ExtLaneManager : AbstractCustomManager, IExtLaneManager {

        private ExtLane[] lanes_;

        static ExtLaneManager() {
            Instance = new ExtLaneManager();
        }

        private ExtLaneManager() {
            lanes_ = new ExtLane[NetManager.MAX_LANE_COUNT];

            for (int i = 0; i < lanes_.Length; i++) {
                lanes_[i].Reset();
            }

            Patch._NetLane.ReleaseLaneImplementationPatch.OnLaneReleased -= this.Reset;
            Patch._NetLane.ReleaseLaneImplementationPatch.OnLaneReleased += this.Reset;
        }

        public static ExtLaneManager Instance { get; }

        internal void Reset(uint laneId) => lanes_[laneId].Reset(); // TODO: call when lane is deleted.
        public int GetLaneIndex(uint laneId) => lanes_[laneId].LaneIndex;

        public NetInfo.Lane GetLaneInfo(uint laneId) {
            ushort segmentId = laneId.ToLane().m_segment;
            ref NetSegment netSegment = ref segmentId.ToSegment();
            int index = lanes_[laneId].LaneIndex;
            var laneInfos = netSegment.Info?.m_lanes;
            if (laneInfos != null && 0 < index && index < laneInfos.Length){
                    return laneInfos[index];
            }
            return null;
        }

        public override void OnLevelUnloading() {
            base.OnLevelUnloading();
            Patch._NetLane.ReleaseLaneImplementationPatch.OnLaneReleased -= this.Reset;
            lanes_ = null;
        }

        protected override void InternalPrintDebugInfo() {
            base.InternalPrintDebugInfo();
            Log._Debug($"Extended lane data:");

            for (uint laneId = 1; laneId < lanes_.Length; ++laneId) {
                ref ExtLane lane = ref lanes_[laneId];
                if (laneId.ToLane().IsValidWithSegment())
                    Log._Debug(lane.ToString());
            }
        }

        private struct ExtLane {
            /// <summary>
            /// This lane's index in the segment represented by <see cref="segmentId"/>.
            /// </summary>
            internal int LaneIndex { get; private set; }

            internal ExtLane(int laneIndex) {
                LaneIndex = laneIndex;
            }

            internal void Reset() {
                LaneIndex = -1;
            }
        }
    }
}
