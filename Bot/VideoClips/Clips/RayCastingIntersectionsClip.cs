﻿using System;
using System.Linq;
using System.Numerics;
using Bot.Debugging.GraphicalDebugging;
using Bot.ExtensionMethods;
using Bot.MapKnowledge;
using Bot.Utils;
using Bot.VideoClips.Manim.Animations;

namespace Bot.VideoClips.Clips;

public class RayCastingIntersectionsClip : Clip {
    public RayCastingIntersectionsClip() {
        var origin = new Vector2(99.5f, 52.5f);

        var cameraReadyFrame = CenterCamera(origin, (int)TimeUtils.SecsToFrames(0));
        var gridReadyFrame = ShowGrid(origin, cameraReadyFrame);
        CastRay(origin, gridReadyFrame);

        Pause(30);
    }

    private int CenterCamera(Vector2 origin, int startAt) {
        var moveCameraAnimation = new MoveCameraAnimation(origin, startAt)
            .WithDurationInSeconds(1);

        AddAnimation(moveCameraAnimation);

        return moveCameraAnimation.EndFrame;
    }

    private void CastRay(Vector2 origin, int startAt) {
        var rayCastResults = RayCasting.RayCast(origin, MathUtils.DegToRad(30), cell => !MapAnalyzer.IsWalkable(cell)).ToList();

        var previousIntersection = rayCastResults[0].RayIntersection;
        var previousAnimationEnd = startAt;
        foreach (var rayCastResult in rayCastResults) {
            var rayEnd = rayCastResult.RayIntersection;
            var lineDrawingAnimation = new LineDrawingAnimation(previousIntersection.ToVector3(), rayEnd.ToVector3(), Colors.Green, previousAnimationEnd)
                .WithConstantRate(4);

            AddAnimation(lineDrawingAnimation);

            var sphereDrawingAnimation = new SphereDrawingAnimation(rayEnd.ToVector3(), 0.1f, Colors.Purple, lineDrawingAnimation.EndFrame)
                .WithDurationInSeconds(0.5f);

            AddAnimation(sphereDrawingAnimation);

            previousIntersection = rayCastResult.RayIntersection;
            previousAnimationEnd = sphereDrawingAnimation.EndFrame + (int)TimeUtils.SecsToFrames(0.5f);
        }
    }

    private int ShowGrid(Vector2 origin, int startAt) {
        var endFrame = startAt;

        var random = new Random();
        foreach (var cell in MapAnalyzer.BuildSearchRadius(origin, 15)) {
            var rng = (float)random.NextDouble();
            var rngStartFrame = startAt + (int)TimeUtils.SecsToFrames(rng * 1.5f);
            var squareAnimation = new CellDrawingAnimation(cell.ToVector3(), rngStartFrame).WithDurationInSeconds(0.5f);

            AddAnimation(squareAnimation);

            endFrame = Math.Max(endFrame, squareAnimation.EndFrame);
        }

        return endFrame;
    }

    private void Pause(int durationInSeconds) {
        var bufferAnimation = new LineDrawingAnimation(new Vector3(), new Vector3(), Colors.Green, 0)
            .WithDurationInSeconds(durationInSeconds);

        AddAnimation(bufferAnimation);
    }
}
