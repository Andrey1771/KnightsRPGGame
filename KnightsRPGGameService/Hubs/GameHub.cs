using KnightsRPGGame.Service.GameAPI.GameComponents;
using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using KnightsRPGGame.Service.GameAPI.Hubs.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.Hubs;

public class GameHub : Hub<IGameClient>
{
    private static GameManager _game;
    private readonly FrameStreamer _frameStreamer;

    private static readonly Dictionary<string, DateTime> _lastShotTime = new();
    private static readonly TimeSpan ShotCooldown = TimeSpan.FromMilliseconds(500);

    private readonly Dictionary<string, CancellationTokenSource> _botSpawners = new();
    private readonly object _botSpawnerLock = new();

    public GameHub(GameManager game, FrameStreamer frameStreamer)
    {
        _game = game;
        _frameStreamer = frameStreamer;
    }

    // -------------------- Подключение/Отключение --------------------

    public override async Task OnConnectedAsync() => await base.OnConnectedAsync();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _frameStreamer.RemovePlayer(Context.ConnectionId);

        if (!_frameStreamer.HasPlayer())
            _frameStreamer.StopStreaming();

        RoomManager.RemovePlayerFromAllRooms(Context.ConnectionId);
        await Clients.All.PlayerLeft(Context.ConnectionId);

        await base.OnDisconnectedAsync(exception);
    }

    // -------------------- Работа с комнатами --------------------

    public async Task CreateRoom(string roomName, int maxPlayers = 4)
    {
        if (RoomManager.CreateRoom(roomName, maxPlayers))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
            RoomManager.AddPlayerToRoom(roomName, Context.ConnectionId);
            await Clients.Caller.RoomCreated(roomName);
        }
        else
        {
            await Clients.Caller.Error("Room already exists.");
        }
    }

    public async Task JoinRoom(string roomName)
    {
        if (RoomManager.AddPlayerToRoom(roomName, Context.ConnectionId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
            await Clients.Group(roomName).PlayerJoined(Context.ConnectionId);

            var players = RoomManager.GetPlayersInRoom(roomName);
            await Clients.Group(roomName).ReceivePlayerList(players);
        }
        else
        {
            await Clients.Caller.Error("Failed to join room (room full or doesn't exist).");
        }
    }

    public async Task LeaveRoom(string roomName)
    {
        RoomManager.RemovePlayerFromRoom(roomName, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);

        await Clients.Group(roomName).PlayerLeft(Context.ConnectionId);
        var players = RoomManager.GetPlayersInRoom(roomName);
        await Clients.Group(roomName).ReceivePlayerList(players);
    }

    public async Task RequestPlayerList(string roomName)
    {
        var players = RoomManager.GetPlayersInRoom(roomName);
        await Clients.Caller.ReceivePlayerList(players);
    }

    public Task<string> GetConnectionId() => Task.FromResult(Context.ConnectionId);

    // -------------------- Управление игрой --------------------

    public async Task StartGame(string roomName)
    {
        var players = RoomManager.GetPlayersInRoom(roomName);
        var playerPositions = new Dictionary<string, PlayerStateDto>();
        var botPositions = new Dictionary<string, PlayerStateDto>();

        foreach (var connectionId in players)
        {
            var pos = new Vector2(0, 0);
            _frameStreamer.RegisterPlayer(connectionId, pos);

            playerPositions[connectionId] = new PlayerStateDto { X = pos.X, Y = pos.Y };
        }

        // Тестовый бот (можно убрать)
        var botId = Guid.NewGuid().ToString();
        var botPos = new Vector2(200, 100);
        _frameStreamer.AddEnemyBot(botId, botPos);
        botPositions[botId] = new PlayerStateDto { X = botPos.X, Y = botPos.Y };

        await Clients.Group(roomName).GameStarted(playerPositions, botPositions);

        foreach (var connectionId in players)
            _frameStreamer.StartStreaming(connectionId);

        StartBotSpawningLoop(roomName);
    }

    public async Task StopGame(string roomName)
    {
        _frameStreamer.StopStreaming();
        StopBotSpawningLoop(roomName);
    }

    // -------------------- Игровые действия --------------------

    public async Task Shoot()
    {
        var connectionId = Context.ConnectionId;
        var now = DateTime.UtcNow;

        if (_lastShotTime.TryGetValue(connectionId, out var lastShot) &&
            now - lastShot < ShotCooldown)
        {
            return;
        }

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

            await _frameStreamer.ProcessShot(connectionId, bullet);

            await Clients.All.BulletFired(connectionId, new PlayerStateDto { X = position.X, Y = position.Y });
            await Clients.All.SpawnBullet(bullet);
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

    // -------------------- Боты --------------------

    private void StartBotSpawningLoop(string roomName)
    {
        lock (_botSpawnerLock)
        {
            if (_botSpawners.ContainsKey(roomName))
                return;

            var cts = new CancellationTokenSource();
            _botSpawners[roomName] = cts;
            var token = cts.Token;
            var random = new Random();

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), token);

                        var botId = Guid.NewGuid().ToString();
                        var botPos = new Vector2(random.Next(50, 750), 0);
                        _frameStreamer.AddEnemyBot(botId, botPos);

                        await Clients.Group(roomName).ReceiveBotList(new Dictionary<string, PlayerStateDto>
                        {
                            { botId, new PlayerStateDto { X = botPos.X, Y = botPos.Y } }
                        });
                    }
                    catch (TaskCanceledException) { }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[BotSpawner Error]: {ex.Message}");
                    }
                }
            }, token);
        }
    }

    private void StopBotSpawningLoop(string roomName)
    {
        lock (_botSpawnerLock)
        {
            if (_botSpawners.TryGetValue(roomName, out var cts))
            {
                cts.Cancel();
                _botSpawners.Remove(roomName);
            }
        }
    }
}
