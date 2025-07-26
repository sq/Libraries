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
        private byte[] YScratchBuffer, UVScratchBuffer;

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
        public int BitsPerPixel { get; private set; }
        
        public Texture2D YTexture { get; private set; }
        public Texture2D UTexture { get; private set; }
        public Texture2D VTexture { get; private set; }

        Action AdvanceOrStopSync, AdvanceOrRestartSync;

        // Bit depth compensation, Y scale, Post brightness amp, Unused
        public Vector4 RescaleFactor => BitsPerPixel switch {
            12 => new Vector4((float)(1.0 / (4096 / 65536.0)), 1, 1, 0),
            10 => new Vector4((float)(1.0 / (1024 / 65536.0)), 1, 1, 0),
            _ => Vector4.One,
        };

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

            int width, height;
            Dav1dfile.PixelLayout layout;
            try {
                Dav1dfile.df_videoinfo2(context, out width, out height, out layout, out byte hbd);
                BitsPerPixel = hbd switch {
                    2 => 12,
                    1 => 10,
                    _ => 8,
                };
            } catch {
                Dav1dfile.df_videoinfo(context, out width, out height, out layout);
                BitsPerPixel = 8;
            }
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

            YTexture = CreateInternalTexture(coordinator, Width, Height, "AV1Video.YTexture");
            UTexture = CreateInternalTexture(coordinator, uvWidth, uvHeight, "AV1Video.UTexture");
            VTexture = CreateInternalTexture(coordinator, uvWidth, uvHeight, "AV1Video.VTexture");
        }

        private Texture2D CreateInternalTexture (RenderCoordinator coordinator, int width, int height, string name) {
            if (false) {
                var createInfo = new SDL3.SDL.SDL_GPUTextureCreateInfo {
                    format = BitsPerPixel > 8 
                        ? SDL3.SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R16_UNORM 
                        : SDL3.SDL.SDL_GPUTextureFormat.SDL_GPU_TEXTUREFORMAT_R8_UNORM,
                    layer_count_or_depth = 1,
                    num_levels = 1,
                    width = (uint)width,
                    height = (uint)height,
                    sample_count = SDL3.SDL.SDL_GPUSampleCount.SDL_GPU_SAMPLECOUNT_1,
                    type = SDL3.SDL.SDL_GPUTextureType.SDL_GPU_TEXTURETYPE_2D,
                    usage = SDL3.SDL.SDL_GPUTextureUsageFlags.SDL_GPU_TEXTUREUSAGE_SAMPLER,
                };
                return new Squared.Render.Evil.SysTexture2D(coordinator.Device, createInfo) {
                    Name = name,
                };
            } else {
                var fmt = BitsPerPixel > 8 ? SurfaceFormat.UShortEXT : SurfaceFormat.ByteEXT;
                return new Texture2D(coordinator.Device, width, height, false, fmt) {
                    Name = name,
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
            UploadDataToTexture(YTexture, YData, YLength, YStride, ref YScratchBuffer);
            UploadDataToTexture(UTexture, UData, UVLength, UVStride, ref UVScratchBuffer);
            UploadDataToTexture(VTexture, VData, UVLength, UVStride, ref UVScratchBuffer);
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

        private void UploadDataToTexture (Texture2D texture, IntPtr data, uint length, uint stride, ref byte[] scratchBuffer) {
            int w = texture.Width, h = texture.Height,
                dataH = (int)(length / stride),
                availH = Math.Min(dataH, h),
                eltSize = BitsPerPixel > 8 ? 2 : 1,
                rowSize = eltSize * w;

            if (w == stride) {
                texture.SetDataPointerEXT(0, new Rectangle(0, 0, w, availH), data, (int)length);
                return;
            }

            Array.Resize(ref scratchBuffer, w * availH * eltSize);

            fixed (byte* scratch = scratchBuffer) {
                byte* source = (byte*)data;
                /*
                if (TenBit) {
                    // HACK: Rescale to 8 bits
                    unchecked {
                        for (int y = 0; y < availH; y++) {
                            ushort* pSource = (ushort*)(source + (stride * y));
                            byte* pDest = scratch + (w * y);
                            for (int x = 0; x < w; x++) {
                                pDest[x] = (byte)(pSource[x] >> 2);
                            }
                        }
                    }
                } else {
                */
                    for (int y = 0; y < availH; y++) {
                        Buffer.MemoryCopy(source + (stride * y), scratch + (rowSize * y), rowSize, rowSize);
                    }
                // }
                texture.SetDataPointerEXT(0, null, (IntPtr)scratch, scratchBuffer.Length);
            }
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