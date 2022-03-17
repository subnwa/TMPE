namespace TrafficManager.Patch._NetLane {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using HarmonyLib;
    using TrafficManager.Manager.Impl;

    [HarmonyPatch(typeof(NetLane), "ReleaseLaneImplementation")]
    internal static class ReleaseLaneImplementationPatch {
        public static event Action<uint> OnLaneReleased;
        static void Prefix(uint lane) => ExtLaneManager.Instance.Reset(lane);
    }
}
