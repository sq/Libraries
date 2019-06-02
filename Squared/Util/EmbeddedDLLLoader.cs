using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Util {
    public class EmbeddedDLLLoader : IDisposable {
        public bool IsDisposed { get; private set; }

        [DllImport("kernel32", SetLastError=true, CharSet = CharSet.Ansi)]
        static extern IntPtr LoadLibrary(
            [MarshalAs(UnmanagedType.LPStr)]
            string lpFileName
        );

        [DllImport("kernel32", SetLastError=true)]
        static extern bool FreeLibrary(IntPtr hModule);

        public readonly Assembly Assembly;
        internal readonly List<IntPtr> LoadedHandles = new List<IntPtr>();
        internal readonly List<string> CreatedFiles = new List<string>();
        internal string TemporaryDirectory;

        internal bool IsRegisteredForShutdownNotice;

        public EmbeddedDLLLoader (Assembly assembly) {
            Assembly = assembly;
        }

        private string GetDirectory () {
            if (TemporaryDirectory == null) {
                var tempdir = Path.GetTempPath();
                if (tempdir != null) {
                    var desiredPath = Path.Combine(tempdir, "Squared.Util.EmbeddedDLLLoader", Assembly.GetName().Name);
                    try {
                        Directory.CreateDirectory(desiredPath);
                        return TemporaryDirectory = desiredPath;
                    } catch {
                    }
                }
                var path = Path.GetTempFileName();
                File.Delete(path);
                Directory.CreateDirectory(path);
                TemporaryDirectory = path;
            }
            return TemporaryDirectory;
        }

        public void Load (string name) {
            var path = Path.Combine(GetDirectory(), name);
            using (var src = Assembly.GetManifestResourceStream(name))
            using (var dest = File.OpenWrite(path))
                src.CopyTo(dest);

            CreatedFiles.Add(path);
            var hLibrary = LoadLibrary(path);
            if (hLibrary == null)
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "Failed to load " + name);
            LoadedHandles.Add(hLibrary);
        }

        public void Dispose () {
            if (IsDisposed)
                return;
            IsDisposed = true;

            foreach (var ptr in LoadedHandles)
                FreeLibrary(ptr);

            foreach (var file in CreatedFiles) {
                try {
                    File.Delete(file);
                } catch {
                }
            }
        }
    }
}
