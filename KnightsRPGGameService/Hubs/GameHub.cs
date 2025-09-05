using KnightsRPGGame.Service.GameAPI.GameComponents;
using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using KnightsRPGGame.Service.GameAPI.GameComponents.Entities.Mongo;
using KnightsRPGGame.Service.GameAPI.Hubs.Interfaces;
using KnightsRPGGame.Service.GameAPI.Repository;
using Microsoft.AspNetCore.SignalR;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.Hubs;

public class GameHub : Hub<IGameClient>
{
    private readonly FrameStreamer _frameStreamer;
    private readonly RoomManager _roomManager;
    private static readonly Dictionary<string, DateTime> _lastShotTime = new();
    private static readonly TimeSpan ShotCooldown = TimeSpan.FromMilliseconds(500);
    private readonly object _botSpawnerLock = new();

    private readonly IGameResultRepository _gameResultRepository;

    public GameHub(FrameStreamer frameStreamer, RoomManager roomManager, IGameResultRepository gameResultRepository)
    {
        _frameStreamer = frameStreamer;
        _roomManager = roomManager;
        _gameResultRepository = gameResultRepository;
    }

    public override async Task OnConnectedAsync() => await base.OnConnectedAsync();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _frameStreamer.RemovePlayer(Context.ConnectionId);

        var roomName = _roomManager.GetRoomNameByConnection(Context.ConnectionId);
        if (roomName != null)
        {
            _roomManager.RemovePlayerFromRoom(roomName, Context.ConnectionId);
            await Clients.Group(roomName).PlayerLeft(Context.ConnectionId);
            if (_roomManager.GetPlayersInRoom(roomName).Count == 0) // TODO Двойная проверка на то, что комната пуста
            {
                TryShutdownRoomIfEmpty(roomName);
                _roomManager.RemoveRoom(roomName);
            } else {
                await UpdatePlayerList(roomName);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task CreateRoom(string roomName, string playerName, int maxPlayers = 4)
    {
        if (!_roomManager.CreateRoom(roomName, Context.ConnectionId, maxPlayers))
        {
            await Clients.Caller.Error("Room already exists.");
            return;
        }

        _roomManager.AddPlayerToRoom(roomName, Context.ConnectionId, playerName);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Caller.RoomCreated(roomName);
    }

    public async Task JoinRoom(string roomName, string playerName)
    {
        var room = _roomManager.GetRoom(roomName);
        if (room == null)
        {
            await Clients.Caller.Error("Room does not exist.");
            return;
        }

        if (room.State.IsGameStarted)
        {
            await Clients.Caller.Error("Cannot join, game already started.");
            return;
        }

        if (!_roomManager.AddPlayerToRoom(roomName, Context.ConnectionId, playerName))
        {
            await Clients.Caller.Error("Failed to join room (room full or already joined).");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Caller.PlayerJoined(Context.ConnectionId);
        await UpdatePlayerList(roomName);
    }


    public async Task LeaveRoom(string roomName)
    {
        _roomManager.RemovePlayerFromRoom(roomName, Context.ConnectionId);
        if (_roomManager.GetPlayersInRoom(roomName).Count == 0)
        {
            TryShutdownRoomIfEmpty(roomName);
            _roomManager.RemoveRoom(roomName);
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName).PlayerLeft(Context.ConnectionId);
        await UpdatePlayerList(roomName);
    }

    public async Task TogglePause(string roomName)
    {
        var room = _roomManager.GetRoom(roomName);
        if (room == null)
        {
            await Clients.Caller.Error("Комната не найдена.");
            return;
        }

        if (room.LeaderConnectionId != Context.ConnectionId)
        {
            await Clients.Caller.Error("Только лидер комнаты может ставить паузу.");
            return;
        }

        if (!room.State.IsPaused)
        {
            _frameStreamer.PauseRoom(room);
        } else
        {
            _frameStreamer.ResumeRoom(room);
        }

        await Clients.Group(roomName).GamePaused(room.State.IsPaused);

        Console.WriteLine($"[GameHub] Room '{roomName}' paused: {room.State.IsPaused}");
    }

    public async Task UpdatePlayerList(string roomName)
    {
        var room = _roomManager.GetRoom(roomName);

        await Clients.Group(roomName).ReceivePlayerList(new PlayerInfoResponseDto
        {
            PlayerInfos = _roomManager.GetPlayersInRoom(roomName),
            LeaderConnectionId = room?.LeaderConnectionId
        });
    }

    public async Task StartGame(string roomName)
    {
        var room = _roomManager.GetRoom(roomName);

        if (room == null)
        {
            await Clients.Caller.Error("Room does not exist.");
            return;
        }

        if (room.State.IsGameStarted)
        {
            await Clients.Caller.Error("Cannot join, game already started.");
            return;
        }

        room.State.IsGameStarted = true;

        var playerPositions = new Dictionary<string, PlayerStateDto>();
        var botPositions = new Dictionary<string, BotStateDto>();

        var players = _roomManager.GetPlayersInRoom(roomName);
        foreach (var player in players)
        {
            var pos = new Vector2(0, 0);
            _frameStreamer.RegisterPlayer(player.ConnectionId, pos);
            playerPositions[player.ConnectionId] = new PlayerStateDto { X = pos.X, Y = pos.Y };
        }
        Console.WriteLine($"StartGame {Context.ConnectionId}");

        _frameStreamer.StartStreamingForRoom(roomName);

        await Clients.Group(roomName).GameStarted(playerPositions, botPositions);
    }

    public async Task StopGame(string roomName)
    {
        TryShutdownRoomIfEmpty(roomName);
    }

    public async Task Shoot()
    {
        var connectionId = Context.ConnectionId;

        var roomName = _roomManager.GetRoomNameByConnection(connectionId); // TODO Упростить
        if (roomName == null)
        {
            await Clients.Caller.Error("Имя комнаты не найдено.");
            return;
        }

        var room = _roomManager.GetRoom(roomName); // TODO Вместо 2 методов 2 перегрузка
        if (room == null)
        {
            await Clients.Caller.Error("Комната не найдена.");
            return;
        }

        if (!room.State.IsGameStarted || room.State.IsPaused)
            return;

        var now = DateTime.UtcNow;

        if (_lastShotTime.TryGetValue(connectionId, out var lastShot) && now - lastShot < ShotCooldown)
            return;

        _lastShotTime[connectionId] = now;

        if (_frameStreamer.TryGetPlayerPosition(connectionId, out var position))
        {
            var bullet = new BulletDto
            {
                OwnerId = connectionId,
                X = position.X,
                Y = position.Y,
                VelocityX = 0,
                VelocityY = -300
            };

            room.State.PlayerBullets[bullet.Id] = bullet;

            await _frameStreamer.ProcessShot(connectionId, bullet);
            await Clients.Group(roomName).SpawnBullet(bullet);
        }
    }

    public Task PerformAction(string action)
    {
        if (Enum.TryParse<PlayerAction>(action, out var parsedAction))
        {
            _frameStreamer.UpdatePlayerAction(Context.ConnectionId, parsedAction);
        }
        return Task.CompletedTask;
    }

    public async Task ReportDeath(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return;

        var connectionId = Context.ConnectionId;
        var roomName = _roomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = _roomManager.GetRoom(roomName);
        if (room == null) return;

        var state = room.State;

        if (state.Players.TryGetValue(connectionId, out var player))
        {
            var result = new GameResultDto
            {
                PlayerName = playerName,
                Score = state.Score,
                DatePlayed = DateTime.UtcNow
            };

            await _gameResultRepository.SaveResultAsync(result);
            Console.WriteLine($"Сохранён результат: {playerName} — {state.Score}");
        }
    }

    public async Task ChangeLeader(string roomName, string newLeaderConnectionId)
    {
        var room = _roomManager.GetRoom(roomName);
        if (room == null)
        {
            await Clients.Caller.Error("Комната не найдена.");
            return;
        }

        if (room.LeaderConnectionId != Context.ConnectionId)
        {
            await Clients.Caller.Error("Только текущий лидер может передавать лидерство.");
            return;
        }

        if (!_roomManager.GetPlayersInRoom(roomName).Any(p => p.ConnectionId == newLeaderConnectionId))
        {
            await Clients.Caller.Error("Игрок не найден в комнате.");
            return;
        }

        room.LeaderConnectionId = newLeaderConnectionId;
        Console.WriteLine($"Лидер комнаты {roomName} сменён на {newLeaderConnectionId}");

        await UpdatePlayerList(roomName);
    }

    private void TryShutdownRoomIfEmpty(string roomName)
    {
        var players = _roomManager.GetPlayersInRoom(roomName);
        if (players.Count == 0)
        {
            Console.WriteLine($"[Room Shutdown]: No players left in room '{roomName}', stopping services.");
            _frameStreamer.StopStreamingForRoom(roomName);
        }
    }
}
