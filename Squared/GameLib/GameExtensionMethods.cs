using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Squared.Game {
    public static class GameExtensionMethods {
        public static double NextDouble (this Random random, double min, double max) {
            return (random.NextDouble() * (max - min)) + min;
        }

        public static float NextFloat (this Random random, float min, float max) {
            return ((float)random.NextDouble() * (max - min)) + min;
        }
    }
}
