using KnightsRPGGame.Service.GameAPI.Models.Dto;

namespace KnightsRPGGame.Service.GameAPI.Repository
{
    public class GameRepository : IGameRepository
    {
        public Task<GameInfoDto> GetGameInfo()
        {
            var gameInfo = new GameInfoDto();
            /*gameInfo.PlayersInfo = new List<PlayerInfoDto>();
            var playerInfoDto = new PlayerInfoDto();
            playerInfoDto.Id = 0;
            var position = new Position();
            position.X = 0;
            position.Y = 0;
            playerInfoDto.Position = position;

            gameInfo.PlayersInfo.Add(playerInfoDto);*/
            return Task.Run(() => gameInfo); // TODO XD Временно
        }
    }
}
