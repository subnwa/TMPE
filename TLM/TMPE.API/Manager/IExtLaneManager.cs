using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TrafficManager.API.Manager {
    public interface IExtLaneManager {
        /// <summary>
        /// Returns the lane Index of the given lane.
        /// </summary>
        /// <param name="laneId">The Lane ID</param>
        /// <returns>Lane Index of the given lane if valid, -1 otherwise</returns>
        int GetLaneIndex(uint laneId);

        /// <summary>
        /// Returns the prefab info for the specified Lane ID.
        /// </summary>
        /// <param name="laneId">a Lane ID</param>
        /// <returns>prefab info for the lane</returns>
        NetInfo.Lane GetLaneInfo(uint laneId);
    }
}
