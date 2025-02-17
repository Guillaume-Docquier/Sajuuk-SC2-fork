﻿using System.Numerics;
using SC2APIProtocol;
using SC2Client.ExtensionMethods;
using SC2Client.GameData;

namespace SC2Client;

/// <summary>
/// A collection of factory methods to create SC2 API requests.
/// The requests can be sent using an ISc2Client.
/// </summary>
public static class RequestBuilder {
    /// <summary>
    /// Creates an SC2 API request to create a computer game.
    /// </summary>
    /// <param name="realTime">Whether the game should be played in real time.</param>
    /// <param name="mapFilePath">The file path of the map to play on.</param>
    /// <param name="opponentRace">The race of the computer opponent.</param>
    /// <param name="opponentDifficulty">The difficulty of the computer opponent.</param>
    /// <returns>The request to create a computer game.</returns>
    public static Request RequestCreateComputerGame(bool realTime, string mapFilePath, Race opponentRace, Difficulty opponentDifficulty) {
        var requestCreateGame = new RequestCreateGame
        {
            Realtime = realTime,
            LocalMap = new LocalMap
            {
                MapPath = mapFilePath,
            }
        };

        requestCreateGame.PlayerSetup.Add(new PlayerSetup
        {
            Type = PlayerType.Participant,
        });

        requestCreateGame.PlayerSetup.Add(new PlayerSetup
        {
            Type = PlayerType.Computer,
            Race = opponentRace,
            Difficulty = opponentDifficulty,
        });

        return new Request
        {
            CreateGame = requestCreateGame,
        };
    }

    /// <summary>
    /// Creates an SC2 API request to join an AIArena ladder game.
    /// </summary>
    /// <param name="race">The race to play as.</param>
    /// <param name="startPort">The AIArena start port provided as CLI arguments.</param>
    /// <returns>The request to join a ladder game.</returns>
    public static Request RequestJoinLadderGame(Race race, int startPort) {
        var requestJoinGame = new RequestJoinGame
        {
            Race = race,
            SharedPort = startPort + 1,
            ServerPorts = new PortSet
            {
                GamePort = startPort + 2,
                BasePort = startPort + 3,
            },
            Options = GetInterfaceOptions(),
        };

        requestJoinGame.ClientPorts.Add(new PortSet
        {
            GamePort = startPort + 4,
            BasePort = startPort + 5
        });

        return new Request
        {
            JoinGame = requestJoinGame,
        };
    }

    /// <summary>
    /// Creates an SC2 API request to join a local game.
    /// </summary>
    /// <param name="race">The race to play as.</param>
    /// <returns>The request to join a local game.</returns>
    public static Request RequestJoinLocalGame(Race race) {
        return new Request
        {
            JoinGame = new RequestJoinGame
            {
                Race = race,
                Options = GetInterfaceOptions(),
            }
        };
    }

    /// <summary>
    /// Creates game interface options that determines what data the game sends when sending game state on each frame.
    /// </summary>
    /// <returns>The interface options.</returns>
    private static InterfaceOptions GetInterfaceOptions() {
        return new InterfaceOptions
        {
            Raw = true,
            Score = true,
            ShowCloaked = true,
            ShowBurrowedShadows = true,
            // ShowPlaceholders = true, // TODO GD Consider enabling this to simplify the BuildingsTracker?
        };
    }

    /// <summary>
    /// Creates an SC2 API request to quit a game.
    /// </summary>
    /// <returns>The request to quit the game.</returns>
    public static Request RequestQuitGame() {
        return new Request
        {
            Quit = new RequestQuit()
        };
    }

    /// <summary>
    /// Requests the static game data.
    /// The Data does not change over time, you'll typically query this only once.
    /// </summary>
    /// <returns></returns>
    public static Request RequestData() {
        return new Request
        {
            Data = new RequestData
            {
                UnitTypeId = true,
                AbilityId = true,
                BuffId = true,
                EffectId = true,
                UpgradeId = true,
            }
        };
    }

    /// <summary>
    /// Requests info about the game.
    /// The GameInfo does not change over time, you'll typically query this only once.
    /// </summary>
    /// <returns></returns>
    public static Request RequestGameInfo() {
        return new Request
        {
            GameInfo = new RequestGameInfo(),
        };
    }

    /// <summary>
    /// Requests a new game observation.
    /// This will return the current game state.
    /// </summary>
    /// <param name="targetFrame">The target frame to get an observation for. In step mode, this has no effect.</param>
    /// <returns></returns>
    public static Request RequestObservation(uint targetFrame) {
        return new Request
        {
            Observation = new RequestObservation
            {
                GameLoop = targetFrame,
            },
        };
    }

    /// <summary>
    /// Requests steps in the game simulation.
    /// In real time mode, this does nothing.
    /// </summary>
    /// <param name="stepSize">The number of frames to simulate. Must be greater than 0, or you'll automatically lose the game.</param>
    /// <returns></returns>
    public static Request RequestStep(uint stepSize) {
        return new Request
        {
            Step = new RequestStep
            {
                Count = stepSize,
            }
        };
    }

    /// <summary>
    /// Requests drawing shapes in game.
    /// </summary>
    /// <param name="debugTexts">The texts to draw</param>
    /// <param name="debugSpheres">The spheres to draw</param>
    /// <param name="debugBoxes">The boxes to draw</param>
    /// <param name="debugLines">The lines to draw</param>
    /// <returns></returns>
    public static Request DebugDraw(
        IEnumerable<DebugText> debugTexts,
        IEnumerable<DebugSphere> debugSpheres,
        IEnumerable<DebugBox> debugBoxes,
        IEnumerable<DebugLine> debugLines
    ) {
        return new Request
        {
            Debug = new RequestDebug
            {
                Debug =
                {
                    new DebugCommand
                    {
                        Draw = new DebugDraw
                        {
                            Text = { debugTexts },
                            Spheres = { debugSpheres },
                            Boxes = { debugBoxes },
                            Lines = { debugLines },
                        },
                    },
                }
            }
        };
    }

    /// <summary>
    /// Executes actions as a player.
    /// </summary>
    /// <param name="actions">The actions to perform.</param>
    /// <returns></returns>
    public static Request RequestAction(IEnumerable<SC2APIProtocol.Action> actions) {
        var actionRequest = new Request
        {
            Action = new RequestAction(),
        };

        actionRequest.Action.Actions.AddRange(actions);

        return actionRequest;
    }

    /// <summary>
    /// A query to test if a building can be placed at the target location.
    /// </summary>
    /// <param name="buildingType">The building you'd want to build.</param>
    /// <param name="position">The position to build at.</param>
    /// <param name="knowledgeBase">The game's knowledge base.</param>
    /// <returns></returns>
    public static Request RequestQueryBuildingPlacement(uint buildingType, Vector2 position, KnowledgeBase knowledgeBase) {
        var requestQuery = new RequestQuery();

        requestQuery.Placements.Add(new RequestQueryBuildingPlacement
        {
            AbilityId = (int)knowledgeBase.GetUnitTypeData(buildingType).AbilityId,
            TargetPos = position.ToPoint2D(),
        });

        return new Request
        {
            Query = requestQuery,
        };
    }
}
