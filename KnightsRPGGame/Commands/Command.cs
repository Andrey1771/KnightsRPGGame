using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using KnightsRPGGame.GameArea;

namespace KnightsRPGGame.Commands
{
    internal abstract class Command
    {
        public Game Game { get; private set; }
        public bool IsValid { get; private set; }

        public Command Execute(Game game)
        {
            Game = game;
            IsValid = Run();
            return this;
        }

        protected abstract bool Run();
    }
}
