﻿using System.Numerics;
using Algorithms;
using Algorithms.ExtensionMethods;
using SC2APIProtocol;
using SC2Client.GameData;
using SC2Client.State;

namespace SC2Client.ExtensionMethods;

public static class Vector2Extensions {
    public static Point2D ToPoint2D(this Vector2 vector, float xOffset = 0, float yOffset = 0) {
        return new Point2D
        {
            X = vector.X + xOffset,
            Y = vector.Y + yOffset,
        };
    }

    public static float DistanceTo(this Vector2 origin, IUnit unit) {
        return Vector2.Distance(origin, unit.Position.ToVector2());
    }

    /// <summary>
    /// Gets all cells traversed by the ray from origin to destination using digital differential analyzer (DDA)
    /// </summary>
    /// <param name="origin"></param>
    /// <param name="destination"></param>
    /// <returns>The cells traversed by the ray from origin to destination</returns>
    public static HashSet<Vector2> GetPointsInBetween(this Vector2 origin, Vector2 destination) {
        var targetCellCorner = destination.AsWorldGridCorner();

        var pointsInBetween = RayCasting.RayCast(origin, destination, cellCorner => cellCorner == targetCellCorner, cell => cell.AsWorldGridCorner())
            .Select(result => result.CornerOfCell.AsWorldGridCenter())
            .ToHashSet();

        return pointsInBetween;
    }

    // TODO GD Write proper documentation
    // Distance means the radius of the square (it returns diagonal neighbors that are 1.41 units away)
    public static IEnumerable<Vector2> GetNeighbors(this Vector2 vector, int distance = 1) {
        for (var x = -distance; x <= distance; x++) {
            for (var y = -distance; y <= distance; y++) {
                if (x != 0 || y != 0) {
                    yield return vector.Translate(xTranslation: x, yTranslation: y);
                }
            }
        }
    }

    /// <summary>
    /// Returns the Vector 2 of the center of the given cell.
    /// Center of cells are on .5, e.g: (1.5, 2.5)
    /// </summary>
    /// <param name="vector"></param>
    /// <returns></returns>
    public static Vector2 AsWorldGridCenter(this Vector2 vector) {
        return new Vector2((float)Math.Floor(vector.X) + KnowledgeBase.GameGridCellRadius, (float)Math.Floor(vector.Y) + KnowledgeBase.GameGridCellRadius);
    }

    /// <summary>
    /// Returns the Vector 2 of the corner of the given cell.
    /// Corner of cells are on .0, e.g: (1.0, 2.0)
    /// </summary>
    /// <param name="vector"></param>
    /// <returns></returns>
    public static Vector2 AsWorldGridCorner(this Vector2 vector) {
        return new Vector2((float)Math.Floor(vector.X), (float)Math.Floor(vector.Y));
    }

    /// <summary>
    /// Builds a square search grid composed of all the 1x1 game cells around a center position.
    /// </summary>
    /// <param name="centerPosition">The position to search around.</param>
    /// <param name="gridRadius">The "radius" of the search grid</param>
    /// <returns></returns>
    public static IEnumerable<Vector2> BuildSearchGrid(this Vector2 centerPosition, int gridRadius) {
        var grid = new List<Vector2>();
        for (var x = centerPosition.X - gridRadius; x <= centerPosition.X + gridRadius; x++) {
            for (var y = centerPosition.Y - gridRadius; y <= centerPosition.Y + gridRadius; y++) {
                grid.Add(new Vector2(x, y));
            }
        }

        return grid.OrderBy(position => centerPosition.DistanceTo(position));
    }

    /// <summary>
    /// Builds a circle search area composed of all the 1x1 game cells around a center position.
    /// </summary>
    /// <param name="centerPosition">The position to search around</param>
    /// <param name="searchRadius">The radius of the search area</param>
    /// <returns></returns>
    public static IEnumerable<Vector2> BuildSearchRadius(this Vector2 centerPosition, float searchRadius) {
        return BuildSearchGrid(centerPosition, (int)searchRadius + 1)
            .Where(cell => cell.DistanceTo(centerPosition) <= searchRadius);
    }
}
