namespace TrafficManager {
    using ColossalFramework.UI;
    using ColossalFramework;
    using CSUtil.Commons.Benchmark;
    using CSUtil.Commons;
    using ICities;
    using JetBrains.Annotations;
    using static LoadingExtension;
    using System.Collections.Generic;
    using System.Reflection;
    using TrafficManager.API.Manager;
    using TrafficManager.State;
    using TrafficManager.UI;
    using UnityEngine;
    using TrafficManager.UI.Helpers;

    [UsedImplicitly]
    public sealed class ThreadingExtension : ThreadingExtensionBase {
        // int ticksSinceLastMinuteUpdate = 0;
        ITrafficLightSimulationManager tlsMan =
            Constants.ManagerFactory.TrafficLightSimulationManager;

        IGeometryManager geoMan = Constants.ManagerFactory.GeometryManager;
        IRoutingManager routeMan = Constants.ManagerFactory.RoutingManager;
        IUtilityManager utilMan = Constants.ManagerFactory.UtilityManager;

        bool firstFrame = true;

        public override void OnCreated(IThreading threading) {
            base.OnCreated(threading);

            //ticksSinceLastMinuteUpdate = 0;
        }

        public override void OnBeforeSimulationTick() {
            base.OnBeforeSimulationTick();

            geoMan.SimulationStep();
            routeMan.SimulationStep();
        }

        public override void OnBeforeSimulationFrame() {
            base.OnBeforeSimulationFrame();

            if (firstFrame) {
                firstFrame = false;
                Log.Info("ThreadingExtension.OnBeforeSimulationFrame: First frame detected. Checking detours.");
            }

            if (Options.timedLightsEnabled) {
                tlsMan.SimulationStep();
            }
        }

        // public override void OnAfterSimulationFrame() {
        //        base.OnAfterSimulationFrame();
        //
        //        routeMan.SimulationStep();
        //
        //        ++ticksSinceLastMinuteUpdate;
        //        if (ticksSinceLastMinuteUpdate > 60 * 60) {
        //            ticksSinceLastMinuteUpdate = 0;
        //            GlobalConfig.Instance.SimulationStep();
        // #if DEBUG
        //            DebugMenuPanel.PrintTransportStats();
        // #endif
        //        }
        // }

        public override void OnUpdate(float realTimeDelta, float simulationTimeDelta) {
            base.OnUpdate(realTimeDelta, simulationTimeDelta);

            using (var bm = Benchmark.MaybeCreateBenchmark()) {
                if (ToolsModifierControl.toolController == null || ModUI.Instance == null) {
                    return;
                }

                TrafficManagerTool tmTool = ModUI.GetTrafficManagerTool(false);
                if (tmTool != null && ToolsModifierControl.toolController.CurrentTool != tmTool &&
                    ModUI.Instance.IsVisible()) {
                    ModUI.Instance.Close();
                }

                if (Input.GetKeyDown(KeyCode.Escape)) {
                    ModUI.Instance.Close();
                }
            } // end benchmark
        }
    } // end class
}