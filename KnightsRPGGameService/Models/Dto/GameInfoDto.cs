namespace KnightsRPGGame.Service.GameAPI.Models.Dto
{
    public class GameInfoDto
    {
        public ICollection<PlayerInfoDto> PlayersInfo { get; set; }
        public MapInfoDto MapInfo { get; set; }
        public ICollection<ObjectInfoDto> ObjectsInfo { get; set; }
    }
}
