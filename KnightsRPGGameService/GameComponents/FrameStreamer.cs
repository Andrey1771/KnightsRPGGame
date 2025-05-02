using KnightsRPGGame.Service.GameAPI.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.GameComponents
{
    public enum PlayerAction
    {
        MoveUp,
        MoveDown,
        MoveLeft,
        MoveRight,
        StopMoveUp,
        StopMoveDown,
        StopMoveLeft,
        StopMoveRight
    }

    public class PlayerState
    {
        public string ConnectionId { get; set; } // Идентификатор подключения игрока
        public Vector2 Position { get; set; }    // Текущая позиция игрока (например, x, y)
        public int Health { get; set; }          // Здоровье игрока (по желанию)
        public int Score { get; set; }           // Очки игрока (по желанию)
    }

    public class PlayerPositionDto
    {
        public float X { get; set; }
        public float Y { get; set; }
    }

    public class FrameStreamer
    {
        private readonly IHubContext<GameHub, IGameClient> _hubContext;

        // Храним состояние игроков
        private readonly ConcurrentDictionary<string, PlayerState> _playerStates = new();

        // Активные действия (нажатые клавиши) по игроку
        private readonly ConcurrentDictionary<string, HashSet<PlayerAction>> _activeActions = new();

        private Timer? _timer;
        private bool _isStreaming = false;

        public FrameStreamer(IHubContext<GameHub, IGameClient> hubContext)
        {
            _hubContext = hubContext;
        }

        public async Task StartStreaming(string connectionId)
        {
            if (_isStreaming) return;

            _isStreaming = true;
            _timer = new Timer(async _ => await StartStreamingAsync(), null, 0, 15);// более 60 фпс

            /*var roomName = RoomManager.GetRoomNameByConnection(connectionId);
            if (roomName != null)
            {
                await _hubContext.Clients.Group(roomName).ReceivePlayerPosition(connectionId, new PlayerPositionDto
                {
                    X = 0,//TODO Инициализация изначальной позиции игрока
                    Y = 0
                });
            }*/

        }

        public void RegisterPlayer(string connectionId, Vector2 position)
        {
            _playerStates[connectionId] = new PlayerState
            {
                ConnectionId = connectionId,
                Position = position,
                Health = 100,
                Score = 0
            };
        }

        public void StopStreaming()
        {
            _timer?.Dispose();
            _timer = null;
            _isStreaming = false;
        }

        public void UpdatePlayerAction(string connectionId, PlayerAction action)
        {
            // Обеспечиваем, что у игрока есть начальное состояние
            var playerState = _playerStates.GetOrAdd(connectionId, _ => new PlayerState
            {
                ConnectionId = connectionId,
                Position = new Vector2(0, 0),
                Health = 100,
                Score = 0
            });

            // Получаем или создаем список активных действий
            var active = _activeActions.GetOrAdd(connectionId, _ => new HashSet<PlayerAction>());

            // Обновляем список активных действий
            switch (action)
            {
                case PlayerAction.MoveUp: active.Add(PlayerAction.MoveUp); break;
                case PlayerAction.MoveDown: active.Add(PlayerAction.MoveDown); break;
                case PlayerAction.MoveLeft: active.Add(PlayerAction.MoveLeft); break;
                case PlayerAction.MoveRight: active.Add(PlayerAction.MoveRight); break;

                case PlayerAction.StopMoveUp: active.Remove(PlayerAction.MoveUp); break;
                case PlayerAction.StopMoveDown: active.Remove(PlayerAction.MoveDown); break;
                case PlayerAction.StopMoveLeft: active.Remove(PlayerAction.MoveLeft); break;
                case PlayerAction.StopMoveRight: active.Remove(PlayerAction.MoveRight); break;
            }
        }

        public async Task StartStreamingAsync()
        {
            foreach (var (connectionId, actions) in _activeActions)
            {
                if (!_playerStates.TryGetValue(connectionId, out var state))
                    continue;

                var moveVector = Vector2.Zero;

                foreach (var action in actions)
                {
                    switch (action)
                    {
                        case PlayerAction.MoveUp: moveVector.Y -= 1; break;
                        case PlayerAction.MoveDown: moveVector.Y += 1; break;
                        case PlayerAction.MoveLeft: moveVector.X -= 1; break;
                        case PlayerAction.MoveRight: moveVector.X += 1; break;
                    }
                }

                state.Position += moveVector;

                var roomName = RoomManager.GetRoomNameByConnection(connectionId);
                if (roomName != null)
                {
                    await _hubContext.Clients.Group(roomName).ReceivePlayerPosition(connectionId, new PlayerPositionDto //рассылаем сразу всей группе?
                    {
                        X = state.Position.X,
                        Y = state.Position.Y
                    });
                }
            }
        }

        public void RemovePlayer(string connectionId)
        {
            _playerStates.TryRemove(connectionId, out _);
            _activeActions.TryRemove(connectionId, out _);
        }

        public bool HasPlayer()
        {
            return _playerStates.Any();
        }
    }
}
