using KnightsRPGGame.Service.GameAPI.Models.Dto;

namespace KnightsRPGGame.Service.GameAPI.Models.Notify
{
    public class GameInfoNotify
    {
        public ICollection<PlayerInfoDto> PlayersInfo { get; set; }
        public MapInfoDto MapInfo { get; set; }
        public ICollection<ObjectInfoDto> ObjectsInfo { get; set; }
    }
}
