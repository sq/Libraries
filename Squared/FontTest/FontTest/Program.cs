using System;

namespace FontTest {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
            Environment.SetEnvironmentVariable("FNA_PLATFORM_BACKEND", "SDL3");

            using (FontTestGame game = new FontTestGame()) {
                game.Run();
            }
        }
    }
}

