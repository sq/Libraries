using System;
using Squared.Render;

namespace PRGUI.Demo {
    static class Program {
        [STAThread]
        static void Main (string[] args) {
            // STBMipGenerator.InstallGlobally();

            using (DemoGame game = new DemoGame())
                game.Run();
        }
    }
}