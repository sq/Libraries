using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;

namespace Squared.Util {
    internal struct FindHandle : IDisposable {
        [DllImport("kernel32.dll")]
        static extern bool FindClose (IntPtr hFindFile);

        public IntPtr Handle;

        public FindHandle (IntPtr handle) {
            Handle = handle;
        }

        public static implicit operator IntPtr (FindHandle handle) {
            return handle.Handle;
        }

        public bool Valid {
            get {
                int value = Handle.ToInt32();
                return (value != -1) && (value != 0);
            }
        }

        public void Dispose () {
            if (Handle != IntPtr.Zero) {
                FindClose(Handle);
                Handle = IntPtr.Zero;
            }
        }
    }

    public static class IO {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WIN32_FIND_DATA {
            public uint dwFileAttributes;
            public ComTypes.FILETIME ftCreationTime;
            public ComTypes.FILETIME ftLastAccessTime;
            public ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr FindFirstFile (
            string lpFileName, out WIN32_FIND_DATA lpFindFileData
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool FindNextFile (
            IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData
        );

        const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        const int FILE_ATTRIBUTE_NORMAL = 0x80;

        public static IEnumerable<string> EnumDirectories (string path) {
            return EnumDirectories(path, "*", false);
        }

        public static IEnumerable<string> EnumDirectories (string path, string searchPattern) {
            return EnumDirectories(path, searchPattern, false);
        }

        public static IEnumerable<string> EnumDirectories (string path, string searchPattern, bool recursive) {
            return EnumDirectoryEntries(
                path, searchPattern, recursive, 
                (a) => (a & FILE_ATTRIBUTE_DIRECTORY) == FILE_ATTRIBUTE_DIRECTORY
            );
        }

        public static IEnumerable<string> EnumFiles (string path) {
            return EnumFiles(path, "*", false);
        }

        public static IEnumerable<string> EnumFiles (string path, string searchPattern) {
            return EnumFiles(path, searchPattern, false);
        }

        public static IEnumerable<string> EnumFiles (string path, string searchPattern, bool recursive) {
            if ((searchPattern == "*") || (recursive == false)) {
                return EnumDirectoryEntries(
                    path, searchPattern, recursive,
                    (a) => (a & FILE_ATTRIBUTE_DIRECTORY) != FILE_ATTRIBUTE_DIRECTORY
                );
            } else {
                return _EnumFilesRecursive(path, searchPattern);
            }
        }

        internal static IEnumerable<string> _EnumFilesRecursive (string path, string searchPattern) {
            foreach (string dir in EnumDirectories(path, "*", true)) {
                foreach (string file in EnumFiles(dir, searchPattern, false)) {
                    yield return file;
                }
            }
        }

        internal static IEnumerable<string> EnumDirectoryEntries (string path, string searchPattern, bool recursive, Func<uint, bool> filter) {
            if (!System.IO.Directory.Exists(path))
                throw new System.IO.DirectoryNotFoundException();

            string actualPath = System.IO.Path.GetFullPath(path + @"\");
            var patterns = searchPattern.Split(';');
            var findData = new WIN32_FIND_DATA();
            var searchPaths = new Queue<string>();
            searchPaths.Enqueue("");

            while (searchPaths.Count != 0) {
                string currentPath = searchPaths.Dequeue();
                foreach (string pattern in patterns) {
                    using (var handle = new FindHandle(FindFirstFile(actualPath + currentPath + pattern, out findData))) {
                        while (handle.Valid) {
                            string fileName = findData.cFileName;
                            if (fileName.Length == 0)
                                fileName = findData.cAlternateFileName;
                            bool isDirectory = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == FILE_ATTRIBUTE_DIRECTORY;
                            bool masked = !filter(findData.dwFileAttributes);

                            if ((fileName == ".") || (fileName == "..")) {
                            } else {
                                if (isDirectory && recursive) {
                                    var item = currentPath + fileName + @"\";
                                    searchPaths.Enqueue(item);
                                }

                                if (!masked) {
                                    yield return actualPath + currentPath + fileName;
                                }
                            }

                            if (!FindNextFile(handle, out findData))
                                break;
                        }
                    }
                }
            }
        }
    }
}
