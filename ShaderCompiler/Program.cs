using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaderCompiler {
    class Program {
        public static void Main (string[] args) {
            var fxcDir = args[0];
            var sourceDir = args[1];
            var destDir = args[2];
            var fxcParams = args[3];
            var fxcPath = Path.Combine(fxcDir, @"fxc.exe");
            var shouldRebuild = (args.Length > 4) && (args[4].ToLower() == "rebuild");
            int totalFileCount = 0, updatedFileCount = 0;
            Console.WriteLine("Compiling shaders from {0}...", sourceDir);
            foreach (var shader in Directory.GetFiles(sourceDir, "*.fx")) {
                var destPath = Path.Combine(destDir, Path.GetFileName(shader) + ".bin");
                var doesNotExist = !File.Exists(destPath);
                var isModified = !doesNotExist && (File.GetLastWriteTimeUtc(destPath) < File.GetLastWriteTimeUtc(shader));
                if (doesNotExist || isModified || shouldRebuild) {
                    Console.WriteLine("File {0} {1}. Compiling...", Path.GetFileName(shader), doesNotExist ? "does not exist" : "is outdated");
                    var psi = new ProcessStartInfo(
                        fxcPath,
                        string.Format("/nologo {0} /T fx_2_0 {1} /Fo {2}", shader, fxcParams, destPath)
                    ) {
                        UseShellExecute = false
                    };
                    using (var p = Process.Start(psi))
                        p.WaitForExit();
                    updatedFileCount++;
                }
                totalFileCount++;
            }
            Console.WriteLine("Compiled {0}/{1} shader(s) to {2}", updatedFileCount, totalFileCount, destDir);
        }
    }
}
