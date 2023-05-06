using KnightsRPGGame.Service.GameAPI.Models.Notify;
using Microsoft.AspNetCore.SignalR;

namespace KnightsRPGGame.Service.GameAPI.Hubs
{
    public class GameHub : Hub
    {
        public async Task NotifyChangedGame(GameInfoNotify gameInfoNotify)
        {
            await Clients.All.SendAsync("GameChanged", gameInfoNotify);
        }
    }
}
