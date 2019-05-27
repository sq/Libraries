using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
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
            if (!File.Exists(fxcPath))
                DownloadFXC(fxcDir);
            var shouldRebuild = (args.Length > 4) && (args[4].ToLower() == "rebuild");
            int totalFileCount = 0, updatedFileCount = 0;
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            string oldParams = null, oldParamsPath = Path.Combine(destDir, "params.txt");
            if (File.Exists(oldParamsPath))
                oldParams = File.ReadAllText(oldParamsPath);

            if (oldParams != fxcParams)
                shouldRebuild = true;

            Console.WriteLine("Compiling shaders from {0}...", sourceDir);
            foreach (var shader in Directory.GetFiles(sourceDir, "*.fx")) {
                var destPath = Path.Combine(destDir, Path.GetFileName(shader) + ".bin");
                var doesNotExist = !File.Exists(destPath);
                var resultDate = File.GetLastWriteTimeUtc(destPath);
                Console.WriteLine(Path.GetFileName(shader));
                var fileList = EnumerateFilenamesForShader(shader).ToList();
                var isModified = !doesNotExist && fileList.Any((fn) => File.GetLastWriteTimeUtc(fn) >= resultDate);
                if (doesNotExist || isModified || shouldRebuild) {
                    if (!doesNotExist)
                        File.Delete(destPath);

                    var localFxcParams = GetFxcParamsForShader(shader) ?? fxcParams;
                    var fullFxcParams = 
                        string.Format("/nologo {0} /T fx_2_0 {1} /Fo {2}", shader, localFxcParams, destPath);

                    Console.WriteLine("  {0} Compiling with params '{1}'...", doesNotExist ? "does not exist" : "is outdated", localFxcParams);
                    var psi = new ProcessStartInfo(
                        fxcPath, fullFxcParams
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

            File.WriteAllText(oldParamsPath, fxcParams);

            if (Debugger.IsAttached)
                Console.ReadLine();
        }

        private static string GetFxcParamsForShader (string path) {
            var prologue = "#pragma fxcparams(";

            foreach (var line in File.ReadAllLines(path)) {
                if (!line.StartsWith(prologue))
                    continue;

                return line.Substring(prologue.Length).Replace(")", "");
            }

            return null;
        }
        
        private static IEnumerable<string> EnumerateFilenamesForShader (string path) {
            yield return path;

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            var name = Path.GetFileName(path);
            var prologue = "#include \"";

            foreach (var line in File.ReadAllLines(path)) {
                if (!line.StartsWith(prologue))
                    continue;

                var includePath = line.Substring(prologue.Length);
                if (includePath.EndsWith("\""))
                    includePath = includePath.Substring(0, includePath.Length - 1);

                var absoluteIncludePath = Path.Combine(dir, includePath);
                if (!File.Exists(absoluteIncludePath))
                    Console.Error.WriteLine("// WARNING: File not found: {0}", absoluteIncludePath);

                // Console.WriteLine("  {1}", name, includePath);

                foreach (var includedPath in EnumerateFilenamesForShader(absoluteIncludePath))
                    yield return includedPath;
            }
        }

        private static HttpWebRequest MakeRequest (string url) {
            var wr = WebRequest.CreateHttp(url);
            wr.CachePolicy = new RequestCachePolicy(RequestCacheLevel.Revalidate);
            return wr;
        }

        public static void DownloadFXC (string targetDirectory) {
            Directory.CreateDirectory(targetDirectory);
            var rootUrl = "https://viramate.luminance.org/fxc/";
            Console.WriteLine("Downloading fxc from {0}...", rootUrl);

            using (var resp = MakeRequest(rootUrl + "fxc.exe").GetResponse())
            using (var os = File.OpenWrite(Path.Combine(targetDirectory, "fxc.exe")))
                resp.GetResponseStream().CopyTo(os);

            using (var resp = MakeRequest(rootUrl + "d3dcompiler_47.dll").GetResponse())
            using (var os = File.OpenWrite(Path.Combine(targetDirectory, "d3dcompiler_47.dll")))
                resp.GetResponseStream().CopyTo(os);
        }
    }
}
