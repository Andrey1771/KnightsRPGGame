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
        Attack
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
        private readonly ConcurrentDictionary<string, PlayerState> _playerStates = new();

        public FrameStreamer(IHubContext<GameHub, IGameClient> hubContext)
        {
            _hubContext = hubContext;
        }

        public void UpdatePlayerAction(string connectionId, PlayerAction action)
        {
            if (!_playerStates.TryGetValue(connectionId, out var playerState))
            {
                playerState = new PlayerState
                {
                    ConnectionId = connectionId,
                    Position = new Vector2(0, 0),
                    Health = 100
                };
                _playerStates[connectionId] = playerState;
            }

            // Обновляем позицию
            var position = playerState.Position;
            switch (action)
            {
                case PlayerAction.MoveUp:
                    position.Y -= 1;
                    break;
                case PlayerAction.MoveDown:
                    position.Y += 1;
                    break;
                case PlayerAction.MoveLeft:
                    position.X -= 1;
                    break;
                case PlayerAction.MoveRight:
                    position.X += 1;
                    break;
            }
            playerState.Position = position;

            // Получаем комнату игрока
            var roomName = RoomManager.GetRoomNameByConnection(connectionId);
            if (roomName != null)
            {
                _hubContext.Clients.Group(roomName).ReceivePlayerPosition(connectionId, new PlayerPositionDto
                {
                    X = position.X,
                    Y = position.Y
                });
            }
        }

        public void RemovePlayer(string connectionId)
        {
            _playerStates.TryRemove(connectionId, out _);
        }
    }
}
