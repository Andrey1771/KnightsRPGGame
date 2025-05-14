using KnightsRPGGame.Service.GameAPI.GameComponents;
using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using KnightsRPGGame.Service.GameAPI.Hubs.Interfaces;
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

    public GameHub(FrameStreamer frameStreamer, RoomManager roomManager)
    {
        _frameStreamer = frameStreamer;
        _roomManager = roomManager;
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
            if (_roomManager.GetPlayersInRoom(roomName).Count == 0)
            {
                TryShutdownRoomIfEmpty(roomName);
                _roomManager.RemoveRoom(roomName);
            }
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task CreateRoom(string roomName, int maxPlayers = 4)
    {
        if (!_roomManager.CreateRoom(roomName, maxPlayers))
        {
            await Clients.Caller.Error("Room already exists.");
            return;
        }

        _roomManager.AddPlayerToRoom(roomName, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Caller.RoomCreated(roomName);
    }

    public async Task JoinRoom(string roomName)
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

        if (!_roomManager.AddPlayerToRoom(roomName, Context.ConnectionId))
        {
            await Clients.Caller.Error("Failed to join room (room full or already joined).");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName).PlayerJoined(Context.ConnectionId);
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

    public async Task UpdatePlayerList(string roomName)
    {
        var players = _roomManager.GetPlayersInRoom(roomName);
        await Clients.Group(roomName).ReceivePlayerList(players);
    }

    public Task<string> GetConnectionId() => Task.FromResult(Context.ConnectionId);

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
        var botPositions = new Dictionary<string, PlayerStateDto>();

        foreach (var connectionId in room.Players)
        {
            var pos = new Vector2(0, 0);
            _frameStreamer.RegisterPlayer(connectionId, pos);
            playerPositions[connectionId] = new PlayerStateDto { X = pos.X, Y = pos.Y };
        }

        await Clients.Group(roomName).GameStarted(playerPositions, botPositions);

        foreach (var connectionId in room.Players)
        {
            _frameStreamer.StartStreaming(connectionId);
        }

        StartBotSpawningLoop(roomName);
    }

    public async Task StopGame(string roomName)
    {
        TryShutdownRoomIfEmpty(roomName);
    }

    public async Task Shoot()
    {
        var connectionId = Context.ConnectionId;
        var roomName = _roomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

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

            var room = _roomManager.GetRoom(roomName);
            if (room != null)
            {
                room.State.PlayerBullets[bullet.Id] = bullet;
            }

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

    private void StartBotSpawningLoop(string roomName)
    {
        lock (_botSpawnerLock)
        {
            var room = _roomManager.GetRoom(roomName);
            if (room == null || room.State.BotSpawners.ContainsKey(roomName)) return;

            var cts = new CancellationTokenSource();
            room.State.BotSpawners[roomName] = cts;
            var token = cts.Token;
            var random = new Random();

            _ = Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {

                        Console.WriteLine($"[BotSpawner Work]: Room {roomName}");
                        await Task.Delay(TimeSpan.FromSeconds(5), token);

                        if (room.State.Bots.Count > 7 /*TODO ограничение на количество спавн ботов*/)
                        {
                            continue;
                        }

                        var botId = Guid.NewGuid().ToString();
                        var botPos = new Vector2(random.Next(50, 640 - 50/*TODO Размер Карты*/), 0);
                        _frameStreamer.AddEnemyBot(botId, botPos, roomName);

                        var updatedRoom = _roomManager.GetRoom(roomName);
                        if (updatedRoom != null)
                        {
                            updatedRoom.State.Bots[botId].Position = botPos;
                            await Clients.Group(roomName).ReceiveBotList(new Dictionary<string, PlayerStateDto>
                            {
                                { botId, new PlayerStateDto { X = botPos.X, Y = botPos.Y } }
                            });
                        }
                    }
                }
                catch (TaskCanceledException)
                {
                    Console.WriteLine($"[BotSpawner Stopped]: Room {roomName}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[BotSpawner Error]: {ex.Message}");
                }
            }, token);
        }
    }

    private void StopBotSpawningLoop(string roomName)
    {
        lock (_botSpawnerLock)
        {
            var room = _roomManager.GetRoom(roomName);
            if (room != null && room.State.BotSpawners.TryGetValue(roomName, out var cts))
            {
                cts.Cancel();
                room.State.BotSpawners.Remove(roomName);
                Console.WriteLine($"[BotSpawner Removed]: Room {roomName}");
            }
        }
    }

    private void TryShutdownRoomIfEmpty(string roomName)
    {
        var players = _roomManager.GetPlayersInRoom(roomName);
        if (players.Count == 0)
        {
            Console.WriteLine($"[Room Shutdown]: No players left in room '{roomName}', stopping services.");
            _frameStreamer.StopStreaming();
            StopBotSpawningLoop(roomName);
        }
    }
}
