using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.GameComponents.Entities
{
    public class EnemyBot
    {
        public string BotId { get; set; } = Guid.NewGuid().ToString();
        public Vector2 Position { get; set; }
        public int Health { get; set; } = 100;

        public int ShootingStyle { get; set; } // 0 = обычный, 1 = веер, 2 = рандом, 3 = следящий
        public DateTime LastShotTime { get; set; } = DateTime.UtcNow;
    }
}
