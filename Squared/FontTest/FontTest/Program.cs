using System;

namespace FontTest {
    static class Program {
        [STAThread]
        static void Main(string[] args) {
            using (FontTestGame game = new FontTestGame()) {
                game.Run();
            }
        }
    }
}

