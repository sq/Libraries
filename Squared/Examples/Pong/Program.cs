using System;

namespace Pong {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main(string[] args) {
            using (var game = new PongExample()) {
                game.Run();
            }
        }
    }
}

