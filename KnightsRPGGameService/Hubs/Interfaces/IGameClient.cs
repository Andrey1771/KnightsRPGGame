using KnightsRPGGame.Service.GameAPI.GameComponents.Entities;
using System;

namespace KnightsRPGGame.Service.GameAPI.Hubs.Interfaces
{
    public interface IGameClient
    {
        Task RoomCreated(string roomName);
        Task Error(string message);
        Task PlayerJoined(string connectionId);
        Task PlayerLeft(string connectionId);
        Task ReceivePlayerList(PlayerInfoResponseDto dto);
        Task GameStarted(Dictionary<string, PlayerStateDto> initialPositions, Dictionary<string, BotStateDto> bots);
        Task GamePaused(bool isPaused);
        Task ReceivePlayerPosition(string connectionId, PlayerStateDto position);

        Task ReceiveBotHit(string botId, int health);
        Task BotDied(string botId);
        Task BulletFired(string connectionId, PlayerStateDto startPosition);
        Task ReceiveBotList(Dictionary<string, BotStateDto> bots);
        Task SpawnBullet(BulletDto bullet);
        Task RemoveBullet(string bulletId);
        Task UpdateBullet(BulletDto bullet);

        Task SpawnEnemyBullet(EnemyBulletDto bullet);
        Task UpdateEnemyBullet(EnemyBulletDto bullet);
        Task RemoveEnemyBullet(string bulletId);
        Task PlayerHit(string connectionId, int newHealth);
        Task PlayerDied(string connectionId);
        Task ReceiveBotPosition(string botId, BotStateDto playerPositionDto);

        Task UpdateScore(float score);
        Task GameOver(float score);
    }
}
