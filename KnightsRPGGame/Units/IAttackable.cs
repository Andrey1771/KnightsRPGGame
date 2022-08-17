using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KnightsRPGGame.Objects;

namespace KnightsRPGGame.Units
{
    internal interface IAttackable
    {
        void Attack(IList<IHealthable> objects);
    }
}
