using System;

namespace LargeBufferTest {
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            using (LargeBufferTestGame game = new LargeBufferTestGame())
            {
                game.Run();
            }
        }
    }
}

