﻿using System.Numerics;

namespace KnightsRPGGame.Service.GameAPI.GameComponents.Entities
{
    public class PlayerState
    {
        public string ConnectionId { get; set; } = string.Empty;
        public Vector2 Position { get; set; }
        public int Health { get; set; } = 100;
        public int Score { get; set; } = 0;
    }
}
