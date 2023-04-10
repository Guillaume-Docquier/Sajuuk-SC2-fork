﻿using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Bot.Debugging;
using Bot.Debugging.GraphicalDebugging;
using Bot.ExtensionMethods;
using Bot.MapKnowledge;
using SC2APIProtocol;

namespace Bot.GameSense.RegionTracking;

public class RegionTracker : INeedUpdating {
    public static RegionTracker Instance { get; private set; } = new RegionTracker();

    private bool _isInitialized = false;

    private IEnumerable<IRegionsEvaluator> Evaluators => _regionForceEvaluators.Values
        .Concat<IRegionsEvaluator>(_regionValueEvaluators.Values)
        .Concat(_regionThreatEvaluators.Values);

    private readonly Dictionary<Alliance, RegionsForceEvaluator> _regionForceEvaluators = new Dictionary<Alliance, RegionsForceEvaluator>();
    private readonly Dictionary<Alliance, RegionsValueEvaluator> _regionValueEvaluators = new Dictionary<Alliance, RegionsValueEvaluator>();
    private readonly Dictionary<Alliance, RegionsThreatEvaluator> _regionThreatEvaluators = new Dictionary<Alliance, RegionsThreatEvaluator>();

    private static readonly List<Color> RegionColors = new List<Color>
    {
        Colors.MulberryRed,
        Colors.MediumTurquoise,
        Colors.SunbrightOrange,
        Colors.PeachPink,
        Colors.Purple,
        Colors.LimeGreen,
        Colors.BurlywoodBeige,
        Colors.LightRed,
    };

    private RegionTracker() {
        _regionForceEvaluators[Alliance.Self] = new RegionsForceEvaluator(Alliance.Self);
        _regionForceEvaluators[Alliance.Enemy] = new RegionsForceEvaluator(Alliance.Enemy);

        _regionValueEvaluators[Alliance.Self] = new RegionsValueEvaluator(Alliance.Self);
        _regionValueEvaluators[Alliance.Enemy] = new RegionsValueEvaluator(Alliance.Enemy);

        _regionThreatEvaluators[Alliance.Enemy] = new RegionsThreatEvaluator(
            _regionForceEvaluators[Alliance.Enemy],
            _regionValueEvaluators[Alliance.Self]
        );
    }

    /// <summary>
    /// Gets the force associated with the region of a given position
    /// </summary>
    /// <param name="position">The position to get the force of</param>
    /// <param name="alliance">The alliance to get the force of</param>
    /// <param name="normalized">Whether or not to get the normalized force between 0 and 1</param>
    /// <returns>The force of the position's region</returns>
    public static float GetForce(Vector2 position, Alliance alliance, bool normalized = false) {
        return GetForce(position.GetRegion(), alliance, normalized);
    }

    /// <summary>
    /// Gets the force of a region
    /// </summary>
    /// <param name="region">The region to get the force of</param>
    /// <param name="alliance">The alliance to get the force of</param>
    /// <param name="normalized">Whether or not to get the normalized force between 0 and 1</param>
    /// <returns>The force of the region</returns>
    public static float GetForce(IRegion region, Alliance alliance, bool normalized = false) {
        if (!Instance._regionForceEvaluators.ContainsKey(alliance)) {
            Logger.Error($"Cannot get force for alliance {alliance}. We don't track that");
        }

        return Instance._regionForceEvaluators[alliance].GetEvaluation(region, normalized);
    }

    /// <summary>
    /// Gets the value associated with the region of a given position
    /// </summary>
    /// <param name="position">The position to get the value of</param>
    /// <param name="alliance">The alliance to get the value of</param>
    /// <param name="normalized">Whether or not to get the normalized value between 0 and 1</param>
    /// <returns>The value of the position's region</returns>
    public static float GetValue(Vector2 position, Alliance alliance, bool normalized = false) {
        return GetValue(position.GetRegion(), alliance, normalized);
    }

    /// <summary>
    /// Gets the value of a region
    /// </summary>
    /// <param name="region">The region to get the value of</param>
    /// <param name="alliance">The alliance to get the value of</param>
    /// <param name="normalized">Whether or not to get the normalized value between 0 and 1</param>
    /// <returns>The value of the region</returns>
    public static float GetValue(IRegion region, Alliance alliance, bool normalized = false) {
        if (!Instance._regionValueEvaluators.ContainsKey(alliance)) {
            Logger.Error($"Cannot get value for alliance {alliance}. We don't track that");
        }

        return Instance._regionValueEvaluators[alliance].GetEvaluation(region, normalized);
    }

    /// <summary>
    /// Gets the threat associated with the region of a given position.
    /// The threat is relative to the army strength, nearby base values and distance to those bases.
    /// </summary>
    /// <param name="position">The position to get the threat of</param>
    /// <param name="alliance">The alliance to get the threat of</param>
    /// <param name="normalized">Whether or not to get the normalized threat between 0 and 1</param>
    /// <returns>The threat of the position's region</returns>
    public static float GetThreat(Vector2 position, Alliance alliance, bool normalized = false) {
        return GetForce(position.GetRegion(), alliance, normalized);
    }

    /// <summary>
    /// Gets the threat of a region.
    /// The threat is relative to the army strength, nearby base values and distance to those bases.
    /// </summary>
    /// <param name="region">The region to get the threat of</param>
    /// <param name="alliance">The alliance to get the threat of</param>
    /// <param name="normalized">Whether or not to get the normalized threat between 0 and 1</param>
    /// <returns>The threat of the region</returns>
    public static float GetThreat(IRegion region, Alliance alliance, bool normalized = false) {
        if (!Instance._regionThreatEvaluators.ContainsKey(alliance)) {
            Logger.Error($"Cannot get threat for alliance {alliance}. We don't track that");
        }

        return Instance._regionThreatEvaluators[alliance].GetEvaluation(region, normalized);
    }

    public void Reset() {
        Instance = new RegionTracker();
    }

    public void Update(ResponseObservation observation) {
        if (!RegionAnalyzer.IsInitialized) {
            return;
        }

        if (!_isInitialized) {
            InitEvaluators();
        }

        DrawRegionsSummary();
    }

    private void InitEvaluators() {
        foreach (var evaluator in Evaluators) {
            evaluator.Init(RegionAnalyzer.Regions);
        }

        _isInitialized = true;
    }

    /// <summary>
    /// <para>Draws a marker over each region and links with neighbors.</para>
    /// <para>The marker also indicates the force of each alliance in the region.</para>
    /// <para>Each region gets a different color using the color pool.</para>
    /// </summary>
    private static void DrawRegionsSummary() {
        if (!DebuggingFlagsTracker.IsActive(DebuggingFlags.Regions)) {
            return;
        }

        var regionIndex = 0;
        foreach (var region in RegionAnalyzer.Regions) {
            // TODO GD Set colors on each region directly, with a color different from its neighbors
            var regionColor = RegionColors[regionIndex % RegionColors.Count];

            var regionTypeText = region.Type.ToString();
            if (region.Type == RegionType.Expand) {
                regionTypeText += $" - {region.ExpandLocation.ExpandType}";
            }

            var obstructedText = region.IsObstructed ? "OBSTRUCTED" : "";

            DrawRegionMarker(region, regionColor, new[]
            {
                $"R{regionIndex} ({regionTypeText}) {obstructedText}",
                $"Self:  {GetForceLabel(region, Alliance.Self),-7} ({GetForce(region, Alliance.Self),5:F2}) | {GetValueLabel(region, Alliance.Self),-10} ({GetValue(region, Alliance.Self),5:F2})",
                $"Enemy: {GetForceLabel(region, Alliance.Enemy),-7} ({GetForce(region, Alliance.Enemy),5:F2}) | {GetValueLabel(region, Alliance.Enemy),-10} ({GetValue(region, Alliance.Enemy),5:F2})",
            }, textXOffset: -3f);

            regionIndex++;
        }
    }

    // TODO GD This might not live in here
    /// <summary>
    /// Returns a string label associated with the region's force
    /// </summary>
    /// <param name="region">The region to get the label for</param>
    /// <param name="alliance">The alliance to consider the force of</param>
    /// <returns>A string that describes the force of the region</returns>
    private static string GetForceLabel(Region region, Alliance alliance) {
        var force = GetForce(region, alliance);

        return force switch
        {
            >= UnitEvaluator.Force.Lethal => "Lethal",
            >= UnitEvaluator.Force.Strong => "Strong",
            >= UnitEvaluator.Force.Medium => "Medium",
            >= UnitEvaluator.Force.Neutral => "Neutral",
            _ => "Weak",
        };
    }

    // TODO GD This might not live in here
    /// <summary>
    /// Returns a string label associated with the region's value
    /// </summary>
    /// <param name="region">The region to get the label for</param>
    /// <param name="alliance">The alliance to consider the value of</param>
    /// <returns>A string that describes the value of the region</returns>
    private static string GetValueLabel(Region region, Alliance alliance) {
        var value = GetValue(region, alliance);

        return value switch
        {
            >= UnitEvaluator.Value.Jackpot => "Jackpot",
            >= UnitEvaluator.Value.Prized => "Prized",
            >= UnitEvaluator.Value.Valuable => "Valuable",
            >= UnitEvaluator.Value.Intriguing => "Intriguing",
            _ => "No Value",
        };
    }

    private static void DrawRegionMarker(Region region, Color regionColor, string[] texts, float textXOffset = 0f) {
        const int zOffset = 5;
        var offsetRegionCenter = region.Center.ToVector3(zOffset: zOffset);

        // Draw the marker
        Program.GraphicalDebugger.AddLink(region.Center.ToVector3(), offsetRegionCenter, color: regionColor);
        Program.GraphicalDebugger.AddTextGroup(texts, size: 14, worldPos: offsetRegionCenter.ToPoint(xOffset: textXOffset), color: regionColor);

        // Draw lines to neighbors
        foreach (var neighbor in region.Neighbors) {
            var neighborOffsetCenter = neighbor.Region.Center.ToVector3(zOffset: zOffset);
            var regionSizeRatio = (float)region.Cells.Count / (region.Cells.Count + neighbor.Region.Cells.Count);
            var lineEnd = Vector3.Lerp(offsetRegionCenter, neighborOffsetCenter, regionSizeRatio);
            Program.GraphicalDebugger.AddLine(offsetRegionCenter, lineEnd, color: regionColor);
        }
    }
}
