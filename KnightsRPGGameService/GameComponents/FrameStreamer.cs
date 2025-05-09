using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using KnightsRPGGame.Service.GameAPI.Hubs;
using KnightsRPGGame.Service.GameAPI.Hubs.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.GameComponents;

public class FrameStreamer
{
    private const float BotSpeed = 15f;
    private const float BulletHitRadius = 20f;

    private readonly IHubContext<GameHub, IGameClient> _hubContext;
    private readonly RoomManager _roomManager;
    private readonly Random _rand = new();

    private Timer? _timer;
    private bool _isStreaming;
    private DateTime _lastUpdateTime = DateTime.UtcNow;

    public FrameStreamer(IHubContext<GameHub, IGameClient> hubContext, RoomManager roomManager)
    {
        _hubContext = hubContext;
        _roomManager = roomManager;
    }

    public bool HasPlayer() =>
        _roomManager.GetAllRooms().Any(room => room.State.Players.Any());

    public bool TryGetPlayerPosition(string connectionId, out PlayerStateDto position)
    {
        foreach (var room in _roomManager.GetAllRooms())
        {
            if (room.State.Players.TryGetValue(connectionId, out var state))
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
        var roomName = _roomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = _roomManager.GetRoom(roomName);
        room?.State.Players.TryAdd(connectionId, new PlayerState { ConnectionId = connectionId, Position = position });
    }

    public void RemovePlayer(string connectionId)
    {
        var roomName = _roomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = _roomManager.GetRoom(roomName);
        if (room == null) return;

        room.State.Players.Remove(connectionId, out _);
        room.State.Actions.Remove(connectionId, out _);
    }

    public void UpdatePlayerAction(string connectionId, PlayerAction action)
    {
        var roomName = _roomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = _roomManager.GetRoom(roomName);
        if (room == null) return;

        room.State.Players.TryAdd(connectionId, new PlayerState { ConnectionId = connectionId });

        var actions = room.State.Actions.GetOrAdd(connectionId, _ => new HashSet<PlayerAction>());
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
        _lastUpdateTime = DateTime.UtcNow;
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
        foreach (var room in _roomManager.GetAllRooms())
        {
            var roomName = room.RoomName;
            var state = room.State;

            var currentTime = DateTime.UtcNow;
            var deltaSeconds = (float)(currentTime - _lastUpdateTime).TotalSeconds;

            foreach (var (connectionId, playerState) in state.Players)
            {
                UpdatePlayerMovement(state, connectionId);

                await _hubContext.Clients.Group(roomName).ReceivePlayerPosition(connectionId, new PlayerStateDto
                {
                    X = playerState.Position.X,
                    Y = playerState.Position.Y,
                    Health = playerState.Health
                });
            }

            await UpdateBullets(0.015f, state, roomName);
            await UpdateEnemyBullets(1f, state, roomName);
            await BotsShootAtPlayers(state, roomName);
            UpdateEnemyBotPositions(state, roomName);
            await BroadcastEnemyBots(state, roomName);

            state.Score += deltaSeconds;
            await _hubContext.Clients.Group(roomName).UpdateScore(state.Score);
        }

        _lastUpdateTime = DateTime.UtcNow;
    }

    private void UpdatePlayerMovement(GameRoom.RoomState state, string connectionId)
    {
        if (!state.Actions.TryGetValue(connectionId, out var actions)) return;

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

        if (moveVector == Vector2.Zero) return;

        moveVector = Vector2.Normalize(moveVector);
        var deltaTime = (float)(DateTime.UtcNow - _lastUpdateTime).TotalSeconds;
        var speed = 200f;
        state.Players[connectionId].Position += moveVector * speed * deltaTime;
    }

    private async Task UpdateBullets(float deltaTime, GameRoom.RoomState state, string roomName)
    {
        foreach (var bullet in state.PlayerBullets.Values.ToList())
        {
            bullet.X += bullet.VelocityX * deltaTime;
            bullet.Y += bullet.VelocityY * deltaTime;

            foreach (var (botId, bot) in state.Bots)
            {
                if (!IsHit(bot.Position, bullet.X, bullet.Y, BulletHitRadius)) continue;

                bot.Health -= 20;
                await _hubContext.Clients.Group(roomName).ReceiveBotHit(botId, bot.Health);

                if (bot.Health <= 0)
                {
                    state.Bots.TryRemove(botId, out _);
                    await _hubContext.Clients.Group(roomName).BotDied(botId);

                    state.Score += 10;
                    await _hubContext.Clients.Group(roomName).UpdateScore(state.Score);
                }

                state.PlayerBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveBullet(bullet.Id);
                break;
            }

            if (!state.PlayerBullets.ContainsKey(bullet.Id)) continue;

            if (IsOutOfBounds(bullet.X, bullet.Y))
            {
                state.PlayerBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveBullet(bullet.Id);
            }
            else await _hubContext.Clients.Group(roomName).UpdateBullet(bullet);
        }
    }

    private async Task UpdateEnemyBullets(float deltaTime, GameRoom.RoomState state, string roomName)
    {
        foreach (var bullet in state.BotBullets.Values.ToList())
        {
            bullet.X += bullet.VelocityX * deltaTime;
            bullet.Y += bullet.VelocityY * deltaTime;

            foreach (var (playerId, player) in state.Players)
            {
                if (!IsHit(player.Position, bullet.X, bullet.Y, BulletHitRadius)) continue;

                player.Health -= 1;
                await _hubContext.Clients.Group(roomName).PlayerHit(playerId, player.Health);
                if (player.Health <= 0)
                    await _hubContext.Clients.Group(roomName).PlayerDied(playerId);

                state.BotBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveEnemyBullet(bullet.Id);
                break;
            }

            if (!state.BotBullets.ContainsKey(bullet.Id)) continue;

            if (IsOutOfBounds(bullet.X, bullet.Y))
            {
                state.BotBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(roomName).RemoveEnemyBullet(bullet.Id);
            }
            else await _hubContext.Clients.Group(roomName).UpdateEnemyBullet(bullet);
        }
    }

    private async Task BotsShootAtPlayers(GameRoom.RoomState state, string roomName)
    {
        foreach (var bot in state.Bots.Values)
        {
            if (_rand.NextDouble() > 0.01) continue;

            var target = state.Players.Values.OrderBy(p => Vector2.Distance(p.Position, bot.Position)).FirstOrDefault();
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

            state.BotBullets[bullet.Id] = bullet;
            await _hubContext.Clients.Group(roomName).SpawnEnemyBullet(bullet);
        }
    }

    private void UpdateEnemyBotPositions(GameRoom.RoomState state, string roomName)
    {
        var delta = (float)(DateTime.UtcNow - _lastUpdateTime).TotalSeconds;

        var toRemove = state.Bots.Values
            .Where(bot => (bot.Position += new Vector2(0, BotSpeed * delta)).Y > 1080)
            .Select(bot => bot.BotId)
            .ToList();

        foreach (var id in toRemove)
        {
            state.Bots.TryRemove(id, out _);
            _hubContext.Clients.Group(roomName).BotDied(id);
        }
    }

    private async Task BroadcastEnemyBots(GameRoom.RoomState state, string roomName)
    {
        foreach (var (id, bot) in state.Bots)
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
        var roomName = _roomManager.GetRoomNameByConnection(connectionId);
        if (roomName == null) return;

        var room = _roomManager.GetRoom(roomName);
        room?.State.PlayerBullets.TryAdd(bullet.Id, bullet);
    }

    public void AddEnemyBot(string botId, Vector2 position, string roomName)
    {
        var room = _roomManager.GetRoom(roomName);
        if (room == null) return;

        var bot = new EnemyBot { BotId = botId, Position = position };
        room.State.Bots[botId] = bot;
    }

    private static bool IsHit(Vector2 target, float x, float y, float radius)
    {
        var dx = target.X - x;
        var dy = target.Y - y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool IsOutOfBounds(float x, float y) => x < 0 || x > 1920 || y < 0 || y > 1080;
}
