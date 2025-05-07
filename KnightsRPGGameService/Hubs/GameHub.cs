using KnightsRPGGame.Service.GameAPI.GameComponents;
using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using KnightsRPGGame.Service.GameAPI.Hubs.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.Hubs;

public class GameHub : Hub<IGameClient>
{
    private readonly FrameStreamer _frameStreamer;
    private static readonly Dictionary<string, DateTime> _lastShotTime = new();
    private static readonly TimeSpan ShotCooldown = TimeSpan.FromMilliseconds(500);

    private readonly Dictionary<string, CancellationTokenSource> _botSpawners = new();
    private readonly object _botSpawnerLock = new();

    public GameHub(FrameStreamer frameStreamer)
    {
        _frameStreamer = frameStreamer;
    }

    public override async Task OnConnectedAsync() => await base.OnConnectedAsync();

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _frameStreamer.RemovePlayer(Context.ConnectionId);

        if (!_frameStreamer.HasPlayer())
        {
            _frameStreamer.StopStreaming();
            
            var room = RoomManager.GetRoomNameByConnection(Context.ConnectionId);
            if (room != null)
            {
                StopBotSpawningLoop(room);
            }
        }

        var roomName = RoomManager.GetRoomNameByConnection(Context.ConnectionId);
        if (roomName != null)
        {
            RoomManager.RemovePlayerFromRoom(roomName, Context.ConnectionId);
            await Clients.Group(roomName).PlayerLeft(Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task CreateRoom(string roomName, int maxPlayers = 4)
    {
        if (!RoomManager.CreateRoom(roomName, maxPlayers))
        {
            await Clients.Caller.Error("Room already exists.");
            return;
        }

        RoomManager.AddPlayerToRoom(roomName, Context.ConnectionId);
        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Caller.RoomCreated(roomName);
    }

    public async Task JoinRoom(string roomName)
    {
        if (!RoomManager.AddPlayerToRoom(roomName, Context.ConnectionId))
        {
            await Clients.Caller.Error("Failed to join room (room full or doesn't exist).");
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName).PlayerJoined(Context.ConnectionId);
        await UpdatePlayerList(roomName);
    }

    public async Task LeaveRoom(string roomName)
    {
        RoomManager.RemovePlayerFromRoom(roomName, Context.ConnectionId);
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, roomName);
        await Clients.Group(roomName).PlayerLeft(Context.ConnectionId);
        await UpdatePlayerList(roomName);
    }

    public async Task UpdatePlayerList(string roomName)
    {
        var players = RoomManager.GetPlayersInRoom(roomName);
        await Clients.Group(roomName).ReceivePlayerList(players);
    }

    public Task<string> GetConnectionId() => Task.FromResult(Context.ConnectionId);

    public async Task StartGame(string roomName)
    {
        var room = RoomManager.GetRoom(roomName);
        if (room == null) return;

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
            _frameStreamer.StartStreaming(connectionId);

        StartBotSpawningLoop(roomName);
    }

    public async Task StopGame(string roomName)
    {
        _frameStreamer.StopStreaming();
        StopBotSpawningLoop(roomName);
    }

    public async Task Shoot()
    {
        var connectionId = Context.ConnectionId;
        var roomName = RoomManager.GetRoomNameByConnection(connectionId);
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
                VelocityY = -300 // TODO
            };

            var room = RoomManager.GetRoom(roomName);
            if (room != null)
            {
                room.Bullets[bullet.Id] = bullet;
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
            if (_botSpawners.ContainsKey(roomName)) return;

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
                        var botPos = new Vector2(random.Next(50, 640 - 50), 0); //TODO
                        _frameStreamer.AddEnemyBot(botId, botPos, roomName);

                        var room = RoomManager.GetRoom(roomName);
                        if (room != null)
                        {
                            room.Bots[botId] = new PlayerStateDto { X = botPos.X, Y = botPos.Y };

                            await Clients.Group(roomName).ReceiveBotList(new Dictionary<string, PlayerStateDto>
                            {
                                { botId, new PlayerStateDto { X = botPos.X, Y = botPos.Y } }
                            });
                        }
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
