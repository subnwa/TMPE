﻿namespace TrafficManager.UI.SubTools.SpeedLimits.Overlay {
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ColossalFramework;
    using JetBrains.Annotations;
    using TrafficManager.API.Traffic.Data;
    using TrafficManager.Manager.Impl;
    using TrafficManager.State;
    using TrafficManager.Traffic;
    using TrafficManager.UI.Helpers;
    using TrafficManager.UI.Textures;
    using TrafficManager.Util;
    using TrafficManager.Util.Caching;
    using TrafficManager.Util.Extensions;
    using UnityEngine;

    /// <summary>
    /// Stores rendering state for Speed Limits overlay and provides rendering of speed limit signs
    /// overlay for segments/lanes.
    /// </summary>
    public class SpeedLimitsOverlay {
        private const float SMALL_ICON_SCALE = 0.66f;

        private TrafficManagerTool mainTool_;

        private ushort segmentId_;
        private NetInfo.Direction finalDirection_ = NetInfo.Direction.None;

        /// <summary>Used to pass options to the overlay rendering.</summary>
        public class DrawArgs {
            /// <summary>If not null, contains mouse position. Null means mouse is over some GUI window.</summary>+
            public Vector2? Mouse;

            /// <summary>List of UI frame rectangles which will make the signs fade if rendered over.</summary>
            public List<Rect> UiWindowRects;

            /// <summary>Set to true to allow bigger and clickable road signs.</summary>
            public bool IsInteractive;

            /// <summary>
            /// User is holding Shift to edit multiple segments.
            /// Set to true when operating entire road between two junctions.
            /// </summary>
            public bool MultiSegmentMode;

            /// <summary>Choose what to display (hold Alt to display something else).</summary>
            public SpeedlimitsToolMode ToolMode;

            /// <summary>
            /// Set this to true to additionally show the other PerLane/PerSegment mode as small
            /// icons together with the large icons.
            /// </summary>
            public bool ShowAltMode;

            /// <summary>Hovered SEGMENT speed limit handles (output after rendering).</summary>
            public List<OverlaySegmentSpeedlimitHandle> HoveredSegmentHandles;

            /// <summary>Hovered LANE speed limit handles (output after rendering).</summary>
            public List<OverlayLaneSpeedlimitHandle> HoveredLaneHandles;

            public static DrawArgs Create() {
                return new() {
                    UiWindowRects = new List<Rect>(),
                    IsInteractive = false,
                    MultiSegmentMode = false,
                    ToolMode = SpeedlimitsToolMode.Segments,
                    HoveredSegmentHandles = new List<OverlaySegmentSpeedlimitHandle>(capacity: 10),
                    HoveredLaneHandles = new List<OverlayLaneSpeedlimitHandle>(capacity: 10),
                    ShowAltMode = false,
                };
            }

            public void ClearHovered() {
                this.HoveredSegmentHandles.Clear();
                this.HoveredLaneHandles.Clear();
            }

            public bool IntersectsAnyUIRect(Rect testRect) {
                return this.UiWindowRects.Any(testRect.Overlaps);
            }
        }

        /// <summary>Environment for rendering multiple signs, to avoid creating same data over and over
        /// and to carry drawing state between multiple calls without using class fields.</summary>
        private class DrawEnv {
            public Vector2 signsThemeAspectRatio_;
            public IDictionary<int,Texture2D> largeSignsTextures_;
            public IDictionary<int,Texture2D> currentThemeTextures_;

            /// <summary>
            /// This is set to true if the user will see blue default signs, or the user is holding
            /// Alt to see blue signs temporarily. Holding Alt while default signs are shown, will
            /// show segment speeds instead.
            /// </summary>
            public bool drawDefaults_;
        }

        private struct CachedSegment {
            public ushort id_;
            public Vector3 center_;
        }

        /// <summary>
        /// Stores potentially visible segment ids while the camera did not move.
        /// </summary>
        [NotNull]
        private readonly GenericArrayCache<CachedSegment> cachedVisibleSegmentIds_;

        /// <summary>If set to true, prompts one-time cache reset.</summary>
        private bool resetCacheFlag_ = false;

        /// <summary>Stores last cached camera position in <see cref="cachedVisibleSegmentIds_"/>.</summary>
        private CameraTransformValue lastCachedCamera_;

        private const float SPEED_LIMIT_SIGN_SIZE = 70f;

        /// <summary>Cached segment centers.</summary>
        private readonly Dictionary<ushort, Vector3> segmentCenters_ = new();

        public SpeedLimitsOverlay(TrafficManagerTool mainTool) {
            this.mainTool_ = mainTool;
            this.cachedVisibleSegmentIds_ = new GenericArrayCache<CachedSegment>(NetManager.MAX_SEGMENT_COUNT);
            this.lastCachedCamera_ = new CameraTransformValue();
        }

        /// <summary>Displays non-sign overlays, like lane highlights.</summary>
        /// <param name="cameraInfo">The camera.</param>
        /// <param name="args">The state of the parent <see cref="SpeedLimitsTool"/>.</param>
        public void RenderHelperGraphics(RenderManager.CameraInfo cameraInfo,
                                         [NotNull] DrawArgs args) {
            if (args.ToolMode != SpeedlimitsToolMode.Lanes) {
                this.RenderSegments(cameraInfo, args);
            }
        }

        /// <summary>Render segment overlay (this is curves, not the signs).</summary>
        /// <param name="cameraInfo">The camera.</param>
        /// <param name="args">The state of the parent <see cref="SpeedLimitsTool"/>.</param>
        private void RenderSegments(RenderManager.CameraInfo cameraInfo,
                                    [NotNull] DrawArgs args) {
            if (!args.MultiSegmentMode) {
                //------------------------
                // Single segment highlight. User is NOT holding Shift.
                //------------------------
                this.RenderSegmentSideOverlay(
                    cameraInfo: cameraInfo,
                    segmentId: this.segmentId_,
                    args: args,
                    finalDirection: this.finalDirection_);
            } else {
                //------------------------
                // Entire street highlight. User is holding Shift.
                //------------------------
                if (RoundaboutMassEdit.Instance.TraverseLoop(
                    segmentId: this.segmentId_,
                    segList: out var segmentList)) {
                    foreach (ushort segmentId in segmentList) {
                        this.RenderSegmentSideOverlay(
                            cameraInfo: cameraInfo,
                            segmentId: segmentId,
                            args: args);
                    }
                } else {
                    SegmentTraverser.Traverse(
                        initialSegmentId: this.segmentId_,
                        direction: SegmentTraverser.TraverseDirection.AnyDirection,
                        side: SegmentTraverser.TraverseSide.AnySide,
                        stopCrit: SegmentTraverser.SegmentStopCriterion.Junction,
                        visitorFun: data => {
                            NetInfo.Direction finalDirection = this.finalDirection_;
                            if (data.IsReversed(this.segmentId_)) {
                                finalDirection = NetInfo.InvertDirection(finalDirection);
                            }

                            this.RenderSegmentSideOverlay(
                                cameraInfo: cameraInfo,
                                segmentId: data.CurSeg.segmentId,
                                args: args,
                                finalDirection: finalDirection);
                            return true;
                        });
                }
            }
        }

        /// <summary>
        /// Renders all lane curves with the given <paramref name="finalDirection"/>
        /// if NetInfo.Direction.None, all lanes are rendered.
        /// </summary>
        private void RenderSegmentSideOverlay(RenderManager.CameraInfo cameraInfo,
                                              ushort segmentId,
                                              DrawArgs args,
                                              NetInfo.Direction finalDirection = NetInfo.Direction.None)
        {
            ref NetSegment netSegment = ref segmentId.ToSegment();
            ExtSegmentManager extSegmentManager = ExtSegmentManager.Instance;

            foreach (LaneIdAndIndex laneIdAndIndex in extSegmentManager.GetSegmentLaneIdsAndLaneIndexes(segmentId)) {
                NetInfo.Lane laneInfo = netSegment.Info.m_lanes[laneIdAndIndex.laneIndex];

                bool render = (laneInfo.m_laneType & SpeedLimitManager.LANE_TYPES) != 0;
                render &= (laneInfo.m_vehicleType & SpeedLimitManager.VEHICLE_TYPES) != 0;
                render &= laneInfo.m_finalDirection == finalDirection || finalDirection == NetInfo.Direction.None;

                if (render) {
                    RenderLaneOverlay(cameraInfo, laneIdAndIndex.laneId, args);
                }
            }
        }

        /// <summary>Draw blue lane curves overlay.</summary>
        /// <param name="cameraInfo">The Camera.</param>
        /// <param name="laneId">The lane.</param>
        /// <param name="args">The state of the parent <see cref="SpeedLimitsTool"/>.</param>
        private void RenderLaneOverlay(RenderManager.CameraInfo cameraInfo,
                                       uint laneId,
                                       [NotNull] DrawArgs args) {
            NetLane[] laneBuffer = NetManager.instance.m_lanes.m_buffer;
            SegmentLaneMarker marker = new SegmentLaneMarker(laneBuffer[laneId].m_bezier);
            bool pressed = Input.GetMouseButton(0);
            Color color = this.mainTool_.GetToolColor(warning: pressed, error: false);

            if (args.ToolMode == SpeedlimitsToolMode.Lanes) {
                marker.Size = 3f; // lump the lanes together.
            }

            marker.RenderOverlay(cameraInfo, color, pressed);
        }

        /// <summary>Called by the parent tool on activation. Reset the cached segments cache and
        /// camera cache.</summary>
        public void ResetCache() {
            this.resetCacheFlag_ = true;
        }

        /// <summary>
        /// Draw speed limit signs (only in GUI mode).
        /// NOTE: This must be called from GUI mode, because of GUI.DrawTexture use.
        /// Render the speed limit signs based on the current settings.
        /// </summary>
        /// <param name="args">The state of the parent <see cref="SpeedLimitsTool"/>.</param>
        public void ShowSigns_GUI(DrawArgs args) {
            Camera camera = Camera.main;
            if (camera == null) {
                return;
            }

            NetManager netManager = Singleton<NetManager>.instance;
            SpeedLimitManager speedLimitManager = SpeedLimitManager.Instance;

            var currentCamera = new CameraTransformValue(camera);
            Transform currentCameraTransform = camera.transform;
            Vector3 camPos = currentCameraTransform.position;

            // TODO: Can road network change while speed limit tool is active? Disasters?
            if (this.resetCacheFlag_ || !this.lastCachedCamera_.Equals(currentCamera)) {
                this.lastCachedCamera_ = currentCamera;
                this.resetCacheFlag_ = false;

                this.ShowSigns_RefreshVisibleSegmentsCache(
                    netManager: netManager,
                    camPos: camPos,
                    speedLimitManager: speedLimitManager);
            }

            bool hover = false;
            IDictionary<int, Texture2D> currentThemeTextures = SpeedLimitTextures.GetTextureSource();
            DrawEnv drawEnv = new DrawEnv {
                signsThemeAspectRatio_ = SpeedLimitTextures.GetTextureAspectRatio(),
                currentThemeTextures_ = currentThemeTextures,
                largeSignsTextures_ = args.ToolMode switch {
                    SpeedlimitsToolMode.Segments => currentThemeTextures,
                    SpeedlimitsToolMode.Lanes => currentThemeTextures,
                    // Defaults can show normal textures if the user holds Alt
                    SpeedlimitsToolMode.Defaults => args.ShowAltMode
                                                        ? currentThemeTextures
                                                        : SpeedLimitTextures.RoadDefaults,
                    _ => throw new ArgumentOutOfRangeException(),
                },
                drawDefaults_ = (args.ToolMode == SpeedlimitsToolMode.Defaults) ^ args.ShowAltMode,
            };

            for (int segmentIdIndex = this.cachedVisibleSegmentIds_.Size - 1;
                 segmentIdIndex >= 0;
                 segmentIdIndex--) {
                ref CachedSegment cachedSeg = ref this.cachedVisibleSegmentIds_.Values[segmentIdIndex];

                // If VehicleRestrictions tool is active, skip drawing the current selected segment
                if (this.mainTool_.GetToolMode() == ToolMode.VehicleRestrictions
                    && cachedSeg.id_ == TrafficManagerTool.SelectedSegmentId) {
                    continue;
                }

                if (args.ToolMode == SpeedlimitsToolMode.Lanes && !drawEnv.drawDefaults_) {
                    // in defaults mode separate lanes don't make any sense, so show segments at all times
                    hover |= this.DrawSpeedLimitHandles_PerLane(
                        segmentId: cachedSeg.id_,
                        camPos: camPos,
                        drawEnv: drawEnv,
                        args: args);
                } else {
                    // Both segment speed limits and default speed limits are displayed in the same way
                    hover |= this.DrawSpeedLimitHandles_PerSegment(
                        segmentId: cachedSeg.id_,
                        segCenter: cachedSeg.center_,
                        camPos: camPos,
                        drawEnv: drawEnv,
                        args: args);
                }
            }

            if (!hover) {
                this.segmentId_ = 0;
            }
        }

        /// <summary>
        /// When camera position has changed and cached segments set is invalid, scan all segments
        /// again and remember those visible in the camera frustum.
        /// </summary>
        /// <param name="netManager">Access to map data.</param>
        /// <param name="camPos">Camera position to consider.</param>
        /// <param name="speedLimitManager">Query if a segment is eligible for speed limits.</param>
        private void ShowSigns_RefreshVisibleSegmentsCache(NetManager netManager,
                                                           Vector3 camPos,
                                                           SpeedLimitManager speedLimitManager) {
            // cache visible segments
            this.cachedVisibleSegmentIds_.Clear();
            this.segmentCenters_.Clear();

            for (uint segmentId = 1; segmentId < NetManager.MAX_SEGMENT_COUNT; ++segmentId) {
                ref var segment = ref ((ushort)segmentId).ToSegment();

                if (!segment.IsValid()) {
                    continue;
                }

                Vector3 distToCamera = segment.m_bounds.center - camPos;

                if (distToCamera.sqrMagnitude > TrafficManagerTool.MAX_OVERLAY_DISTANCE_SQR) {
                    continue; // do not draw if too distant
                }

                bool visible = GeometryUtil.WorldToScreenPoint(
                    worldPos: segment.m_bounds.center,
                    screenPos: out Vector3 _);

                if (!visible) {
                    continue;
                }

                if (!speedLimitManager.MayHaveCustomSpeedLimits(ref segment)) {
                    continue;
                }

                this.cachedVisibleSegmentIds_.Add(
                    new CachedSegment {
                        id_ = (ushort)segmentId,
                        center_ = segment.GetCenter(),
                    });
            } // end for all segments
        }

        /// <summary>
        /// Render speed limit handles one per segment, both directions averaged, and if the speed
        /// limits on the directions don't match, extra small speed limit icons are added.
        /// </summary>
        /// <param name="segmentId">Seg id.</param>
        /// <param name="segCenter">Bezier center for the segment to draw at.</param>
        /// <param name="camPos">Camera.</param>
        /// <param name="args">Render args.</param>
        private bool DrawSpeedLimitHandles_PerSegment(ushort segmentId,
                                                      Vector3 segCenter,
                                                      Vector3 camPos,
                                                      [NotNull] DrawEnv drawEnv,
                                                      [NotNull] DrawArgs args) {
            // Default signs are round, mph/kmph textures can be round or rectangular
            var colorController = new OverlayHandleColorController(args.IsInteractive);

            //--------------------------
            // For all segments visible
            //--------------------------
            bool visible = GeometryUtil.WorldToScreenPoint(worldPos: segCenter, screenPos: out Vector3 screenPos);

            bool ret = visible && DrawSpeedLimitHandles_SegmentCenter(
                    segmentId,
                    segCenter,
                    camPos,
                    screenPos,
                    colorController,
                    drawEnv,
                    args);

            colorController.RestoreGUIColor();
            return ret;
        }

        private bool DrawSpeedLimitHandles_SegmentCenter(
            ushort segmentId,
            Vector3 segCenter,
            Vector3 camPos,
            Vector3 screenPos,
            OverlayHandleColorController colorController,
            [NotNull] DrawEnv drawEnv,
            [NotNull] DrawArgs args)
        {
            Vector2 largeRatio = drawEnv.drawDefaults_
                                     ? SpeedLimitTextures.DefaultSpeedlimitsAspectRatio()
                                     : drawEnv.signsThemeAspectRatio_;

            // TODO: Replace formula in visibleScale and size to use Constants.OVERLAY_INTERACTIVE_SIGN_SIZE and OVERLAY_READONLY_SIGN_SIZE
            float visibleScale = 100.0f / (segCenter - camPos).magnitude;
            float size = (args.IsInteractive ? 1f : 0.8f) * SPEED_LIMIT_SIGN_SIZE * visibleScale;

            SignRenderer signRenderer = default;
            SignRenderer squareSignRenderer = default;

            // Recalculate visible rect for screen position and size
            Rect signScreenRect = signRenderer.Reset(screenPos, size: size * largeRatio);
            bool isHoveredHandle = args.IsInteractive && signRenderer.ContainsMouse(args.Mouse);

            //-----------
            // Rendering
            //-----------
            // Sqrt(visibleScale) makes fade start later as distance grows
            colorController.SetGUIColor(
                hovered: isHoveredHandle,
                intersectsGuiWindows: args.IntersectsAnyUIRect(signScreenRect),
                opacityMultiplier: Mathf.Sqrt(visibleScale));

            NetInfo neti = segmentId.ToSegment().Info;
            var defaultSpeedLimit = new SpeedValue(
                gameUnits: SpeedLimitManager.Instance.GetCustomNetInfoSpeedLimit(info: neti));

            // Render override if interactive, or if readonly info layer and override exists
            if (drawEnv.drawDefaults_) {
                //-------------------------------------
                // Draw default speed limit (blue sign)
                //-------------------------------------
                squareSignRenderer.Reset(
                    screenPos,
                    size: size * SpeedLimitTextures.DefaultSpeedlimitsAspectRatio());
                squareSignRenderer.DrawLargeTexture(
                    speedlimit: defaultSpeedLimit,
                    textureSource: SpeedLimitTextures.RoadDefaults);
            } else {
                //-------------------------------------
                // Draw override, if exists, otherwise draw circle and small blue default
                // Get speed limit override for segment
                //-------------------------------------
                SpeedValue? overrideSpeedlimitForward =
                    SpeedLimitManager.Instance.GetCustomSpeedLimit(segmentId, finalDir: NetInfo.Direction.Forward);
                SpeedValue? overrideSpeedlimitBack =
                    SpeedLimitManager.Instance.GetCustomSpeedLimit(segmentId, finalDir: NetInfo.Direction.Backward);
                SpeedValue? drawSpeedlimit = GetAverageSpeedlimit(
                    forward: overrideSpeedlimitForward,
                    back: overrideSpeedlimitBack);

                if (!drawSpeedlimit.HasValue || drawSpeedlimit.Value.Equals(defaultSpeedLimit)) {
                    // No value, no override
                    squareSignRenderer.Reset(
                        screenPos,
                        size: size * SpeedLimitTextures.DefaultSpeedlimitsAspectRatio());
                    squareSignRenderer.DrawLargeTexture(SpeedLimitTextures.NoOverride);
                    squareSignRenderer.DrawSmallTexture(
                        speedlimit: defaultSpeedLimit,
                        textureSource: SpeedLimitTextures.RoadDefaults);
                } else {
                    signRenderer.DrawLargeTexture(
                        speedlimit: drawSpeedlimit,
                        textureSource: drawEnv.largeSignsTextures_);
                }
            }

            if (!isHoveredHandle) {
                return false;
            }

            // Clickable overlay (interactive signs also True):
            // Register the position of a mouse-hovered speedlimit overlay icon
            args.HoveredSegmentHandles.Add(
                item: new OverlaySegmentSpeedlimitHandle(segmentId));

            this.segmentId_ = segmentId;
            this.finalDirection_ = NetInfo.Direction.Both;
            return true;
        }

        private SpeedValue? GetAverageSpeedlimit(SpeedValue? forward, SpeedValue? back) {
            if (forward.HasValue && back.HasValue) {
                return (forward.Value + back.Value).Scale(0.5f);
            }
            return forward ?? back;
        }

        /// <summary>Draw speed limit handles one per lane.</summary>
        /// <param name="segmentId">Seg id.</param>
        /// <param name="camPos">Camera.</param>
        /// <param name="drawEnv">Temporary values used for rendering this frame.</param>
        /// <param name="args">Render args.</param>
        private bool DrawSpeedLimitHandles_PerLane(
            ushort segmentId,
            Vector3 camPos,
            [NotNull] DrawEnv drawEnv,
            [NotNull] DrawArgs args)
        {
            bool ret = false;
            ref NetSegment segment = ref segmentId.ToSegment();
            Vector3 segmentCenterPos = segment.m_bounds.center;

            // show individual speed limit handle per lane
            int numLanes = GeometryUtil.GetSegmentNumVehicleLanes(
                segmentId: segmentId,
                nodeId: null,
                numDirections: out int numDirections,
                vehicleTypeFilter: SpeedLimitManager.VEHICLE_TYPES);

            NetInfo segmentInfo = segment.Info;
            Vector3 yu = (segment.m_endDirection - segment.m_startDirection).normalized;
            Vector3 xu = Vector3.Cross(yu, new Vector3(0, 1f, 0)).normalized;
            float signSize = args.IsInteractive
                                 ? Constants.OVERLAY_INTERACTIVE_SIGN_SIZE
                                 : Constants.OVERLAY_READONLY_SIGN_SIZE;

            Vector3 drawOriginPos = segmentCenterPos -
                                    (0.5f * (((numLanes - 1) + numDirections) - 1) * signSize * xu);
            ExtSegmentManager extSegmentManager = ExtSegmentManager.Instance;

            IList<LanePos> sortedLanes = extSegmentManager.GetSortedLanes(
                segmentId: segmentId,
                segment: ref segment,
                startNode: null,
                laneTypeFilter: SpeedLimitManager.LANE_TYPES,
                vehicleTypeFilter: SpeedLimitManager.VEHICLE_TYPES);

            bool onlyMonorailLanes = sortedLanes.Count > 0;

            if (args.IsInteractive) {
                foreach (LanePos laneData in sortedLanes) {
                    byte laneIndex = laneData.laneIndex;
                    NetInfo.Lane laneInfo = segmentInfo.m_lanes[laneIndex];

                    if ((laneInfo.m_vehicleType & VehicleInfo.VehicleType.Monorail) ==
                        VehicleInfo.VehicleType.None) {
                        onlyMonorailLanes = false;
                        break;
                    }
                }
            }

            var directions = new HashSet<NetInfo.Direction>();
            int sortedLaneIndex = -1;

            // Main grid for large icons
            var grid = new Highlight.Grid(
                gridOrigin: drawOriginPos,
                cellWidth: signSize,
                cellHeight: signSize,
                xu: xu,
                yu: yu);

            // Sign renderer logic and chosen texture for signs
            SignRenderer signRenderer = default;
            // For non-square road sign theme, need square renderer to display no-override
            SignRenderer squareSignRenderer = default;
            // Defaults have 1:1 ratio (square textures)
            Vector2 largeRatio = drawEnv.drawDefaults_
                                     ? SpeedLimitTextures.DefaultSpeedlimitsAspectRatio()
                                     : drawEnv.signsThemeAspectRatio_;

            // Signs are rendered in a grid starting from col 0
            float signColumn = 0f;
            var colorController = new OverlayHandleColorController(args.IsInteractive);

            //-----------------------
            // For all lanes sorted
            //-----------------------
            foreach (LanePos laneData in sortedLanes) {
                ++sortedLaneIndex;
                uint laneId = laneData.laneId;
                byte laneIndex = laneData.laneIndex;

                NetInfo.Lane laneInfo = segmentInfo.m_lanes[laneIndex];
                if (!directions.Contains(laneInfo.m_finalDirection)) {
                    if (directions.Count > 0) {
                        signColumn += 1f; // full space between opposite directions
                    }

                    directions.Add(laneInfo.m_finalDirection);
                }

                Vector3 worldPos = grid.GetPositionForRowCol(signColumn, 0);
                bool visible = GeometryUtil.WorldToScreenPoint(worldPos, out Vector3 screenPos);

                if (!visible) {
                    continue;
                }

                float visibleScale = 100.0f / (worldPos - camPos).magnitude;
                float size = (args.IsInteractive ? 1f : 0.8f) * SPEED_LIMIT_SIGN_SIZE * visibleScale;
                Rect signScreenRect = signRenderer.Reset(screenPos, size: largeRatio * size);

                // Set render transparency based on mouse hover
                bool isHoveredHandle = args.IsInteractive && signRenderer.ContainsMouse(args.Mouse);

                // Sqrt(visibleScale) makes fade start later as distance grows
                colorController.SetGUIColor(
                    hovered: isHoveredHandle,
                    intersectsGuiWindows: args.IntersectsAnyUIRect(signScreenRect),
                    opacityMultiplier: Mathf.Sqrt(visibleScale));

                // Get speed limit override for the lane
                GetSpeedLimitResult overrideSpeedlimit =
                    SpeedLimitManager.Instance.GetCustomSpeedLimit(laneId);

                if (!overrideSpeedlimit.OverrideValue.HasValue
                    || (overrideSpeedlimit.DefaultValue.HasValue &&
                        overrideSpeedlimit.OverrideValue.Value.Equals(
                            overrideSpeedlimit.DefaultValue.Value)))
                {
                    squareSignRenderer.Reset(
                        screenPos,
                        size: size * SpeedLimitTextures.DefaultSpeedlimitsAspectRatio());
                    squareSignRenderer.DrawLargeTexture(SpeedLimitTextures.NoOverride);
                    squareSignRenderer.DrawSmallTexture(
                        speedlimit: overrideSpeedlimit.DefaultValue,
                        textureSource: SpeedLimitTextures.RoadDefaults);
                } else {
                    signRenderer.DrawLargeTexture(
                        speedlimit: overrideSpeedlimit.OverrideValue.Value,
                        textureSource: drawEnv.largeSignsTextures_);
                }

                if (args.IsInteractive
                    && !onlyMonorailLanes
                    && ((laneInfo.m_vehicleType & VehicleInfo.VehicleType.Monorail) != VehicleInfo.VehicleType.None))
                {
                    Texture2D tex1 = RoadUI.VehicleInfoSignTextures[
                        LegacyExtVehicleType.ToNew(old: ExtVehicleType.PassengerTrain)];

                    // TODO: Replace with direct call to GUI.DrawTexture as in the func above
                    grid.DrawStaticSquareOverlayGridTexture(
                        texture: tex1,
                        camPos: camPos,
                        x: signColumn,
                        y: 1f,
                        size: SPEED_LIMIT_SIGN_SIZE,
                        screenRect: out Rect _);
                }

                if (isHoveredHandle) {
                    // Clickable overlay (interactive signs also True):
                    // Register the position of a mouse-hovered speedlimit overlay icon
                    args.HoveredLaneHandles.Add(
                        new OverlayLaneSpeedlimitHandle(
                            segmentId: segmentId,
                            laneId: laneId,
                            laneIndex: laneIndex,
                            laneInfo: laneInfo,
                            sortedLaneIndex: sortedLaneIndex));

                    this.segmentId_ = segmentId;
                    ret = true;
                }

                signColumn += 1f;
            }

            colorController.RestoreGUIColor();
            return ret;
        }
    }

    // end class
}