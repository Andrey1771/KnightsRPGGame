using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using System.Collections.Concurrent;

namespace KnightsRPGGame.Service.GameAPI.GameComponents
{
    public class GameRoom
    {
        public string RoomName { get; set; }
        public List<string> Players { get; set; } = new List<string>();
        public int MaxPlayers { get; set; } = 4;
        public bool IsFull => Players.Count >= MaxPlayers;
        public string LeaderConnectionId { get; set; }

        public RoomState State { get; set; } = new RoomState();

        public class RoomState
        {
            public ConcurrentDictionary<string, PlayerState> Players { get; } = new();
            public ConcurrentDictionary<string, EnemyBot> Bots { get; } = new();
            public ConcurrentDictionary<string, HashSet<PlayerAction>> Actions { get; } = new();
            public Dictionary<string, BulletDto> PlayerBullets { get; } = new();
            public Dictionary<string, EnemyBulletDto> BotBullets { get; } = new();
            public float Score { get; set; } = 0;
            public bool IsGameStarted { get; set; } = false;
            public bool IsGameOver { get; set; } = false;

            public readonly Dictionary<string, CancellationTokenSource> BotSpawners = new();//TODO
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

        public bool AddPlayerToRoom(string roomName, string connectionId, bool isLeader = false)
        {
            if (rooms.TryGetValue(roomName, out var room))
            {
                if (!room.IsFull)
                {
                    room.Players.Add(connectionId);
                    return true;
                }
            }
            return false;
        }

        public void RemovePlayerFromRoom(string roomName, string connectionId)
        {
            if (rooms.TryGetValue(roomName, out var room))
            {
                room.Players.Remove(connectionId);
                if (room.LeaderConnectionId == connectionId)
                {
                    var playerConnectionId = room.Players.FirstOrDefault();
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

        public List<string> GetPlayersInRoom(string roomName)
        {
            if (rooms.TryGetValue(roomName, out var room))
            {
                return room.Players.ToList();
            }
            return new List<string>();
        }

        public IEnumerable<GameRoom> GetAllRooms()
        {
            return rooms.Values;
        }
        
        public string? GetRoomNameByConnection(string connectionId)
        {
            foreach (var room in rooms)
            {
                if (room.Value.Players.Contains(connectionId))
                {
                    return room.Key;
                }
            }
            return null;
        }
    }
}
