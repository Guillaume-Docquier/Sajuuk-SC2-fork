﻿using System.Collections.Generic;
using System.Linq;
using Bot.ExtensionMethods;
using Bot.GameSense;

namespace Bot.Managers.WarManagement.ArmySupervision.UnitsControl.SneakAttackUnitsControl;

public partial class SneakAttack {
    public class ApproachState : SneakAttackState {
        private readonly IUnitsTracker _unitsTracker;
        private readonly ITerrainTracker _terrainTracker;

        private const float SetupDistance = 1.25f;

        private const float OperationRadius = TankRange + 5;

        private readonly StuckDetector _stuckDetector = new StuckDetector();

        public ApproachState(IUnitsTracker unitsTracker, ITerrainTracker terrainTracker) {
            _unitsTracker = unitsTracker;
            _terrainTracker = terrainTracker;
        }

        public override bool IsViable(IReadOnlyCollection<Unit> army) {
            if (DetectionTracker.Instance.IsDetected(army)) {
                return false;
            }

            var priorityTargets = Context.GetPriorityTargetsInOperationRadius(army, OperationRadius);
            if (!priorityTargets.Any()) {
                return false;
            }

            // If we're engaged, it means they somehow see us, abort!
            return !IsArmyGettingEngaged(army);
        }

        protected override void Execute() {
            _stuckDetector.Tick(Context._armyCenter);
            if (_stuckDetector.IsStuck) {
                Logger.Warning("{0} army is stuck", Name);
                NextState = new TerminalState(_unitsTracker, _terrainTracker);

                return;
            }

            var closestPriorityTarget = Context.GetPriorityTargetsInOperationRadius(Context._army, OperationRadius).MinBy(enemy => enemy.DistanceTo(Context._armyCenter));
            if (closestPriorityTarget != null) {
                Context._targetPosition = closestPriorityTarget.Position.ToVector2();
                Context._isTargetPriority = true;
            }
            else if (!Context._isTargetPriority) {
                var closestVisibleEnemy = Context.GetGroundEnemiesInSight(Context._army).MinBy(enemy => enemy.DistanceTo(Context._armyCenter));
                if (closestVisibleEnemy != null) {
                    Context._targetPosition = closestVisibleEnemy.Position.ToVector2();
                    Context._isTargetPriority = false;
                }
            }

            if (Context._targetPosition == default) {
                Logger.Warning("{0} has no target", Name);
                Context._isTargetPriority = false;
                NextState = new TerminalState(_unitsTracker, _terrainTracker);

                return;
            }

            BurrowOverlings(Context._army);

            if (Context._targetPosition.DistanceTo(Context._armyCenter) > SetupDistance) {
                foreach (var soldier in Context._army.Where(soldier => soldier.IsIdle() || soldier.IsMoving() || soldier.IsAttacking())) {
                    soldier.Move(Context._targetPosition);
                }
            }
            else {
                NextState = new SetupState(_unitsTracker, _terrainTracker);
            }
        }
    }
}
