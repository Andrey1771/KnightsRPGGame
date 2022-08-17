using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using KnightsRPGGame.Objects;
namespace KnightsRPGGame.Shells
{
    internal class Shell : GameObject, IMovable
    {
        public Vector<float> Speed { get; set; }
        public Vector<float> Acceleration { get; set; }
    }
}
