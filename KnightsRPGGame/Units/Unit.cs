using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using KnightsRPGGame.Objects;

namespace KnightsRPGGame.Units
{
    internal class Unit : GameObject, IMovable, IHealthable, IManable, IArmorable
    {
        public Vector<float> Speed { get; set; }
        public Vector<float> MaxSpeed { get; set; }
        public Vector<float> Acceleration { get; set; }
        public float HealthPoints { get; set; }
        public float MaxHealthPoints { get; set; }
        public float Mana { get; set; }
        public float MaxMana { get; set; }
        public float Armor { get; set; }
        public float MagicResist { get; set; }
    }
}
