using KnightsRPGGame.Service.GameAPI.GameComponents.Entities.Mongo;

namespace KnightsRPGGame.Service.GameAPI.Repository
{
    public interface IGameResultRepository
    {
        Task SaveResultAsync(GameResultDto result);
    }
}
