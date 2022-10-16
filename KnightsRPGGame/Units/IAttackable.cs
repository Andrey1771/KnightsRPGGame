using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using KnightsRPGGame.Objects;
using KnightsRPGGame.Shells;

namespace KnightsRPGGame.Units
{
    internal interface IAttackable
    {
        void Attack(Shell shell, Vector<float> direction);
    }
}
