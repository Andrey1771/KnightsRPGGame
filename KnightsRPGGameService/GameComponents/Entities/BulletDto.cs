namespace KnightsRPGGame.Service.GameAPI.GameComponents.Entities
{
    public class BulletDto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string OwnerId { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }

        public static float PlayerVelocityX { get; } = 0; // TODO Перенести в правила игры
        public static float PlayerVelocityY { get; } = -300; // TODO Перенести в правила игры
    }
}
