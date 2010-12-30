using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.RegularExpressions;
#if !XBOX
using ComTypes = System.Runtime.InteropServices.ComTypes;
using System.Drawing;
#endif

#if XBOX
namespace System.IO {
    // Who would ever want to throw exceptions on the XBox? Apparently more people than they thought
    public sealed class InvalidDataException : SystemException {
        public InvalidDataException ()
            : base() {
        }

        public InvalidDataException (string message)
            : base(message) {
        }

        public InvalidDataException (string message, Exception innerException)
            : base(message, innerException) {
        }
    }
}
#endif

namespace Squared.Util {
    public static class DisposableBufferPool<T>
        where T : IDisposable {

        public struct DisposableBuffer : IDisposable {
            public T[] Data;

            public DisposableBuffer (T[] data) {
                Data = data;
            }

            public static implicit operator T[] (DisposableBuffer _) {
                return _.Data;
            }

            public void Clear (int index, int length) {
                Array.Clear(Data, index, length);
            }

            public void Dispose () {
                T[] data = Data;
                Data = null;

                foreach (var item in data)
                    item.Dispose();

                BufferPool<T>.AddToPool(data);
            }
        }

        public static DisposableBuffer Allocate (int size) {
            var b = BufferPool<T>.Allocate(size);
            return new DisposableBuffer(b.Data);
        }
    }

    public static class BufferPool<T> {
        public static int MaxPoolCount = 8;
        public static int MaxBufferSize = 4096;

        public struct Buffer : IDisposable {
            public T[] Data;

            public Buffer (T[] data) {
                Data = data;
            }

            public static implicit operator T[] (Buffer _) {
                return _.Data;
            }

            public void Clear (int index, int length) {
                Array.Clear(Data, index, length);
            }

            public void Dispose () {
                T[] data = Data;
                Data = null;

                BufferPool<T>.AddToPool(data);
            }
        }

        private static List<T[]> Pool = new List<T[]>();

        internal static void AddToPool (T[] buffer) {
            if (buffer.Length > MaxBufferSize)
                return;

            lock (Pool) {
                if (Pool.Count < MaxPoolCount)
                    Pool.Add(buffer);
            }
        }

        public static Buffer Allocate (int size) {
            lock (Pool) {
                for (int i = Pool.Count - 1; i >= 0; i--) {
                    var item = Pool[i];
                    if (item.Length >= size) {
                        T[] result = item;
                        Pool.RemoveAt(i);
                        return new Buffer(result);
                    }
                }

                if ((Pool.Count == MaxPoolCount) && (size < MaxBufferSize)) {
                    int smallest = int.MaxValue;
                    int smallestIndex = -1;
                    for (int i = 0; i < Pool.Count; i++) {
                        if (Pool[i].Length < smallest) {
                            smallest = Pool[i].Length;
                            smallestIndex = i;
                        }
                    }

                    if (smallestIndex != -1)
                        Pool.RemoveAt(smallestIndex);
                }
            }

            {
                T[] result = new T[size];
                return new Buffer(result);
            }
        }
    }

    public class CharacterBuffer : IDisposable {
        public static int DefaultBufferSize = 1024;

        private BufferPool<char>.Buffer _Buffer;
        private char[] _Data;
        private int _Length = 0;

        public CharacterBuffer () {
            ResizeBuffer(DefaultBufferSize);
        }

        private void ResizeBuffer (int size) {
            BufferPool<char>.Buffer temp = BufferPool<char>.Allocate(size);

            if (_Buffer.Data != null) {
                Array.Copy(_Buffer.Data, temp.Data, _Length);
                _Buffer.Dispose();
            }

            _Buffer = temp;
            _Data = _Buffer.Data;
        }

        public string DisposeAndGetContents () {
            string result = ToString();
            Dispose();
            return result;
        }

        public void Dispose () {
            _Length = 0;
            if (_Data != null)
                _Buffer.Dispose();
            _Data = null;
        }

        public void Grow (int extraCharactersNeeded) {
            int newLength = _Length + extraCharactersNeeded;
            int bufferSize = _Data.Length;

            while (bufferSize < newLength)
                bufferSize *= 2;

            if (bufferSize > _Data.Length)
                ResizeBuffer(bufferSize);
        }

        public void Append (char[] source, int offset, int count) {
            int newLength = _Length + count;
            int bufferSize = _Data.Length;

            while (bufferSize < newLength)
                bufferSize *= 2;

            if (bufferSize > _Data.Length)
                ResizeBuffer(bufferSize);

            Array.Copy(source, offset, _Data, _Length, count);
            _Length += count;
        }

        public void Append (char character) {
            int bufferSize = _Data.Length;
            if (_Length == bufferSize)
                ResizeBuffer(bufferSize * 2);

            _Data[_Length] = character;
            _Length += 1;
        }

        public void Remove (int position, int length) {
            int newLength = _Length - length;
            int sourceIndex = position + length;
            int copySize = _Length - position;

            if ((position + copySize) < _Length)
                Array.Copy(_Data, sourceIndex, _Data, position, copySize);

            _Length = newLength;
        }

        public void Clear () {
            _Length = 0;
        }

        public override string ToString () {
            return new String(_Data, 0, _Length);
        }

        public char this[int index] {
            get {
                return _Data[index];
            }
            set {
                _Data[index] = value;
            }
        }

        public int Capacity {
            get {
                return _Data.Length;
            }
        }

        public int Length {
            get {
                return _Length;
            }
        }
    }

#if !XBOX
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
#endif

    public static class IO {
#if !XBOX
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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEINFO {
            public IntPtr hIcon;
            public IntPtr iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        };

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

        const uint SHGFI_ICON = 0x100;
        const uint SHGFI_LARGEICON = 0x0;
        const uint SHGFI_SMALLICON = 0x1;
        const uint SHGFI_USEFILEATTRIBUTES = 0x10;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        [SuppressUnmanagedCodeSecurity()]
        static extern IntPtr SHGetFileInfo (string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        static System.Reflection.ConstructorInfo _IconConstructor = null;

        public static Icon ExtractAssociatedIcon (string path, bool large) {
            if (_IconConstructor == null) {
                _IconConstructor = typeof(System.Drawing.Icon).GetConstructor(
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                    null,
                    new Type[] { typeof(IntPtr), typeof(bool) },
                    new System.Reflection.ParameterModifier[0]
                );
            }

            var info = new SHFILEINFO();
            uint fileAttributes = FILE_ATTRIBUTE_NORMAL;
            uint flags = SHGFI_ICON | (large ? SHGFI_LARGEICON : SHGFI_SMALLICON);
            try {
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                    flags |= SHGFI_USEFILEATTRIBUTES;
            } catch (Exception) {
                flags |= SHGFI_USEFILEATTRIBUTES;
            }
            SHGetFileInfo(path, fileAttributes, ref info, (uint)Marshal.SizeOf(info), flags);

            var iconHandle = info.hIcon;
            if (iconHandle != IntPtr.Zero)
                return (Icon)_IconConstructor.Invoke(new object[] { iconHandle, true });
            else
                return null;
        }
#endif

        const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        const int FILE_ATTRIBUTE_NORMAL = 0x80;

        public struct DirectoryEntry {
            public string Name;
            public uint Attributes;
            public ulong Size;
            public long Created;
            public long LastAccessed;
            public long LastWritten;
            public bool IsDirectory;
        }

        public static Encoding DetectStreamEncoding (System.IO.Stream stream) {
            var reader = new System.IO.StreamReader(stream, true);
            var buffer = new char[256];

            reader.ReadBlock(buffer, 0, (int)Math.Min(buffer.Length, stream.Length));
            var result = reader.CurrentEncoding;

            stream.Seek(0, System.IO.SeekOrigin.Begin);
            return result;
        }

        public static Regex GlobToRegex (string glob) {
            if (glob.EndsWith(".*"))
                glob = glob.Substring(0, glob.Length - 2);
            glob = "^" + Regex.Escape(glob.ToLower()).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return new Regex(
                glob, 
                RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture
            );
        }

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
#if XBOX
            throw new NotImplementedException();
#else
            if (!System.IO.Directory.Exists(path))
                throw new System.IO.DirectoryNotFoundException();

            var buffer = new StringBuilder();
            string actualPath = System.IO.Path.GetFullPath(path + @"\");
            var patterns = searchPattern.Split(';');
            var globs = (from p in patterns select GlobToRegex(p)).ToArray();
            var findData = new WIN32_FIND_DATA();
            var searchPaths = new Queue<string>();
            searchPaths.Enqueue("");

            while (searchPaths.Count != 0) {
                string currentPath = searchPaths.Dequeue();

                buffer.Remove(0, buffer.Length);
                buffer.Append(actualPath);
                buffer.Append(currentPath);
                buffer.Append("*");

                using (var handle = new FindHandle(FindFirstFile(buffer.ToString(), out findData)))
                while (handle.Valid) {
                    string fileName = findData.cFileName;

                    if ((fileName != ".") && (fileName != "..")) {
                        bool isDirectory = (findData.dwFileAttributes & FILE_ATTRIBUTE_DIRECTORY) == FILE_ATTRIBUTE_DIRECTORY;
                        bool masked = !attributeFilter(findData.dwFileAttributes);

                        buffer.Remove(0, buffer.Length);
                        buffer.Append(actualPath);
                        buffer.Append(currentPath);
                        buffer.Append(fileName);

                        if (isDirectory)
                            buffer.Append("\\");

                        if (recursive && isDirectory) {
                            var subdir = buffer.ToString().Substring(actualPath.Length);
                            searchPaths.Enqueue(subdir);
                        }

                        if (!masked) {
                            string fileNameLower = fileName.ToLower();

                            bool globMatch = false;
                            foreach (var glob in globs) {
                                if (glob.IsMatch(fileNameLower)) {
                                    globMatch = true;
                                    break;
                                }
                            }

                            if (globMatch) {
                                yield return new DirectoryEntry {
                                    Name = buffer.ToString(),
                                    Attributes = findData.dwFileAttributes,
                                    Size = findData.dwFileSizeLow + (findData.dwFileSizeHigh * ((ulong)(UInt32.MaxValue) + 1)),
                                    Created = findData.ftCreationTime,
                                    LastAccessed = findData.ftLastAccessTime,
                                    LastWritten = findData.ftLastWriteTime,
                                    IsDirectory = isDirectory
                                };
                            }
                        }
                    }

                    if (!FindNextFile(handle, out findData))
                        break;
                }
            }
#endif
        }
    }
}
