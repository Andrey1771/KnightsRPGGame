using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using KnightsRPGGame.Service.GameAPI.Hubs;
using KnightsRPGGame.Service.GameAPI.Hubs.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.GameComponents;

public class FrameStreamer
{
    private const float BotSpeed = 15f;
    private const float BulletHitRadius = 20f;
    private readonly IHubContext<GameHub, IGameClient> _hubContext;
    private readonly Random _rand = new();
    private Timer? _timer;
    private bool _isStreaming;

    private readonly ConcurrentDictionary<string, RoomState> _rooms = new();
    private DateTime _lastUpdateTime = DateTime.UtcNow;

    public FrameStreamer(IHubContext<GameHub, IGameClient> hubContext) => _hubContext = hubContext;

    public bool HasPlayer() => _rooms.Values.Any(room => room.Players.Any());

    public bool TryGetPlayerPosition(string connectionId, out PlayerStateDto position)
    {
        foreach (var room in _rooms.Values)
        {
            if (room.Players.TryGetValue(connectionId, out var state))
            {
                position = new PlayerStateDto { X = state.Position.X, Y = state.Position.Y, Health = state.Health };
                return true;
            }
        }

        position = default!;
        return false;
    }

    public void RegisterPlayer(string connectionId, Vector2 position)
    {
        var roomName = RoomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = GetOrCreateRoom(roomName);
        room.Players[connectionId] = new PlayerState { ConnectionId = connectionId, Position = position };
    }

    public void RemovePlayer(string connectionId)
    {
        var roomName = RoomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null || !_rooms.TryGetValue(roomName, out var room)) return;

        room.Players.Remove(connectionId, out var player);
        room.Actions.Remove(connectionId, out var action);
    }

    public void UpdatePlayerAction(string connectionId, PlayerAction action)
    {
        var roomName = RoomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = GetOrCreateRoom(roomName);
        room.Players.TryAdd(connectionId, new PlayerState { ConnectionId = connectionId });

        var actions = room.Actions.GetOrAdd(connectionId, _ => new HashSet<PlayerAction>());
        lock (actions)
        {
            if (action.ToString().StartsWith("Stop"))
            {
                var moveAction = (PlayerAction)Enum.Parse(typeof(PlayerAction), action.ToString().Replace("Stop", ""));
                actions.Remove(moveAction);
            }
            else actions.Add(action);
        }
    }

    public async Task StartStreaming(string connectionId)
    {
        if (_isStreaming) return;
        _isStreaming = true;
        _timer = new Timer(async _ => await UpdateFrame(), null, 0, 15);
    }

    public void StopStreaming()
    {
        _timer?.Dispose();
        _timer = null;
        _isStreaming = false;
    }

    private async Task UpdateFrame()
    {
        foreach (var (roomName, room) in _rooms)
        {
            var currentTime = DateTime.UtcNow;
            var deltaSeconds = (float)(currentTime - _lastUpdateTime).TotalSeconds;

            foreach (var (connectionId, playerState) in room.Players)
            {
                UpdatePlayerMovement(room, connectionId);

                await _hubContext.Clients.Group(roomName).ReceivePlayerPosition(connectionId, new PlayerStateDto
                {
                    X = playerState.Position.X,
                    Y = playerState.Position.Y,
                    Health = playerState.Health
                });
            }

            await UpdateBullets(0.015f, room, roomName);
            await UpdateEnemyBullets(1f, room, roomName);
            await BotsShootAtPlayers(room, roomName);
            UpdateEnemyBotPositions(room, roomName);
            await BroadcastEnemyBots(room, roomName);

            room.Score += deltaSeconds;

            await _hubContext.Clients.Group(roomName).UpdateScore(room.Score);
        }

        _lastUpdateTime = DateTime.UtcNow;
    }

    private void UpdatePlayerMovement(RoomState room, string connectionId)
    {
        if (!room.Actions.TryGetValue(connectionId, out var actions)) return;

        var moveVector = Vector2.Zero;
        lock (actions)
        {
            foreach (var action in actions)
            {
                moveVector += action switch
                {
                    PlayerAction.MoveUp => new Vector2(0, -1),
                    PlayerAction.MoveDown => new Vector2(0, 1),
                    PlayerAction.MoveLeft => new Vector2(-1, 0),
                    PlayerAction.MoveRight => new Vector2(1, 0),
                    _ => Vector2.Zero
                };
            }
        }

        if (moveVector == Vector2.Zero)
            return;

        moveVector = Vector2.Normalize(moveVector);

        var deltaTime = (float)(DateTime.UtcNow - _lastUpdateTime).TotalSeconds;
        var speed = 200f; // скорость движения игрока

        room.Players[connectionId].Position += moveVector * speed * deltaTime;
    }

    private async Task UpdateBullets(float deltaTime, RoomState room, string roomName)
    {
        foreach (var bullet in room.PlayerBullets.Values.ToList())
        {
            bullet.X += bullet.VelocityX * deltaTime;
            bullet.Y += bullet.VelocityY * deltaTime;

            foreach (var (botId, bot) in room.Bots)
            {
                if (!IsHit(bot.Position, bullet.X, bullet.Y, BulletHitRadius)) continue;

                bot.Health -= 20;
                await _hubContext.Clients.Group(roomName).ReceiveBotHit(botId, bot.Health);

                if (bot.Health <= 0)
                {
                    room.Bots.TryRemove(botId, out _);
                    await _hubContext.Clients.Group(roomName).BotDied(botId);

                    room.Score += 10;
                    await _hubContext.Clients.Group(roomName).UpdateScore(room.Score);
                }

                room.PlayerBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveBullet(bullet.Id);
                break;
            }

            if (!room.PlayerBullets.ContainsKey(bullet.Id)) continue;

            if (IsOutOfBounds(bullet.X, bullet.Y))
            {
                room.PlayerBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveBullet(bullet.Id);
            }
            else await _hubContext.Clients.Group(roomName).UpdateBullet(bullet);
        }
    }

    private async Task UpdateEnemyBullets(float deltaTime, RoomState room, string roomName)
    {
        foreach (var bullet in room.BotBullets.Values.ToList())
        {
            bullet.X += bullet.VelocityX * deltaTime;
            bullet.Y += bullet.VelocityY * deltaTime;

            foreach (var (playerId, player) in room.Players)
            {
                if (!IsHit(player.Position, bullet.X, bullet.Y, BulletHitRadius)) continue;

                player.Health -= 1;
                await _hubContext.Clients.Group(roomName).PlayerHit(playerId, player.Health);
                if (player.Health <= 0)
                    await _hubContext.Clients.Group(roomName).PlayerDied(playerId);

                room.BotBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveEnemyBullet(bullet.Id);
                break;
            }

            if (!room.BotBullets.ContainsKey(bullet.Id)) continue;

            if (IsOutOfBounds(bullet.X, bullet.Y))
            {
                room.BotBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveEnemyBullet(bullet.Id);
            }
            else await _hubContext.Clients.Group(roomName).UpdateEnemyBullet(bullet);
        }
    }

    private async Task BotsShootAtPlayers(RoomState room, string roomName)
    {
        foreach (var bot in room.Bots.Values)
        {
            if (_rand.NextDouble() > 0.01) continue;

            var target = room.Players.Values.OrderBy(p => Vector2.Distance(p.Position, bot.Position)).FirstOrDefault();
            if (target == null) continue;

            var dir = Vector2.Normalize(target.Position - bot.Position) * 5f;

            var bullet = new EnemyBulletDto
            {
                ShooterBotId = bot.BotId,
                X = bot.Position.X,
                Y = bot.Position.Y,
                VelocityX = dir.X,
                VelocityY = dir.Y
            };

            room.BotBullets[bullet.Id] = bullet;
            await _hubContext.Clients.Group(roomName).SpawnEnemyBullet(bullet);
        }
    }

    private void UpdateEnemyBotPositions(RoomState room, string roomName)
    {
        var delta = (float)(DateTime.UtcNow - _lastUpdateTime).TotalSeconds;

        var toRemove = room.Bots.Values
            .Where(bot => (bot.Position += new Vector2(0, BotSpeed * delta)).Y > 1080)
            .Select(bot => bot.BotId)
            .ToList();

        foreach (var id in toRemove)
        {
            room.Bots.TryRemove(id, out _);
            _hubContext.Clients.Group(roomName).BotDied(id);
        }
    }

    private async Task BroadcastEnemyBots(RoomState room, string roomName)
    {
        foreach (var (id, bot) in room.Bots)
        {
            await _hubContext.Clients.Group(roomName).ReceiveBotPosition(id, new PlayerStateDto
            {
                X = bot.Position.X,
                Y = bot.Position.Y,
                Health = bot.Health
            });
        }
    }

    public async Task ProcessShot(string connectionId, BulletDto bullet)
    {
        var roomName = RoomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = GetOrCreateRoom(roomName);
        room.PlayerBullets[bullet.Id] = bullet;
    }

    public void AddEnemyBot(string botId, Vector2 position, string roomName)
    {
        if (!_rooms.TryGetValue(roomName, out var room)) return;

        var bot = new EnemyBot { BotId = botId, Position = position };
        room.Bots[botId] = bot;
    }

    private RoomState GetOrCreateRoom(string roomName)
    {
        if (!_rooms.TryGetValue(roomName, out var room))
        {
            room = new RoomState();
            _rooms[roomName] = room;
        }
        return room;
    }

    private static bool IsHit(Vector2 target, float x, float y, float radius)
    {
        var dx = target.X - x;
        var dy = target.Y - y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool IsOutOfBounds(float x, float y) => x < 0 || x > 1920 || y < 0 || y > 1080;

    public class RoomState
    {
        public ConcurrentDictionary<string, PlayerState> Players { get; } = new();
        public ConcurrentDictionary<string, EnemyBot> Bots { get; } = new();
        public ConcurrentDictionary<string, HashSet<PlayerAction>> Actions { get; } = new();
        public Dictionary<string, BulletDto> PlayerBullets { get; } = new();
        public Dictionary<string, EnemyBulletDto> BotBullets { get; } = new();
        public float Score { get; set; } = 0;
    }
}
