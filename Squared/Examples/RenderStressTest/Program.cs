using System;

namespace RenderStressTest {
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            using (Game game = new Game())
            {
                game.Run();
            }
        }
    }
}

