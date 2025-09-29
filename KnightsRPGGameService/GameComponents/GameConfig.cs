using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.GameComponents
{
    public static class GameConfig
    {
        // FPS
        public const int TargetFps = 40;
        public static int FrameIntervalMs => 1000 / TargetFps;

        // Размеры карты
        public const float MapWidth = 640f;
        public const float MapHeight = 960f;

        // Игроки
        public const float PlayerSpeed = 200f;
        public const int PlayerHitDamage = 20;
        public static readonly Vector2 PlayerInitPosition = new Vector2(0, 0);

        // Боты
        public const float BotSpeed = 15f;
        public const int MaxBots = 8;
        public static readonly TimeSpan BotSpawnInterval = TimeSpan.FromSeconds(5);
        public static readonly TimeSpan BotFireInterval = TimeSpan.FromSeconds(2);

        // Пули
        public const float BulletHitRadius = 20f;
        public const float BulletVelocity = 20f;


        public const float PlayerBulletVelocityX = 0;
        public const float PlayerBulletVelocityY = -300;
    }
}
