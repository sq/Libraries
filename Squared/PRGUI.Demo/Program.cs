using System;

namespace PRGUI.Demo {
    static class Program {
        [STAThread]
        static void Main (string[] args) {
            using (DemoGame game = new DemoGame())
                game.Run();
        }
    }
}