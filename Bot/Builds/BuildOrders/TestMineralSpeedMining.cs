﻿using System.Collections.Generic;
using Bot.GameData;
using Bot.GameSense;
using Bot.GameSense.EnemyStrategyTracking;

namespace Bot.Builds.BuildOrders;

public class TestMineralSpeedMining : IBuildOrder {
    public IReadOnlyCollection<BuildRequest> BuildRequests { get; }

    public TestMineralSpeedMining(IUnitsTracker unitsTracker) {
        BuildRequests = new List<BuildRequest>
        {
            new QuantityBuildRequest(unitsTracker, BuildType.Train, Units.Drone,    atSupply: 12),
            new QuantityBuildRequest(unitsTracker, BuildType.Train, Units.Overlord, atSupply: 13),
            new TargetBuildRequest  (unitsTracker, BuildType.Train, Units.Drone,    atSupply: 13, targetQuantity: 16),

            // Prevent anyone else from building anything else
            new QuantityBuildRequest(unitsTracker, BuildType.Train, Units.Zergling, atSupply: 16),
        };

        foreach (var buildRequest in BuildRequests) {
            buildRequest.Priority = BuildRequestPriority.VeryHigh;
            buildRequest.BlockCondition = BuildBlockCondition.All;
        }
    }

    public void PruneRequests() {}

    public void ReactTo(EnemyStrategy newEnemyStrategy) {}
}
