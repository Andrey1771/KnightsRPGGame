namespace KnightsRPGGame.Service.GameAPI.GameComponents.Entities
{
    public class EnemyBulletDto
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string ShooterBotId { get; set; } = string.Empty;
        public float X { get; set; }
        public float Y { get; set; }
        public float VelocityX { get; set; }
        public float VelocityY { get; set; }
    }
}
