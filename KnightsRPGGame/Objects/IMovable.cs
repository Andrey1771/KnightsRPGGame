using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace KnightsRPGGame.Objects
{
    internal interface IMovable
    {
        public Vector<float> Speed { get; set; }
        public Vector<float> MaxSpeed { get; set; }
        public Vector<float> Acceleration { get; set; }
    }
}
