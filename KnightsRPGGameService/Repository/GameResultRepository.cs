using KnightsRPGGame.Service.GameAPI.GameComponents.Entities.Mongo;
using MongoDB.Driver;

namespace KnightsRPGGame.Service.GameAPI.Repository
{
    public class GameResultRepository : IGameResultRepository
    {
        private readonly IMongoCollection<GameResultDto> _collection;

        public GameResultRepository(IMongoClient client, IConfiguration config)
        {
            var db = client.GetDatabase(config["ConnectionStrings:Name"]);
            _collection = db.GetCollection<GameResultDto>("results");
        }

        public Task SaveResultAsync(GameResultDto result)
        {
            return _collection.InsertOneAsync(result);
        }
    }
}
