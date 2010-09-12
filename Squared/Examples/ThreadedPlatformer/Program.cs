using System;

namespace ThreadedPlatformer {
    static class Program {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        static void Main (string[] args) {
            using (PlatformerGame game = new PlatformerGame()) {
                game.Run();
            }
        }
    }
}

