﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Bot.Actions;
using Bot.Debugging.GraphicalDebugging;
using Bot.GameData;
using Bot.GameSense;
using Bot.MapAnalysis;
using Bot.Requests;
using Bot.Utils;
using SC2APIProtocol;

namespace Bot.Wrapper;

public class LocalBotRunner : IBotRunner {
    private readonly ISc2Client _sc2Client;
    private readonly IRequestService _requestService;
    private readonly IRequestBuilder _requestBuilder;
    private readonly KnowledgeBase _knowledgeBase;
    private readonly IFrameClock _frameClock;
    private readonly IController _controller;
    private readonly IActionService _actionService;
    private readonly IGraphicalDebugger _graphicalDebugger;
    private readonly IUnitsTracker _unitsTracker;
    private readonly IPathfinder _pathfinder;

    private readonly IBot _bot;
    private readonly string _mapFileName;
    private readonly Race _opponentRace;
    private readonly Difficulty _opponentDifficulty;
    private readonly uint _stepSize;
    private readonly bool _realTime;

    private const string ServerAddress = "127.0.0.1";

    private string _starcraftExe;
    private string _starcraftDir;
    private string _starcraftMapsDir;

    // TODO GD Could be injected
    private readonly PerformanceDebugger _performanceDebugger = new PerformanceDebugger();
    private static readonly ulong DebugMemoryEvery = TimeUtils.SecsToFrames(5);

    public LocalBotRunner(
        ISc2Client sc2Client,
        IRequestService requestService,
        IRequestBuilder requestBuilder,
        KnowledgeBase knowledgeBase,
        IFrameClock frameClock,
        IController controller,
        IActionService actionService,
        IGraphicalDebugger graphicalDebugger,
        IUnitsTracker unitsTracker,
        IPathfinder pathfinder,
        IBot bot,
        string mapFileName,
        Race opponentRace,
        Difficulty opponentDifficulty,
        uint stepSize,
        bool realTime
    ) {
        _sc2Client = sc2Client;
        _requestService = requestService;
        _requestBuilder = requestBuilder;
        _knowledgeBase = knowledgeBase;
        _frameClock = frameClock;
        _controller = controller;
        _actionService = actionService;
        _graphicalDebugger = graphicalDebugger;
        _unitsTracker = unitsTracker;
        _pathfinder = pathfinder;

        _bot = bot;
        _mapFileName = mapFileName;
        _opponentRace = opponentRace;
        _opponentDifficulty = opponentDifficulty;
        _stepSize = stepSize;
        _realTime = realTime;
    }

    public async Task PlayGame() {
        const int gamePort = 5678;

        Logger.Info("Finding the SC2 executable info");
        FindExecutableInfo();

        Logger.Info("Starting SinglePlayer Instance");
        StartSc2Instance(gamePort);

        await _sc2Client.Connect(ServerAddress, gamePort);

        Logger.Info($"Creating game on map: {_mapFileName}");
        await CreateGame(_mapFileName, _opponentRace, _opponentDifficulty, _realTime);

        Logger.Info("Joining game");
        var playerId = await JoinGame(_bot.Race);
        await Run(_bot, playerId);
    }

    private void FindExecutableInfo() {
        var myDocuments = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var executeInfo = Path.Combine(myDocuments, "StarCraft II", "ExecuteInfo.txt");

        if (File.Exists(executeInfo)) {
            var lines = File.ReadAllLines(executeInfo);
            foreach (var line in lines) {
                if (line.Trim().StartsWith("executable")) {
                    _starcraftExe = line.Substring(line.IndexOf('=') + 1).Trim();
                    _starcraftDir = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(_starcraftExe))); //we need 2 folders down
                    if (_starcraftDir != null) {
                        _starcraftMapsDir = Path.Combine(_starcraftDir, "Maps");
                    }

                    break;
                }
            }
        }

        if (_starcraftExe == default) {
            throw new Exception($"Unable to find:{executeInfo}. Make sure you started the game successfully at least once.");
        }
    }

    private void StartSc2Instance(int port) {
        var processStartInfo = new ProcessStartInfo(_starcraftExe)
        {
            // TODO GD Make and enum for this
            // DisplayMode 0: Windowed
            // DisplayMode 1: Full screen
            Arguments = $"-listen {ServerAddress} -port {port} -displayMode 1",
            WorkingDirectory = Path.Combine(_starcraftDir, "Support64")
        };

        Logger.Info("Launching SC2:");
        Logger.Info("--> File: {0}", _starcraftExe);
        Logger.Info("--> Working Dir: {0}", processStartInfo.WorkingDirectory);
        Logger.Info("--> Arguments: {0}", processStartInfo.Arguments);
        Process.Start(processStartInfo);
    }

    private async Task CreateGame(string mapFileName, Race opponentRace, Difficulty opponentDifficulty, bool realTime) {;
        var mapPath = Path.Combine(_starcraftMapsDir, mapFileName);
        if (!File.Exists(mapPath)) {
            Logger.Error($"Unable to locate map: {mapPath}");
            throw new Exception($"Unable to locate map: {mapPath}");
        }

        var createGameResponse = await _requestService.SendRequest(_requestBuilder.RequestCreateComputerGame(realTime, mapPath, opponentRace, opponentDifficulty), logErrors: true);

        // TODO GD This might be broken now, used to be ResponseJoinGame.Types.Error.Unset (0) but it doesn't exist anymore
        if (createGameResponse.CreateGame.Error != ResponseCreateGame.Types.Error.MissingMap) {
            Logger.Error($"CreateGame error: {createGameResponse.CreateGame.Error.ToString()}");
            if (!string.IsNullOrEmpty(createGameResponse.CreateGame.ErrorDetails)) {
                Logger.Error(createGameResponse.CreateGame.ErrorDetails);
            }
        }
    }

    private async Task<uint> JoinGame(Race race) {
        var joinGameResponse = await _requestService.SendRequest(_requestBuilder.RequestJoinLocalGame(race), logErrors: true);

        // TODO GD This might be broken now, used to be ResponseJoinGame.Types.Error.Unset (0) but it doesn't exist anymore
        if (joinGameResponse.JoinGame.Error != ResponseJoinGame.Types.Error.MissingParticipation) {
            Logger.Error($"JoinGame error: {joinGameResponse.JoinGame.Error.ToString()}");
            if (!string.IsNullOrEmpty(joinGameResponse.JoinGame.ErrorDetails)) {
                Logger.Error(joinGameResponse.JoinGame.ErrorDetails);
            }
        }

        return joinGameResponse.JoinGame.PlayerId;
    }

    // TODO GD This must be shared with the ladder game connection
    private async Task Run(IBot bot, uint playerId) {
        var dataRequest = new Request
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
        var dataResponse = await _requestService.SendRequest(dataRequest);
        _knowledgeBase.Data = dataResponse.Data;

        while (true) {
            // _frameClock.CurrentFrame is uint.MaxValue until we request frame 0
            var nextFrame = _frameClock.CurrentFrame == uint.MaxValue ? 0 : _frameClock.CurrentFrame + _stepSize;
            var observationResponse = await _requestService.SendRequest(_requestBuilder.RequestObservation(nextFrame));

            if (observationResponse.Status is Status.Quit) {
                Logger.Info("Game was terminated.");
                break;
            }

            var observation = observationResponse.Observation;
            if (observationResponse.Status is Status.Ended) {
                _performanceDebugger.LogAveragePerformance();

                foreach (var result in observation.PlayerResult) {
                    if (result.PlayerId == playerId) {
                        Logger.Info("Result: {0}", result.Result);
                    }
                }

                break;
            }

            await RunBot(bot, observation);

            if (observation.Observation.GameLoop % DebugMemoryEvery == 0) {
                PrintMemoryInfo();
            }

            await _requestService.SendRequest(_requestBuilder.RequestStep(_stepSize));
        }
    }

    // TODO GD This must be shared with the ladder game connection
    private async Task RunBot(IBot bot, ResponseObservation observation) {
        var gameInfoResponse = await _requestService.SendRequest(_requestBuilder.RequestGameInfo());

        _performanceDebugger.FrameStopwatch.Start();

        _performanceDebugger.ControllerStopwatch.Start();
        _controller.NewFrame(gameInfoResponse.GameInfo, observation);
        _performanceDebugger.ControllerStopwatch.Stop();

        _performanceDebugger.BotStopwatch.Start();
        await bot.OnFrame();
        _performanceDebugger.BotStopwatch.Stop();

        _performanceDebugger.ActionsStopwatch.Start();
        var actions = _actionService.GetActions().ToList();

        if (actions.Count > 0) {
            var response = await _requestService.SendRequest(_requestBuilder.RequestAction(actions));

            var unsuccessfulActions = actions
                .Zip(response.Action.Result, (action, result) => (action, result))
                .Where(action => action.result != ActionResult.Success)
                .Select(action => $"({_knowledgeBase.GetAbilityData(action.action.ActionRaw.UnitCommand.AbilityId).FriendlyName}, {action.result})")
                .ToList();

            if (unsuccessfulActions.Count > 0) {
                Logger.Warning("Unsuccessful actions: [{0}]", string.Join("; ", unsuccessfulActions));
            }
        }
        _performanceDebugger.ActionsStopwatch.Stop();

        _performanceDebugger.DebuggerStopwatch.Start();
        var request = _graphicalDebugger.GetDebugRequest();
        if (request != null) {
            await _requestService.SendRequest(request);
        }
        _performanceDebugger.DebuggerStopwatch.Stop();

        _performanceDebugger.FrameStopwatch.Stop();

        if (_performanceDebugger.FrameStopwatch.ElapsedMilliseconds > 10) {
            _performanceDebugger.LogTimers(actions.Count);
        }

        _performanceDebugger.CompileData();
    }

    // TODO GD This must be shared with the ladder game connection
    private void PrintMemoryInfo() {
        var memoryUsedMb = Process.GetCurrentProcess().WorkingSet64 * 1e-6;
        if (memoryUsedMb > 200) {
            Logger.Performance("==== Memory Debug Start ====");
            Logger.Performance("Memory used: {0} MB", memoryUsedMb.ToString("0.00"));
            Logger.Performance("Units: {0} owned, {1} neutral, {2} enemy", _unitsTracker.OwnedUnits.Count, _unitsTracker.NeutralUnits.Count, _unitsTracker.EnemyUnits.Count);
            Logger.Performance(
                "Pathfinding cache: {0} paths, {1} tiles",
                _pathfinder.CellPathsMemory.Values.Sum(destinations => destinations.Keys.Count),
                _pathfinder.CellPathsMemory.Values.SelectMany(destinations => destinations.Values).Sum(path => path.Count)
            );
            Logger.Performance("==== Memory Debug End ====");
        }
    }
}
