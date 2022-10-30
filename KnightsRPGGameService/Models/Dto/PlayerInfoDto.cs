using KnightsRPGGame.Utilits;

namespace KnightsRPGGame.Service.GameAPI.Models.Dto
{
    public class PlayerInfoDto
    {
        // TODO в будущем заменится на токен
        public int Id { get; set; }
        public Position Position { get; set; }
    }
}
