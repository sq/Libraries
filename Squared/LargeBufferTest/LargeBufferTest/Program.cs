using System;
using SDL3;

namespace LargeBufferTest {
    static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            Environment.SetEnvironmentVariable("FNA_PLATFORM_BACKEND", "SDL3");
            // SDL.SDL_SetHintWithPriority("FNA3D_FORCE_DRIVER", "D3D11", SDL.SDL_HintPriority.SDL_HINT_OVERRIDE);

            using (LargeBufferTestGame game = new LargeBufferTestGame())
            {
                game.Run();
            }
        }
    }
}

