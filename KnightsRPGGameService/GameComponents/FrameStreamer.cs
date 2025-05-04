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

    private readonly ConcurrentDictionary<string, PlayerState> _playerStates = new();
    private readonly ConcurrentDictionary<string, HashSet<PlayerAction>> _activeActions = new();
    private readonly ConcurrentDictionary<string, EnemyBot> _enemyBots = new();
    private readonly Dictionary<string, BulletDto> _activeBullets = new();
    private readonly Dictionary<string, EnemyBulletDto> _enemyBullets = new();

    private readonly Random _rand = new();
    private DateTime _lastUpdateTime = DateTime.UtcNow;

    private Timer? _timer;
    private bool _isStreaming;

    public FrameStreamer(IHubContext<GameHub, IGameClient> hubContext) => _hubContext = hubContext;

    public bool HasPlayer()
    {
        return _playerStates.Any();
    }

    public bool TryGetPlayerPosition(string connectionId, out PlayerStateDto position)
    {
        if (_playerStates.TryGetValue(connectionId, out var state))
        {
            position = new PlayerStateDto
            {
                X = state.Position.X,
                Y = state.Position.Y,
                Health = state.Health,
            };
            return true;
        }

        position = default!;
        return false;
    }

    public void RegisterPlayer(string connectionId, Vector2 position)
    {
        _playerStates[connectionId] = new PlayerState
        {
            ConnectionId = connectionId,
            Position = position
        };
    }

    public void RemovePlayer(string connectionId)
    {
        _playerStates.TryRemove(connectionId, out _);
        _activeActions.TryRemove(connectionId, out _);
    }

    public void UpdatePlayerAction(string connectionId, PlayerAction action)
    {
        _playerStates.TryAdd(connectionId, new PlayerState { ConnectionId = connectionId });
        var actions = _activeActions.GetOrAdd(connectionId, _ => new HashSet<PlayerAction>());

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
        foreach (var (connectionId, state) in _playerStates)
        {
            UpdatePlayerMovement(connectionId);

            var room = RoomManager.GetRoomNameByConnection(connectionId);
            if (room == null) continue;

            await _hubContext.Clients.Group(room).ReceivePlayerPosition(connectionId, new PlayerStateDto
            {
                X = state.Position.X,
                Y = state.Position.Y,
                Health = state.Health
            });

            await UpdateBullets(0.015f, room);
            await UpdateEnemyBullets(1f, room);
            await BotsShootAtPlayers(room);
            UpdateEnemyBotPositions(room);
            await BroadcastEnemyBots(room);
        }
    }

    private void UpdatePlayerMovement(string connectionId)
    {
        if (!_activeActions.TryGetValue(connectionId, out var actions)) return;

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
        _playerStates[connectionId].Position += moveVector;
    }

    private async Task BroadcastEnemyBots(string room)
    {
        foreach (var (id, bot) in _enemyBots)
        {
            await _hubContext.Clients.Group(room).ReceiveBotPosition(id, new PlayerStateDto
            {
                X = bot.Position.X,
                Y = bot.Position.Y,
                Health = bot.Health
            });
        }
    }

    private void UpdateEnemyBotPositions(string room)
    {
        var delta = (float)(DateTime.UtcNow - _lastUpdateTime).TotalSeconds;
        _lastUpdateTime = DateTime.UtcNow;

        var toRemove = _enemyBots.Values
            .Where(bot => (bot.Position += new Vector2(0, BotSpeed * delta)).Y > 1080)
            .Select(bot => bot.BotId)
            .ToList();

        foreach (var id in toRemove)
        {
            _enemyBots.TryRemove(id, out _);
            _hubContext.Clients.Group(room).BotDied(id);
        }
    }

    private async Task UpdateEnemyBullets(float deltaTime, string room)
    {
        foreach (var bullet in _enemyBullets.Values.ToList())
        {
            bullet.X += bullet.VelocityX * deltaTime;
            bullet.Y += bullet.VelocityY * deltaTime;

            foreach (var (id, player) in _playerStates)
            {
                if (!IsHit(player.Position, bullet.X, bullet.Y, BulletHitRadius)) continue;

                player.Health -= 1;
                await _hubContext.Clients.Group(room).PlayerHit(id, player.Health);
                if (player.Health <= 0)
                    await _hubContext.Clients.Group(room).PlayerDied(id);

                _enemyBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(room).RemoveEnemyBullet(bullet.Id);
                break;
            }

            if (!_enemyBullets.ContainsKey(bullet.Id)) continue;

            if (IsOutOfBounds(bullet.X, bullet.Y))
            {
                _enemyBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(room).RemoveEnemyBullet(bullet.Id);
            }
            else await _hubContext.Clients.Group(room).UpdateEnemyBullet(bullet);
        }
    }

    private async Task UpdateBullets(float deltaTime, string room)
    {
        foreach (var bullet in _activeBullets.Values.ToList())
        {
            bullet.X += bullet.VelocityX * deltaTime;
            bullet.Y += bullet.VelocityY * deltaTime;

            foreach (var (botId, bot) in _enemyBots)
            {
                if (!IsHit(bot.Position, bullet.X, bullet.Y, BulletHitRadius)) continue;

                bot.Health -= 20;
                await _hubContext.Clients.Group(room).ReceiveBotHit(botId, bot.Health);

                if (bot.Health <= 0)
                {
                    _enemyBots.TryRemove(botId, out _);
                    await _hubContext.Clients.Group(room).BotDied(botId);
                }

                _activeBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(room).RemoveBullet(bullet.Id);
                break;
            }

            if (!_activeBullets.ContainsKey(bullet.Id)) continue;

            if (IsOutOfBounds(bullet.X, bullet.Y))
            {
                _activeBullets.Remove(bullet.Id);
                await _hubContext.Clients.Group(room).RemoveBullet(bullet.Id);
            }
            else await _hubContext.Clients.Group(room).UpdateBullet(bullet);
        }
    }

    private async Task BotsShootAtPlayers(string room)
    {
        foreach (var bot in _enemyBots.Values)
        {
            if (_rand.NextDouble() > 0.01) continue;

            var target = _playerStates.Values.OrderBy(p => Vector2.Distance(p.Position, bot.Position)).FirstOrDefault();
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

            _enemyBullets[bullet.Id] = bullet;
            await _hubContext.Clients.Group(room).SpawnEnemyBullet(bullet);
        }
    }

    public async Task ProcessShot(string connectionId, BulletDto bullet) => _activeBullets[bullet.Id] = bullet;

    public void AddEnemyBot(string botId, Vector2 position)
    {
        var bot = new EnemyBot { BotId = botId, Position = position };
        _enemyBots[bot.BotId] = bot;
    }

    public void AddEnemyBot(Vector2 position)
    {
        var bot = new EnemyBot { Position = position };
        _enemyBots[bot.BotId] = bot;
    }

    private static bool IsHit(Vector2 target, float x, float y, float radius)
    {
        var dx = target.X - x;
        var dy = target.Y - y;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static bool IsOutOfBounds(float x, float y) => x < 0 || x > 1920 || y < 0 || y > 1080;
}