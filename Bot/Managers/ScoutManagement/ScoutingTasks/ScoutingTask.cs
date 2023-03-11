﻿using System.Collections.Generic;
using System.Numerics;

namespace Bot.Managers.ScoutManagement.ScoutingTasks;

public abstract class ScoutingTask {
    public Vector2 ScoutLocation { get; }

    /**
     * High priority is more important
     */
    public int Priority { get; }

    public int MaxScouts { get; }

    public ScoutingTask(Vector2 scoutLocation, int priority, int maxScouts) {
        ScoutLocation = scoutLocation;
        Priority = priority;
        MaxScouts = maxScouts;
    }

    public abstract bool IsComplete();

    public abstract void Cancel();

    public abstract void Execute(HashSet<Unit> scouts);
}
