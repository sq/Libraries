using System;

namespace RenderPrecisionTest {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
            using (RenderPrecisionTestGame game = new RenderPrecisionTestGame()) {
                game.Run();
            }
        }
    }
}

