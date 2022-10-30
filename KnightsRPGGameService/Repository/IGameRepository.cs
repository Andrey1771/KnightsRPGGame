using KnightsRPGGame.Service.GameAPI.Models.Dto;

namespace KnightsRPGGame.Service.GameAPI.Repository
{
    public interface IGameRepository
    {
        Task<GameInfoDto> GetGameInfo(); // Используется только при загрузки
    }
}
