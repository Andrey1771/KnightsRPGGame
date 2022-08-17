using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KnightsRPGGame.Objects
{
    internal interface IHealthable
    {
        public float HealthPoints { get; set; }
        public float MaxHealthPoints { get; set; }
        public bool Live => HealthPoints > 0;
    }
}
