﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bot.ExtensionMethods;
using Bot.GameData;
using Bot.GameSense;
using Bot.MapKnowledge;
using Bot.Utils;

namespace Bot.Managers.WarManagement.ArmySupervision.UnitsControl;

public class DroneKitingUnitsControl : IUnitsControl {
    public bool IsExecuting() {
        // No state, we can abort anytime
        return false;
    }

    public IReadOnlySet<Unit> Execute(IReadOnlySet<Unit> army) {
        var uncontrolledUnits = new HashSet<Unit>(army);

        var droneMaximumWeaponCooldown = KnowledgeBase.GetUnitTypeData(Units.Drone).Weapons[0].Speed; // Speed in frames
        var dronesThatNeedToKite = army
            .Where(unit => unit.UnitType == Units.Drone)
            // We mineral walk for a duration of 75% of the weapon cooldown.
            // This can be tweaked. The rationale is that if we walk for 50% of the cooldown, the back and forth will be the same distance.
            // However, if we're gradually moving back, then the way back will be shorter and we might be in a fighting situation before our weapons are ready.
            // On the other hand, if we walk for 100% of the cooldown, our weapons will be ready but we'll be too far to attack.
            // The ideal value is most likely between 50% and 100%, so I went for 75%
            .Where(drone => drone.RawUnitData.WeaponCooldown > (1 - 0.75) * droneMaximumWeaponCooldown)
            .ToHashSet();

        if (dronesThatNeedToKite.Count == 0) {
            return uncontrolledUnits;
        }

        var regimentCenter = Clustering.GetCenter(dronesThatNeedToKite);

        var candidateMineralFields = GetMineralFieldsToWalkTo(regimentCenter);
        if (candidateMineralFields.Count <= 0) {
            return uncontrolledUnits;
        }

        // Filter out some enemies for performance
        var potentialEnemiesToAvoid = UnitsTracker.EnemyUnits.Where(enemy => enemy.DistanceTo(regimentCenter) <= 20).ToList();
        if (potentialEnemiesToAvoid.Count <= 0) {
            return uncontrolledUnits;
        }

        foreach (var drone in dronesThatNeedToKite) {
            if (drone.IsMineralWalking()) {
                uncontrolledUnits.Remove(drone);
                continue;
            }

            var enemiesToAvoid = potentialEnemiesToAvoid.Where(enemy => enemy.DistanceTo(regimentCenter) <= 5).ToList();
            if (!enemiesToAvoid.Any(enemy => enemy.CanHitGround)) {
                // This tries to handle cases where kiting would decrease the dps because we're hitting things that don't fight back (i.e buildings in construction)
                continue;
            }

            var mineralToWalkTo = GetMineralFieldToWalkTo(drone, enemiesToAvoid, candidateMineralFields);
            if (mineralToWalkTo != null) {
                drone.Gather(mineralToWalkTo);
                uncontrolledUnits.Remove(drone);
            }
        }

        return uncontrolledUnits;
    }

    public void Reset(IReadOnlyCollection<Unit> army) {
        // No state, aborting is instantaneous
    }

    /// <summary>
    /// Gets an first non depleted expand that will route through the exist from the origin.
    /// Returns null if none were found.
    /// </summary>
    /// <param name="origin">The origin of the exit</param>
    /// <param name="exit">The exit to go through</param>
    /// <returns>An expand whose path from the origin goes through the exit, or null if none.</returns>
    private static ExpandLocation GetExpandLocationGoingThroughExit(Region origin, Region exit) {
        var exploredRegions = new HashSet<Region> { origin };
        var regionsToExplore = new Queue<Region>();
        regionsToExplore.Enqueue(exit);

        while (regionsToExplore.Count > 0) {
            var regionToExplore = regionsToExplore.Dequeue();
            exploredRegions.Add(regionToExplore);

            if (regionToExplore.Type == RegionType.Expand && !regionToExplore.ExpandLocation.IsDepleted) {
                return regionToExplore.ExpandLocation;
            }

            foreach (var neighbor in regionToExplore.GetReachableNeighbors().Where(neighbor => !exploredRegions.Contains(neighbor))) {
                regionsToExplore.Enqueue(neighbor);
            }
        }

        return null;
    }

    /// <summary>
    /// Find all mineral fields that can be used to mineral walk.
    /// - Those in the current region
    /// - One patch per exit from the current region
    ///
    /// We will return a list of tuples composed of the mineral field and a position representing the exit that the unit would go through when going to that mineral field.
    /// In the case of mineral fields in the same region as the regimentPosition, their exit will be themselves because the unit won't be funneled through an exit.
    /// </summary>
    /// <param name="regimentPosition">Where the regiment currently is</param>
    /// <returns>A list of tuples composed of the mineral field and a position representing the exit that the unit would go through when going to that mineral field.</returns>
    private static List<(Unit unit, Vector2 exit)> GetMineralFieldsToWalkTo(Vector2 regimentPosition) {
        var regimentRegion = regimentPosition.GetRegion();
        var regimentRegionExits = regimentRegion.GetReachableNeighbors();

        return regimentRegionExits.SelectMany(regimentRegionExit => {
                var expandLocation = GetExpandLocationGoingThroughExit(regimentRegion, regimentRegionExit);
                if (expandLocation == null) {
                    return Enumerable.Empty<(Unit, Vector2)>();
                }

                // When the region is the regimentRegion, we return all minerals with their own position as exit.
                // We keep all minerals because each of them will likely be in a different direction from the unit.
                if (regimentRegionExit == regimentRegion) {
                    return expandLocation.ResourceCluster
                        .Where(resource => Units.MineralFields.Contains(resource.UnitType))
                        .Select(resource => (resource, resource.Position.ToVector2()));
                }

                // When the region is not the regimentRegion, it means we'll have to go through an exit to get to the mineral field.
                // Because of this, all minerals will more or less create the same path to that exit.
                // The initial movement is going to be hard to accurately determine, so we'll use the center of the exit as an estimation.
                var resource = expandLocation.ResourceCluster
                    .First(resource => Units.MineralFields.Contains(resource.UnitType));

                return new List<(Unit, Vector2)> { (resource, regimentRegionExit.Center) };
            })
            .ToList();
    }

    /// <summary>
    /// Gets the mineral from mineralFields that the given drone should to walk to in order to avoid enemiesToAvoid.
    /// </summary>
    /// <param name="drone">The drone that needs to run</param>
    /// <param name="enemiesToAvoid">The enemies that need to be avoided</param>
    /// <param name="mineralFields">The mineral fields we can walk to</param>
    /// <returns>The mineral field to walk to, or null if there are no good choices</returns>
    private static Unit GetMineralFieldToWalkTo(Unit drone, IReadOnlyCollection<Unit> enemiesToAvoid, IEnumerable<(Unit unit, Vector2 exit)> mineralFields) {
        if (enemiesToAvoid.Count <= 0) {
            return null;
        }

        // Get the enemy-drone angle
        var enemiesCenter = Clustering.GetCenter(enemiesToAvoid);
        var enemyVector = enemiesCenter.DirectionTo(drone.Position);

        // Find the mineral field with the angle closest to enemy-drone (angle 0 = directly fleeing the enemy
        var mineralToWalkTo = mineralFields.MinBy(mineralField => {
            var mineralVector = drone.Position.ToVector2().DirectionTo(mineralField.exit);

            return Math.Abs(enemyVector.GetRadAngleTo(mineralVector));
        });

        var mineralVector = drone.Position.ToVector2().DirectionTo(mineralToWalkTo.exit);
        var mineralAngle = Math.Abs(enemyVector.GetRadAngleTo(mineralVector));
        if (mineralAngle > MathUtils.DegToRad(90)) {
            // If the best we got sends us towards the enemy, we don't do it
            return null;
        }

        return mineralToWalkTo.unit;
    }
}
