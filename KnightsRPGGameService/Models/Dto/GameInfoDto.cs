namespace KnightsRPGGame.Service.GameAPI.Models.Dto
{
    public class GameInfoDto
    {
        public MapInfoDto MapInfo { get; set; }
        public ICollection<ObjectInfoDto> ObjectsInfo { get; set; }
    }
}
