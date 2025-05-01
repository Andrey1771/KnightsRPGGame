using KnightsRPGGame.Service.GameAPI.GameComponents;
using Microsoft.AspNetCore.SignalR;
using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.Hubs
{
    public interface IGameClient
    {
        Task RoomCreated(string roomName);
        Task Error(string message);
        Task PlayerJoined(string connectionId);
        Task PlayerLeft(string connectionId);
        Task ReceivePlayerList(List<string> connectionIds);
        Task GameStarted();
        Task ReceivePlayerPosition(string connectionId, PlayerPositionDto position);
    }

    public class GameHub : Hub<IGameClient>
    {
        private static GameManager _game;
        private FrameStreamer _frameStreamer;

        public GameHub(GameManager game, FrameStreamer frameStreamer)
        {
            _game = game;
            _frameStreamer = frameStreamer;
        }

        /*public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
            _frameStreamer.StartStreamingAsync(); // start the streaming task
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            var player = _game.GetPlayer(Context.ConnectionId);
            
            if (player != null) // LeftRoom()
            {
                var connectionId = Context.ConnectionId;
                await _game.RemovePlayer(Context.ConnectionId);
                await Clients.All.SendAsync("PlayerLeft", connectionId, player.name);
            }

            await base.OnDisconnectedAsync(exception);
        }*/

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public async Task StartGame(string roomName)
        {
            await Clients.Group(roomName).GameStarted();
            _frameStreamer.StartStreaming();
        }

        public async Task StopGame(string roomName)
        {
            _frameStreamer.StopStreaming();
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
            _frameStreamer.StopStreaming();
            _frameStreamer.RemovePlayer(Context.ConnectionId);
            RoomManager.RemovePlayerFromAllRooms(Context.ConnectionId);
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

        /*public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Тут можно дописать логику выхода из всех комнат
            await base.OnDisconnectedAsync(exception);
        }

        public async Task AddPlayer(string name)
        {
            _game.AddPlayer(Context.ConnectionId, name);

            await Clients.All.SendAsync("PlayerJoined", name);
        }

        public async Task RestartGame()
        {
            _game.Initializie("");
        }*/
    }
}
