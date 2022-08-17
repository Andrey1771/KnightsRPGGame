using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KnightsRPGGame.Objects
{
    internal interface IManable
    {
        public float Mana { get; set; }
        public float MaxMana { get; set; }
    }
}
