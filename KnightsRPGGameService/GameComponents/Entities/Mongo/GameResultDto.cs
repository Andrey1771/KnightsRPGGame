using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;


namespace KnightsRPGGame.Service.GameAPI.GameComponents.Entities.Mongo
{
    public class GameResultDto
    {
        [BsonId]
        [BsonRepresentation(BsonType.ObjectId)]
        public string Id { get; set; } = default!;

        public string PlayerName { get; set; } = default!;
        public float Score { get; set; }
        public DateTime DatePlayed { get; set; }
    }
}
