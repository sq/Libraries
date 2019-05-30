using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Cache;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ShaderCompiler {
    class Program {
        public static void Main (string[] args) {
            var shouldRebuild = args.Any(a => a.ToLower() == "--rebuild");
            args = args.Where(a => a.ToLower() != "--rebuild").ToArray();
            
            var fxcDir = args[0];
            var sourceDir = args[1];
            var destDir = args[2];
            var fxcParams = args[3];
            var fxcPath = Path.Combine(fxcDir, @"fxc.exe");

            string testParsePath = null;
            if (args.Length > 4)
                testParsePath = args[4];

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

                var needNewline = true;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(Path.GetFileName(shader));

                var fileList = EnumerateFilenamesForShader(shader).ToList();
                var isModified = !doesNotExist && fileList.Any((fn) => File.GetLastWriteTimeUtc(fn) >= resultDate);

                if (doesNotExist || isModified || shouldRebuild) {
                    if (!doesNotExist)
                        File.Delete(destPath);

                    var localFxcParams = GetFxcParamsForShader(shader, fxcParams, defines);
                    var fullFxcParams = 
                        string.Format("/nologo {0} /T fx_2_0 {1} /Fo {2}", shader, localFxcParams, destPath);

                    Console.ForegroundColor = ConsoleColor.White;
                    Console.WriteLine(
                        " {0}{2}Compiling with params '{1}'...", 
                        doesNotExist 
                            ? "does not exist"
                            : (
                                shouldRebuild
                                    ? "is forced to rebuild"
                                    : "is outdated"
                            ), 
                        localFxcParams,
                        Environment.NewLine
                    );
                    needNewline = false;

                    int exitCode;
                    {
                        var psi = new ProcessStartInfo(
                            fxcPath, fullFxcParams
                        ) {
                            UseShellExecute = false
                        };

                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        using (var p = Process.Start(psi)) {
                            p.WaitForExit();
                            exitCode = p.ExitCode;
                        }
                    }

                    if (exitCode != 0) {
                        errorCount += 1;
                    } else if (!String.IsNullOrWhiteSpace(testParsePath)) {
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

                    Console.WriteLine();
                    updatedFileCount++;
                }

                Console.ResetColor();
                if (needNewline)
                    Console.WriteLine();
                totalFileCount++;
            }

            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Compiled {0}/{1} shader(s) with {3} error(s) to '{2}'", updatedFileCount, totalFileCount, destDir, errorCount);
            Console.ResetColor();

            if (errorCount == 0)
                File.WriteAllText(oldParamsPath, fxcParams);
            else
                File.Delete(oldParamsPath);

            if (Debugger.IsAttached)
                Console.ReadLine();
        }

        private static string GetFxcParamsForShader (string path, string defaultParams, Dictionary<string, string> defines) {
            var result = new StringBuilder();
            var prologue = "#pragma fxcparams(";
            var conditionalRegex = new Regex("if([ ]*)\\(([ ]*)(?'name'[^ =]+)[ ]*(?'operator'[!=]=)[ ]*(?'value'('[^']*)'|[^ =\\)]+)\\)[ ]*", RegexOptions.ExplicitCapture);

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
            else foreach (var kvp in defines)
                result.AppendFormat(" /D {0}={1}", kvp.Key, kvp.Value);

            return result.ToString().Trim();
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
                if (!File.Exists(absoluteIncludePath)) {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Error.WriteLine("// WARNING: File not found: {0}", absoluteIncludePath);
                }

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
