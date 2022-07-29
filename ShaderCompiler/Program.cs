using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShaderCompiler {
    class Program {
        private static readonly HashSet<string> Switches = new HashSet<string> {
            "--rebuild", "--parallel", "--disassemble"
        };

        public static void Main (string[] args) {
            var switches = args.Where(a => Switches.Contains(a.ToLower())).ToArray();
            bool shouldRebuild = switches.Contains("--rebuild"),
                buildInParallel = switches.Contains("--parallel"),
                outputDisassembly = switches.Contains("--disassemble");
            args = args.Where(a => !Switches.Contains(a.ToLower())).ToArray();
            
            var fxcDir = args[0];
            var sourceDir = args[1];
            var destDir = args[2];
            var fxcParams = args[3];
            var fxcPath = Path.Combine(fxcDir, @"fxc.exe");
            var fxcPostParams = (args.Length > 4) ? args[4] : "";

            string testParsePath = null;
            if (args.Length > 5)
                testParsePath = args[5];

            if (!File.Exists(fxcPath))
                DownloadFXC(fxcDir);
            int totalFileCount = 0, updatedFileCount = 0, errorCount = 0;
            if (!Directory.Exists(destDir))
                Directory.CreateDirectory(destDir);

            var defines = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var paramsString = fxcParams.Trim();
            if (!string.IsNullOrWhiteSpace(paramsString)) {
                var defineRegex = new Regex("/D[ ]+(?'name'[a-zA-Z0-9_]+)=(?'value'[a-zA-Z0-9_,;.\\-\\*\\!\\&\\|]*)", RegexOptions.ExplicitCapture);
                foreach (Match m in defineRegex.Matches(paramsString)) {
                    var name = m.Groups["name"].Value;
                    var value = m.Groups["value"].Value ?? "";
                    defines.Add(name, value);
                }
            }

            var pending = new List<Task<int>>();
            var outputs = new List<string>();

            Console.WriteLine("Compiling shaders from {0}...", sourceDir);
            foreach (var shader in Directory.GetFiles(sourceDir, "*.fx")) {
                var destPath = Path.Combine(destDir, Path.GetFileName(shader) + ".bin");
                outputs.Add(destPath);
                var paramsPath = Path.Combine(destDir, Path.GetFileName(shader) + ".params");
                var doesNotExist = !File.Exists(destPath);
                var resultDate = File.GetLastWriteTimeUtc(destPath);

                var needNewline = true;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(Path.GetFileName(shader));

                var fileList = EnumerateFilenamesForShader(shader).ToList();
                var isModified = !doesNotExist && fileList.Any((fn) => File.GetLastWriteTimeUtc(fn) >= resultDate);
                var localFxcParams = GetFxcParamsForShader(shader, fxcParams, defines);
                var fullFxcParams = 
                    string.Format(" /T fx_2_0 {0} {1} ", localFxcParams, fxcPostParams);

                string existingParams = null;
                shouldRebuild = switches.Contains("--rebuild");
                if (File.Exists(paramsPath)) {
                    existingParams = File.ReadAllText(paramsPath, Encoding.UTF8).Trim();
                    if (!existingParams.Equals(fullFxcParams.Trim())) {
                        shouldRebuild = true;
                        Console.WriteLine(" params '{0}' -> '{1}'", existingParams, fullFxcParams.Trim());
                    }
                }

                if (doesNotExist || isModified || shouldRebuild) {
                    if (!doesNotExist)
                        File.Delete(destPath);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(
                        " {0}{2}Compiling with params '{1}'...",
                        doesNotExist
                            ? "output missing"
                            : (
                                shouldRebuild
                                    ? "parameters changed"
                                    : "is outdated"
                            ),
                        localFxcParams,
                        Environment.NewLine
                    );
                    needNewline = false;

                    try {
                        if (File.Exists(paramsPath))
                            File.Delete(paramsPath);
                    } catch {
                    }

                    if (buildInParallel) {
                        pending.Add(CompileInParallel(fxcPath, testParsePath, defines, shader, destPath, paramsPath, fullFxcParams, outputDisassembly));
                    } else {
                        var exitCode = CompileShader(fxcPath, testParsePath, defines, shader, destPath, paramsPath, fullFxcParams, outputDisassembly);
                        if (exitCode != 0)
                            errorCount++;
                    }

                    Console.WriteLine();
                    updatedFileCount++;
                }

                Console.ResetColor();
                if (needNewline)
                    Console.WriteLine();
                totalFileCount++;
            }

            if (pending.Count > 0) {
                Console.WriteLine($"Waiting for {pending.Count} parallel compile(s)...");
                foreach (var t in pending) {
                    t.Wait();
                    if (t.Result != 0)
                        errorCount++;
                }
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Compiled {0}/{1} shader(s) with {3} error(s) to '{2}'. Creating archive...", updatedFileCount, totalFileCount, destDir, errorCount);

            var zipPath = Path.Combine(destDir, "shaders.zip");
            var tempPath = zipPath + ".tmp";
            using (var zip = new ZipArchive(File.Open(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None), ZipArchiveMode.Create, false)) {
                foreach (var output in outputs)
                    zip.CreateEntryFromFile(output, Path.GetFileName(output), CompressionLevel.Optimal);
            }
            File.Copy(tempPath, zipPath, true);
            File.Delete(tempPath);
            Console.WriteLine($"Wrote {outputs.Count} shaders to '{zipPath}'.");

            Console.ResetColor();

            if (Debugger.IsAttached)
                Console.ReadLine();
        }

        private static async Task<int> CompileInParallel (
            string fxcPath, string testParsePath, Dictionary<string, string> defines, 
            string shader, string destPath, string paramsPath, string fullFxcParams, 
            bool outputDisassembly
        ) {
            var buf = new StringBuilder();
            // FIXME: Redirecting in .net sucks ass
            buf = null;
            var compileWorker = Task.Run(
                () => CompileShader(fxcPath, testParsePath, defines, shader, destPath, paramsPath, fullFxcParams, outputDisassembly, buf)
            );
            return await compileWorker;
        }

        private static int CompileShader (
            string fxcPath, string testParsePath, Dictionary<string, string> defines, 
            string shader, string destPath, string paramsPath, string fullFxcParams,
            bool outputDisassembly, StringBuilder outputBuffer = null
        ) {
            int exitCode;
            {
                var arglist = "/nologo " + shader + fullFxcParams + "/Fo " + destPath;
                var psi = new ProcessStartInfo(
                    fxcPath, arglist
                ) {
                    UseShellExecute = false,
                    RedirectStandardError = (outputBuffer != null),
                    RedirectStandardOutput = (outputBuffer != null)
                };

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                using (var p = Process.Start(psi)) {
                    p.WaitForExit();
                    exitCode = p.ExitCode;
                }
            }

            if (outputDisassembly) {
                var arglist = "/nologo " + shader + " " + GetParamsForDefines(defines) + " /P " + destPath.Replace(".bin", ".p");
                var psi = new ProcessStartInfo(
                    fxcPath, arglist
                ) {
                    UseShellExecute = false
                };

                Console.ForegroundColor = ConsoleColor.DarkBlue;
                using (var p = Process.Start(psi)) {
                    p.WaitForExit();
                    exitCode += p.ExitCode;
                }
            } else {
                try {
                    File.Delete(destPath.Replace(".bin", ".p"));
                } catch {
                }
            }

            if (exitCode != 0) {
                File.WriteAllText(paramsPath, fullFxcParams.Trim(), Encoding.UTF8);
                if (!String.IsNullOrWhiteSpace(testParsePath)) {
                    var psi = new ProcessStartInfo(
                        testParsePath, string.Format("glsl120 \"{0}\"", destPath)
                    ) {
                        UseShellExecute = false,
                        RedirectStandardOutput = true
                    };
                    using (var outStream = File.OpenWrite(destPath.Replace(".bin", ".glsl")))
                    using (var p = Process.Start(psi)) {
                        p.StandardOutput.BaseStream.CopyTo(outStream);
                        p.StandardOutput.Close();
                        p.WaitForExit();
                    }
                }
            }

            return exitCode;
        }

        private static string GetParamsForDefines (Dictionary<string, string> defines) {
            var result = new StringBuilder();
            foreach (var kvp in defines)
                result.AppendFormat(" /D {0}={1}", kvp.Key, kvp.Value);
            return result.ToString();
        }

        private static string GetFxcParamsForShader (string path, string defaultParams, Dictionary<string, string> defines) {
            var result = new StringBuilder();
            var prologue = "#pragma fxcparams(";
            var conditionalRegex = new Regex("if([ ]*)\\(([ ]*)(?'name'[^ =]+)[ ]*(?'operator'[!=]=)[ ]*(?'value'('[^']*)'|[^ =\\)]+)\\)[ ]*", RegexOptions.ExplicitCapture);

            // TODO: Add a way to define conditional variants like TEXTURED or SHADOWED and have the compiler
            //  automatically compile separate versions with those defines set

            foreach (var line in File.ReadAllLines(path)) {
                if (!line.StartsWith(prologue))
                    continue;

                var skip = false;
                var text = line.Trim().Substring(prologue.Length, line.Length - prologue.Length - 1).Trim();
                var conditionals = conditionalRegex.Matches(text);
                var filteredText = conditionalRegex.Replace(text, "").Trim();

                foreach (Match m in conditionals) {
                    var name = m.Groups["name"].Value;
                    var op = m.Groups["operator"].Value;
                    var value = m.Groups["value"].Value;

                    string definedValue;
                    defines.TryGetValue(name, out definedValue);
                    var doesMatch = (definedValue ?? "").Equals(value.Trim(), StringComparison.OrdinalIgnoreCase);

                    if (op == "!=")
                        doesMatch = !doesMatch;

                    if (!doesMatch)
                        skip = true;
                }

                if (!skip) {
                    result.Append(filteredText);
                    break;
                }
            }

            if (result.Length == 0)
                result.Append(defaultParams.Trim());
            else
                result.Append(GetParamsForDefines(defines));

            return result.ToString().Trim();
        }
        
        private static IEnumerable<string> EnumerateFilenamesForShader (string path) {
            yield return path;

            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            var name = Path.GetFileName(path);
            var prologue = "#include \"";

            foreach (var _line in File.ReadAllLines(path)) {
                var line = _line.Trim();
                if (!line.StartsWith(prologue))
                    continue;

                var includePath = line.Substring(prologue.Length);
                if (includePath.EndsWith("\""))
                    includePath = includePath.Substring(0, includePath.Length - 1);

                var absoluteIncludePath = Path.Combine(dir, includePath);
                if (!File.Exists(absoluteIncludePath)) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("// WARNING: File not found: {0}", absoluteIncludePath);
                }

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
