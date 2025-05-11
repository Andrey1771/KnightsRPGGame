namespace KnightsRPGGame.Service.GameAPI.GameComponents.Entities
{
    public enum BulletType
    {
        Straight,
        ZigZag,
        Arc,
        Explosive
    }

    public class EnemyBulletDto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ShooterBotId { get; set; } = default!;
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
        public BulletType Type { get; set; } = BulletType.Straight;
        public float TimeAlive { get; set; } // Нужен для ZigZag / Arc
    }

}
