using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Squared.Threading;

namespace Squared.Render.AV1 {
    public unsafe class AV1Video : IDisposable {
        public readonly RenderCoordinator Coordinator;
        private IntPtr YData;
        private IntPtr UData;
        private IntPtr VData;
        private uint YLength;
        private uint UVLength;
        private uint YStride;
        private uint UVStride;

        private MemoryMappedFile MappedFile { get; set; }
        private MemoryMappedViewAccessor MappedView { get; set; }
        private MemoryMappedViewStream MappedViewStream { get; set; }
        private UnmanagedMemoryStream UnmanagedMemoryStream { get; set; }
        public uint DataSize { get; private set; }
        public void* pData { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool OwnsStream { get; private set; }

        public IntPtr Context { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public Dav1dfile.PixelLayout Layout { get; private set; }

        public Texture2D YTexture { get; private set; }
        public Texture2D UTexture { get; private set; }
        public Texture2D VTexture { get; private set; }

        Action AdvanceOrStopSync, AdvanceOrRestartSync;

        public AV1Video (RenderCoordinator coordinator, string filename)
            : this(coordinator, File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), true) {
        }

        public AV1Video (RenderCoordinator coordinator, Stream stream, bool ownsStream) {
            Coordinator = coordinator;

            if (stream is FileStream fs) {
                // FIXME: Does this inherit the stream position? Does it matter?
                MappedFile = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, !ownsStream);
                MappedView = MappedFile.CreateViewAccessor(0, fs.Length, MemoryMappedFileAccess.Read);
                byte* _pData = null;
                MappedView.SafeMemoryMappedViewHandle.AcquirePointer(ref _pData);
                pData = _pData;
                DataSize = (uint)fs.Length;
            } else if (stream is MemoryMappedViewStream mmvs) {
                OwnsStream = ownsStream;
                MappedViewStream = mmvs;
                byte* _pData = null;
                mmvs.SafeMemoryMappedViewHandle.AcquirePointer(ref _pData);
                pData = _pData;
                DataSize = (uint)mmvs.Length;
            } else if (stream is UnmanagedMemoryStream ums) {
                OwnsStream = ownsStream;
                UnmanagedMemoryStream = ums;
                pData = ums.PositionPointer;
                DataSize = (uint)(ums.Length - ums.Position);
            } else {
                throw new ArgumentException("Provided stream must be a FileStream, UnmanagedMemoryStream or MemoryMappedViewStream");
            }

            var ok = Dav1dfile.df_open_from_memory((IntPtr)pData, DataSize, out var context);
            if (ok == 0)
                throw new Exception("Failed to open video");

            Context = context;

            Dav1dfile.df_videoinfo(context, out var width, out var height, out var layout);
            Width = width;
            Height = height;
            Layout = layout;

            int uvWidth, uvHeight;

            switch (layout) {
                case Dav1dfile.PixelLayout.I420:
				    uvWidth = Width / 2;
				    uvHeight = Height / 2;
                    break;
                case Dav1dfile.PixelLayout.I422:
				    uvWidth = Width / 2;
                    uvHeight = Height;
                    break;
                case Dav1dfile.PixelLayout.I444:
				    uvWidth = width;
				    uvHeight = height;
                    break;
                default:
                    throw new Exception("Unsupported pixel layout in AV1 file");
            }

            AdvanceOrRestartSync = _AdvanceOrRestartSync;
            AdvanceOrStopSync = _AdvanceOrStopSync;

            lock (coordinator.CreateResourceLock) {
                YTexture = new Texture2D(coordinator.Device, Width, Height, false, SurfaceFormat.Alpha8) {
                    Name = "AV1Video.YTexture",
                };
                UTexture = new Texture2D(coordinator.Device, uvWidth, uvHeight, false, SurfaceFormat.Alpha8) {
                    Name = "AV1Video.UTexture",
                };
                VTexture = new Texture2D(coordinator.Device, uvWidth, uvHeight, false, SurfaceFormat.Alpha8) {
                    Name = "AV1Video.VTexture",
                };
            }
        }

        public void Reset () {
            Dav1dfile.df_reset(Context);
        }

        public bool DecodeFrames (int frameCount = 1) {
            var ok = Dav1dfile.df_readvideo(
                Context, frameCount,
                out YData, out UData, out VData,
                out YLength, out UVLength,
                out YStride, out UVStride
            );

            if (ok != 1)
                return false;

            return true;
        }

        public void UploadFrame () {
            lock (Coordinator.UseResourceLock) {
                UploadDataToTexture(YTexture, YData, YLength, YStride);
                UploadDataToTexture(UTexture, UData, UVLength, UVStride);
                UploadDataToTexture(VTexture, VData, UVLength, UVStride);
            }
        }

        public void AdvanceAsync (bool loop) {
            var d = loop ? AdvanceOrRestartSync : AdvanceOrStopSync;
            Coordinator.BeforeIssue(d);
        }

        private void _AdvanceOrRestartSync () {
            lock (this) {
                if (!DecodeFrames(1)) {
                    Reset();
                    if (!DecodeFrames(1))
                        return;
                }
                UploadFrame();
            }
        }

        private void _AdvanceOrStopSync () {
            lock (this) {
                if (DecodeFrames(1))
                    UploadFrame();
            }
        }

        private void UploadDataToTexture (Texture2D texture, IntPtr data, uint length, uint stride) {
            texture.SetDataPointerEXT(0, null, data, (int)length);
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            pData = null;
            if (OwnsStream) {
                MappedViewStream?.Dispose();
                UnmanagedMemoryStream?.Dispose();
            }
            MappedView?.Dispose();
            MappedFile?.Dispose();

            YTexture?.Dispose();
            UTexture?.Dispose();
            VTexture?.Dispose();

            YTexture = UTexture = VTexture = null;

            GC.SuppressFinalize(this);
        }

        ~AV1Video () {
            if (!IsDisposed)
                Dispose();
        }
    }
}