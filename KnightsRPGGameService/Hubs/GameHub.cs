using KnightsRPGGame.Service.GameAPI.GameComponents;
using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using KnightsRPGGame.Service.GameAPI.Hubs.Interfaces;
using Microsoft.AspNetCore.SignalR;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.Hubs
{
    public class GameHub : Hub<IGameClient>
    {
        private static GameManager _game;
        private FrameStreamer _frameStreamer;

        private static readonly Dictionary<string, DateTime> _lastShotTime = new();
        private static readonly TimeSpan ShotCooldown = TimeSpan.FromMilliseconds(500); // 0.5 секунды


        private readonly Dictionary<string, CancellationTokenSource> _botSpawners = new();
        private readonly object _botSpawnerLock = new();


        public GameHub(GameManager game, FrameStreamer frameStreamer)
        {
            _game = game;
            _frameStreamer = frameStreamer;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
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
                            await Task.Delay(TimeSpan.FromSeconds(5), token); // Период спавна

                            var botId = Guid.NewGuid().ToString();
                            float x = random.Next(50, 750); // Ширина карты (800) - допустимая зона
                            float y = 0; // Верх карты

                            var botPos = new Vector2(x, y);
                            _frameStreamer.AddEnemyBot(botId, botPos);

                            await Clients.Group(roomName).ReceiveBotList(new Dictionary<string, PlayerStateDto>
                    {
                        { botId, new PlayerStateDto { X = x, Y = y } }
                    });
                        }
                        catch (TaskCanceledException)
                        {
                            // expected on stop
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"[BotSpawner Error]: {ex.Message}");
                        }
                    }

                }, token);
            }
        }

        public async Task StartGame(string roomName)
        {
            var players = RoomManager.GetPlayersInRoom(roomName);

            var playerPositions = new Dictionary<string, PlayerStateDto>();
            var botPositions = new Dictionary<string, PlayerStateDto>();

            foreach (var connectionId in players)
            {
                var playerPos = new Vector2(0, 0);
                _frameStreamer.RegisterPlayer(connectionId, playerPos);

                playerPositions[connectionId] = new PlayerStateDto
                {
                    X = playerPos.X,
                    Y = playerPos.Y
                };
            }

            // Создание бота (тест) TODO удалить
            var bot1Id = Guid.NewGuid().ToString();
            var bot1Pos = new Vector2(200, 100);
            _frameStreamer.AddEnemyBot(bot1Id, bot1Pos);

            botPositions[bot1Id] = new PlayerStateDto
            {
                X = bot1Pos.X,
                Y = bot1Pos.Y
            };

            // Отправляем старт игры с игроками и ботами
            await Clients.Group(roomName).GameStarted(playerPositions, botPositions);

            // Запуск стриминга
            foreach (var connectionId in players)
            {
                _frameStreamer.StartStreaming(connectionId);
            }

            StartBotSpawningLoop(roomName);
        }

        public async Task Shoot()
        {
            var connectionId = Context.ConnectionId;
            var now = DateTime.UtcNow;

            if (_lastShotTime.TryGetValue(connectionId, out var lastShot))
            {
                if (now - lastShot < ShotCooldown)
                {
                    // Игнорируем выстрел — слишком рано
                    return;
                }
            }

            _lastShotTime[connectionId] = now;

            if (_frameStreamer.TryGetPlayerPosition(connectionId, out var position))
            {
                await Clients.All.BulletFired(connectionId, new PlayerStateDto
                {
                    X = position.X,
                    Y = position.Y
                });

                var bullet = new BulletDto
                {
                    OwnerId = connectionId,
                    X = position.X,
                    Y = position.Y,
                    VelocityX = 0, // вверх
                    VelocityY = -300
                };

                await _frameStreamer.ProcessShot(connectionId, bullet);

                await Clients.All.SpawnBullet(bullet);
            }
        }

        public async Task StopGame(string roomName)
        {
            _frameStreamer.StopStreaming();
            StopBotSpawningLoop(roomName);
        }

        public Task<string> GetConnectionId()
        {
            return Task.FromResult(Context.ConnectionId);
        }

        public async Task PerformAction(string action)
        {
            if (Enum.TryParse<PlayerAction>(action, out var parsedAction))
            {
                _frameStreamer.UpdatePlayerAction(Context.ConnectionId, parsedAction);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            _frameStreamer.RemovePlayer(Context.ConnectionId);
            if (!_frameStreamer.HasPlayer())
            {
                _frameStreamer.StopStreaming();
            }
            RoomManager.RemovePlayerFromAllRooms(Context.ConnectionId);
            // Сообщаем, что игрок вышел
            await Clients.All.PlayerLeft(Context.ConnectionId); //TODO ALL?

            await base.OnDisconnectedAsync(exception);
        }

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

                // Отправляем событие, что игрок присоединился
                await Clients.Group(roomName).PlayerJoined(Context.ConnectionId);

                // Отправляем всем новый список игроков
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

            // Сообщаем, что игрок вышел
            await Clients.Group(roomName).PlayerLeft(Context.ConnectionId);

            // Отправляем новый список игроков
            var players = RoomManager.GetPlayersInRoom(roomName);
            await Clients.Group(roomName).ReceivePlayerList(players);
        }

        public async Task RequestPlayerList(string roomName)
        {
            var players = RoomManager.GetPlayersInRoom(roomName);

            await Clients.Caller.ReceivePlayerList(players);
        }
    }
}
