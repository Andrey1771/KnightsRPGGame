using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KnightsRPGGame.GameArea
{
    internal class Turns
    {
        public int CurrentTurn { get; private set; }

        internal void NextTurn()
        {
            CurrentTurn++;
        }
    }
}
