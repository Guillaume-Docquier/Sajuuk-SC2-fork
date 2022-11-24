﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bot.Builds;
using Bot.Builds.BuildOrders;
using Bot.Debugging;
using Bot.ExtensionMethods;
using Bot.GameSense;
using Bot.Managers;
using Bot.Managers.EconomyManagement;
using Bot.Managers.ScoutManagement;
using Bot.Managers.WarManagement;
using Bot.Scenarios;
using SC2APIProtocol;

namespace Bot;

public class SajuukBot: PoliteBot {
    private readonly List<Manager> _managers = new List<Manager>();

    public override string Name => "Sajuuk";

    public override Race Race => Race.Zerg;

    private readonly BotDebugger _debugger = new BotDebugger();

    public SajuukBot(string version, List<IScenario> scenarios = null) : base(version, scenarios) {}

    protected override Task DoOnFrame() {
        if (Controller.Frame == 0) {
            InitManagers();
        }
        else if (Controller.Frame == 2016) {
            Logger.Metric("Collected Minerals: {0}", Controller.Observation.Observation.Score.ScoreDetails.CollectedMinerals);
        }

        _managers.ForEach(manager => manager.OnFrame());

        var managerRequests = GetManagersBuildRequests();
        var buildBlockStatus = AddressManagerRequests(managerRequests);

        _debugger.Debug(
            managerRequests
                .SelectMany(groupedBySupply => groupedBySupply.SelectMany(request => request))
                .ToList(),
            buildBlockStatus
        );

        foreach (var unit in UnitsTracker.UnitsByTag.Values) {
            unit.ExecuteModules();
        }

        return Task.CompletedTask;
    }

    private void InitManagers() {
        var buildManager = new BuildManager(new TwoBasesRoach());
        _managers.Add(buildManager);

        _managers.Add(new SupplyManager(buildManager));
        _managers.Add(new ScoutManager());
        _managers.Add(new EconomyManager());
        _managers.Add(new WarManager());
        _managers.Add(new CreepManager());
        _managers.Add(new UpgradesManager());
    }

    /// <summary>
    /// Try to fulfill the manager requests in a smart order.
    /// Orders are sorted by priority and supply timing.
    /// When we add an order that changes the supply, we look back to see if a previous order now meets its timing.
    /// </summary>
    /// <param name="groupedManagersBuildRequests"></param>
    /// <returns>The blocking build fulfillment with the block reason, or (null, None) if nothing was blocking</returns>
    private static (BuildFulfillment, BuildBlockCondition) AddressManagerRequests(List<List<List<BuildFulfillment>>> groupedManagersBuildRequests) {
        var lookBackSupplyTarget = long.MaxValue;
        foreach (var priorityGroups in groupedManagersBuildRequests) {
            foreach (var supplyGroups in priorityGroups) {
                if (supplyGroups[0].AtSupply > Controller.CurrentSupply) {
                    lookBackSupplyTarget = Math.Min(lookBackSupplyTarget, supplyGroups[0].AtSupply);
                    continue;
                }

                // TODO GD Interweave requests (i.e if we have 2 requests, 10 roaches and 10 drones, do 1 roach 1 drone, 1 roach 1 drone, etc)
                foreach (var buildStep in supplyGroups) {
                    while (buildStep.Remaining > 0) {
                        var buildStepResult = Controller.ExecuteBuildStep(buildStep);
                        if (buildStepResult == BuildRequestResult.Ok) {
                            buildStep.Fulfill(1);

                            if (Controller.CurrentSupply >= lookBackSupplyTarget) {
                                return AddressManagerRequests(groupedManagersBuildRequests);
                            }
                        }
                        // Don't retry expands if they are all taken
                        else if (buildStep.BuildType == BuildType.Expand && buildStepResult.HasFlag(BuildRequestResult.NoSuitableLocation)) {
                            buildStep.Fulfill(1);
                        }
                        else if (ShouldBlock(buildStep, buildStepResult, out var buildBlockingReason)) {
                            // We must wait to fulfill this one
                            return (buildStep, buildBlockingReason);
                        }
                        else {
                            // Not possible anymore, go to next
                            break;
                        }
                    }
                }
            }
        }

        return (null, BuildBlockCondition.None);
    }

    /// <summary>
    /// Returns the managers build requests grouped by priority and supply required
    /// </summary>
    /// <returns>Nested lists containing the managers build requests grouped by priority and supply required</returns>
    private List<List<List<BuildFulfillment>>> GetManagersBuildRequests() {
        // We shuffle so that the requests are not always in the same order
        // We cannot shuffle request groups because we want to preserve the relative order of a manager's requests
        var shuffledManagers = _managers.ToList().Shuffle();

        var priorityGroups = shuffledManagers
            .SelectMany(manager => manager.BuildFulfillments.Where(buildFulfillment => buildFulfillment.Remaining > 0))
            .GroupBy(buildRequest => buildRequest.Priority)
            .OrderByDescending(group => group.Key);

        var buildFulfillments = priorityGroups
            .Select(priorityGroup => priorityGroup
                .GroupBy(buildRequest => buildRequest.AtSupply)
                // AtSupply 0 should be last because it means we don't care about the supply value
                .OrderBy(group => group.Key == 0 ? int.MaxValue : group.Key)
                .Select(supplyGroup => supplyGroup.ToList())
                .ToList()
            )
            .ToList();

        return buildFulfillments;
    }

    /// <summary>
    /// Determines if a BuildFulfillment should block the build based on its block conditions and the request result.
    /// </summary>
    /// <param name="buildFulfillment">The build fulfillment that might be blocking</param>
    /// <param name="buildRequestResult">The build request result</param>
    /// <param name="buildBlockingReason">The blocking reason, if any</param>
    /// <returns>True if the build should be blocked</returns>
    private static bool ShouldBlock(BuildFulfillment buildFulfillment, BuildRequestResult buildRequestResult, out BuildBlockCondition buildBlockingReason) {
        // TODO GD All of this is not really cute, is there a nicer way?
        if (buildRequestResult.HasFlag(BuildRequestResult.TechRequirementsNotMet) && buildFulfillment.BlockCondition.HasFlag(BuildBlockCondition.MissingTech)) {
            buildBlockingReason = BuildBlockCondition.MissingTech;
            return true;
        }

        if (buildRequestResult.HasFlag(BuildRequestResult.NotEnoughMinerals) && buildFulfillment.BlockCondition.HasFlag(BuildBlockCondition.MissingMinerals)) {
            buildBlockingReason = BuildBlockCondition.MissingMinerals;
            return true;
        }

        if (buildRequestResult.HasFlag(BuildRequestResult.NotEnoughVespeneGas) && buildFulfillment.BlockCondition.HasFlag(BuildBlockCondition.MissingVespene)) {
            buildBlockingReason = BuildBlockCondition.MissingVespene;
            return true;
        }

        if (buildRequestResult.HasFlag(BuildRequestResult.NoProducersAvailable) && buildFulfillment.BlockCondition.HasFlag(BuildBlockCondition.MissingProducer)) {
            buildBlockingReason = BuildBlockCondition.MissingProducer;
            return true;
        }

        buildBlockingReason = BuildBlockCondition.None;
        return false;
    }
}
