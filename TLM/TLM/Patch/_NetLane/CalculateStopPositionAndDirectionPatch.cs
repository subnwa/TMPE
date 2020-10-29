namespace TrafficManager.Patch._NetLane {
    using HarmonyLib;
    using JetBrains.Annotations;
    using UnityEngine;

    [HarmonyPatch(typeof(NetLane), nameof(NetLane.CalculateStopPositionAndDirection))]
    [UsedImplicitly]
    public class CalculateStopPositionAndDirectionPatch {
        [UsedImplicitly]
        public static bool Prefix(NetLane __instance,
                                  float laneOffset,
                                  float stopOffset,
                                  out Vector3 position,
                                  out Vector3 direction) {
            position = __instance.m_bezier.Position(laneOffset);
            direction = __instance.m_bezier.Tangent(laneOffset);
            Vector3 normalized = Vector3.Cross(Vector3.up, direction).normalized;
            position += normalized *
                        (MathUtils.SmootherStep( 0.9f, 0f, Mathf.Abs(laneOffset - 0.5f)) * stopOffset);
            return false;
        }
    }
}