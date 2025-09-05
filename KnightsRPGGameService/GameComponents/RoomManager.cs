using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using System.Collections.Concurrent;

namespace KnightsRPGGame.Service.GameAPI.GameComponents
{
    public class GameRoom
    {
        public string RoomName { get; set; }
        public ConcurrentDictionary<string, PlayerInfo> Players { get; set; } = new(); //TODO Дублирование, необходим рефакторинг
        public int MaxPlayers { get; set; } = 4;
        public bool IsFull => Players.Count >= MaxPlayers;
        public string LeaderConnectionId { get; set; }

        public RoomState State { get; set; } = new RoomState();

        public class RoomState
        {
            public ConcurrentDictionary<string, PlayerState> Players { get; } = new();
            public ConcurrentDictionary<string, EnemyBot> Bots { get; } = new();
            public ConcurrentDictionary<string, HashSet<PlayerAction>> Actions { get; } = new();
            public ConcurrentDictionary<string, BulletDto> PlayerBullets { get; } = new();
            public ConcurrentDictionary<string, EnemyBulletDto> BotBullets { get; } = new();
            public float Score { get; set; } = 0;
            public bool IsGameStarted { get; set; } = false;
            public bool IsGameOver { get; set; } = false;

            public bool IsPaused { get; set; } = false;
            public DateTime LastBotSpawnTime { get; set; }
            public DateTime LastUpdateTime { get; set; }
            public DateTime? PauseStartTime { get; set; } = null;

            public Timer? FrameTimer { get; set; }
            public bool IsStreaming { get; set; } = false;


            public readonly ConcurrentDictionary<string, CancellationTokenSource> BotSpawners = new();//TODO
        }
    }


    public class RoomManager
    {
        private ConcurrentDictionary<string, GameRoom> rooms = new ConcurrentDictionary<string, GameRoom>();

        public GameRoom? GetRoom(string roomName) => rooms.TryGetValue(roomName, out var room) ? room : null;

        public bool CreateRoom(string roomName, string connectionId, int maxPlayers = 4)
        {
            return rooms.TryAdd(roomName, new GameRoom
            {
                RoomName = roomName,
                MaxPlayers = maxPlayers,
                LeaderConnectionId = connectionId

            });
        }

        public bool AddPlayerToRoom(string roomName, string connectionId, string playerName)
        {
            if (rooms.TryGetValue(roomName, out var room))
            {
                if (!room.IsFull)
                {
                    return room.Players.TryAdd(connectionId, new PlayerInfo
                    {
                        Name = playerName
                    });
                }
            }
            return false;
        }

        public void RemovePlayerFromRoom(string roomName, string connectionId)
        {
            if (rooms.TryGetValue(roomName, out var room))
            {
                room.Players.TryRemove(connectionId, out _);
                if (room.LeaderConnectionId == connectionId)
                {
                    var playerConnectionId = room.Players.Keys.FirstOrDefault();
                    if (playerConnectionId != null) // Если игроков не осталось, то комната будет удалена сразу
                    {
                        room.LeaderConnectionId = playerConnectionId;
                    }
                }
            }
        }

        public void RemoveRoom(string roomName)
        {
            rooms.TryRemove(roomName, out _);
        }

        public List<PlayerInfo> GetPlayersInRoom(string roomName)
        {
            if (rooms.TryGetValue(roomName, out var room))
            {
                return room.Players.Select(p => new PlayerInfo
                {
                    ConnectionId = p.Key,
                    Name = p.Value.Name
                }).ToList();
            }
            return new List<PlayerInfo>();
        }

        public IEnumerable<GameRoom> GetAllRooms()
        {
            return rooms.Values;
        }
        
        public string? GetRoomNameByConnection(string connectionId)
        {
            foreach (var room in rooms)
            {
                if (room.Value.Players.ContainsKey(connectionId))
                {
                    return room.Key;
                }
            }
            return null;
        }
    }
}
