using System;
using Squared.Render;

namespace PRGUI.Demo {
    static class Program {
        [STAThread]
        static void Main (string[] args) {
            // STBMipGenerator.InstallGlobally();
            Environment.SetEnvironmentVariable("FNA_PLATFORM_BACKEND", "SDL3");

            using (DemoGame game = new DemoGame())
                game.Run();
        }
    }
}