namespace KnightsRPGGame.Service.GameAPI.GameComponents
{
    public static class GameConfig
    {
        // Размеры карты
        public const float MapWidth = 640f;
        public const float MapHeight = 960f;

        // Игроки
        public const float PlayerSpeed = 200f;
        public const int PlayerHitDamage = 20;

        // Боты
        public const float BotSpeed = 15f;
        public const int MaxBots = 8;
        public static readonly TimeSpan BotSpawnInterval = TimeSpan.FromSeconds(5);

        // Пули
        public const float BulletHitRadius = 20f;
        public const float BulletVelocity = 20f;
    }
}
