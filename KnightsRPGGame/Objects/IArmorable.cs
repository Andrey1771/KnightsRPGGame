using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KnightsRPGGame.Objects
{
    internal interface IArmorable
    {
        public float Armor { get; set; }
        public float MagicResist { get; set; }
    }
}
