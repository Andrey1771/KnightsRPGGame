using KnightsRPGGame.Service.GameAPI.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Xml.Linq;

namespace KnightsRPGGame.Service.GameAPI.GameComponents
{
    public class GameManager
    {
        private readonly IHubContext<GameHub> _hubContext;

        private readonly SemaphoreSlim _gameLock = new SemaphoreSlim(1, 1);

        private Dictionary<string, Game> _gameInRoom = new ();

        private int _width = 1910;
        private int _height = 800;

        public GameManager(IHubContext<GameHub> gameHub)
        {
            _hubContext = gameHub;
        }

        public void CreateRoom(string roomName)
        {
            if (_gameInRoom.ContainsKey(roomName))
            {
                throw new ArgumentException("Комната с таким именем уже создана");
            }

            _gameInRoom.Add(roomName, default);
        }

        public void InitializeGameInRoom(string roomName)
        {
            if (!_gameInRoom.ContainsKey(roomName))
            {
                throw new ArgumentException("Комната с таким именем не существует");
            }

            _gameInRoom[roomName] = InitializeNewGame(roomName);
        }

        private Game InitializeNewGame(string roomName)
        {
            var game = new Game(_width, _height, roomName);
            game.State = GameState.Running;
            return game;
        }

        public void AddPlayer(string connectionId, string name, string roomName)
        {
            try
            {
                Random rand = new Random();
                var x = rand.Next(50, _width - 50);
                var y = rand.Next(50, _height - 50);

                GetGame(roomName).AddPlayer(connectionId, name, x, y); //TODO
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }

        public async Task RemovePlayer(string connectionId, string roomName)
        {
            await _gameLock.WaitAsync();
            try
            {
                GetGame(roomName).Players.Remove(connectionId); //TODO
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
            finally
            {
                _gameLock.Release();
            }
        }

        public Game GetGame(string roomName)
        {
            if (!_gameInRoom.TryGetValue(roomName, out Game game))
            {
                throw new ArgumentException("Комната с таким именем не существует");
            }

            return game;
        }

        public Player GetPlayer(string roomName, string contextId)
        {
            Player player = null;
            GetGame(roomName).Players.TryGetValue(contextId, out player);
            return player;
        }
    }
}
