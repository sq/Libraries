using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using ComTypes = System.Runtime.InteropServices.ComTypes;
using System.Security;

namespace Squared.Util {
    internal struct FindHandle : IDisposable {
        [DllImport("kernel32.dll")]
        [SuppressUnmanagedCodeSecurity()]
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
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack=1)]
        struct WIN32_FIND_DATA {
            public UInt32 dwFileAttributes;
            public Int64 ftCreationTime;
            public Int64 ftLastAccessTime;
            public Int64 ftLastWriteTime;
            public UInt32 dwFileSizeHigh;
            public UInt32 dwFileSizeLow;
            public UInt32 dwReserved0;
            public UInt32 dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        public struct DirectoryEntry {
            public string Name;
            public uint Attributes;
            public ulong Size;
            public long Created;
            public long LastWritten;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        [SuppressUnmanagedCodeSecurity()]
        static extern IntPtr FindFirstFile (
            string lpFileName, out WIN32_FIND_DATA lpFindFileData
        );

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        [SuppressUnmanagedCodeSecurity()]
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
            return 
                from de in 
                EnumDirectoryEntries(
                    path, searchPattern, recursive, IsDirectory
                )
                select de.Name;
        }

        public static IEnumerable<string> EnumFiles (string path) {
            return EnumFiles(path, "*", false);
        }

        public static IEnumerable<string> EnumFiles (string path, string searchPattern) {
            return EnumFiles(path, searchPattern, false);
        }

        public static IEnumerable<string> EnumFiles (string path, string searchPattern, bool recursive) {
            return 
                from de in 
                EnumDirectoryEntries(
                    path, searchPattern, recursive, IsFile
                )
                select de.Name;
        }

        public static bool IsDirectory (uint attributes) {
            return (attributes & FILE_ATTRIBUTE_DIRECTORY) == FILE_ATTRIBUTE_DIRECTORY;
        }

        public static bool IsFile (uint attributes) {
            return (attributes & FILE_ATTRIBUTE_DIRECTORY) != FILE_ATTRIBUTE_DIRECTORY;
        }

        public static IEnumerable<DirectoryEntry> EnumDirectoryEntries (string path, string searchPattern, bool recursive, Func<uint, bool> attributeFilter) {
            if (!System.IO.Directory.Exists(path))
                throw new System.IO.DirectoryNotFoundException();

            var buffer = new StringBuilder();
            string actualPath = System.IO.Path.GetFullPath(path + @"\");
            var patterns = searchPattern.Split(';');
            var findData = new WIN32_FIND_DATA();
            var searchPaths = new Queue<string>();
            searchPaths.Enqueue("");

            while (searchPaths.Count != 0) {
                string currentPath = searchPaths.Dequeue();

                if (recursive) {
                    buffer.Remove(0, buffer.Length);
                    buffer.Append(actualPath);
                    buffer.Append(currentPath);
                    buffer.Append("*");

                    using (var handle = new FindHandle(FindFirstFile(buffer.ToString(), out findData))) {
                        while (handle.Valid) {
                            string fileName = findData.cFileName;
                            bool isDirectory = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == FILE_ATTRIBUTE_DIRECTORY;

                            if ((fileName != ".") && (fileName != "..") && (isDirectory)) {
                                buffer.Remove(0, buffer.Length);
                                buffer.Append(currentPath);
                                buffer.Append(fileName);
                                buffer.Append("\\");
                                searchPaths.Enqueue(buffer.ToString());
                            }

                            if (!FindNextFile(handle, out findData))
                                break;
                        }
                    }
                }
                
                foreach (string pattern in patterns) {
                    buffer.Remove(0, buffer.Length);
                    buffer.Append(actualPath);
                    buffer.Append(currentPath);
                    buffer.Append(pattern);

                    using (var handle = new FindHandle(FindFirstFile(buffer.ToString(), out findData))) {
                        while (handle.Valid) {
                            string fileName = findData.cFileName;
                            bool masked = !attributeFilter(findData.dwFileAttributes);

                            if ((fileName != ".") && (fileName != "..") && (!masked)) {
                                buffer.Remove(0, buffer.Length);
                                buffer.Append(actualPath);
                                buffer.Append(currentPath);
                                buffer.Append(fileName);

                                yield return new DirectoryEntry {
                                    Name = buffer.ToString(),
                                    Attributes = findData.dwFileAttributes,
                                    Size = findData.dwFileSizeLow + (findData.dwFileSizeHigh * ((ulong)(UInt32.MaxValue) + 1)),
                                    Created = findData.ftCreationTime,
                                    LastWritten = findData.ftLastWriteTime
                                };
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
