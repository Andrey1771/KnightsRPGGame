using System.Collections.Concurrent;

namespace KnightsRPGGame.Service.GameAPI.GameComponents
{
    public class GameRoom
    {
        public string RoomName { get; set; }
        public List<string> Players { get; set; } = new List<string>();
        public int MaxPlayers { get; set; } = 4;

        public bool IsFull => Players.Count >= MaxPlayers;
    }

    public static class RoomManager
    {
        private static ConcurrentDictionary<string, GameRoom> rooms = new ConcurrentDictionary<string, GameRoom>();

        public static bool CreateRoom(string roomName, int maxPlayers = 4)
        {
            return rooms.TryAdd(roomName, new GameRoom
            {
                RoomName = roomName,
                MaxPlayers = maxPlayers
            });
        }

        public static bool AddPlayerToRoom(string roomName, string connectionId)
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

        public static void RemovePlayerFromRoom(string roomName, string connectionId)
        {
            if (rooms.TryGetValue(roomName, out var room))
            {
                room.Players.Remove(connectionId);
                if (room.Players.Count == 0)
                {
                    rooms.TryRemove(roomName, out _);
                }
            }
        }

        public static IEnumerable<GameRoom> GetAllRooms()
        {
            return rooms.Values;
        }
    }
}
