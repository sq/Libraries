using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Squared.Render.Basis { 
    public static unsafe class Transcoder {
        static Transcoder () {
            try {
                var loader = new Util.EmbeddedDLLLoader(Assembly.GetExecutingAssembly());
                loader.Load(DllName + ".dll");
            } catch (Exception exc) {
                Console.Error.WriteLine("Failed to load {0}: {1}", DllName, exc.Message);
            }
        }

        public const string DllName = "basis";

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr New (bool ktx2);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern void Delete (IntPtr transcoder);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Start (IntPtr transcoder, void * pData, UInt32 dataSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UInt32 GetTotalImages (IntPtr transcoder, void * pData, UInt32 dataSize);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetImageInfo (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, out ImageInfo result
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetImageLevelInfo (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, UInt32 levelIndex, out ImageLevelInfo result
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int GetImageLevelDesc (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, UInt32 levelIndex,
            out UInt32 originalWidth, out UInt32 originalHeight, out UInt32 totalBlocks
        );

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern UInt32 GetBytesPerBlockOrPixel (TranscoderTextureFormats format);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int TranscodeImageLevel (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, UInt32 levelIndex,
            void * pOutput, UInt32 outputSizeInBlocks,
            TranscoderTextureFormats format, DecodeFlags decodeFlags,
            UInt32 outputRowPitch, UInt32 outputHeightInPixels
        );

        public static bool IsBlockTextureFormat (TranscoderTextureFormats format) {
            switch (format) {
                case TranscoderTextureFormats.RGBA32:
                case TranscoderTextureFormats.RGB565:
                case TranscoderTextureFormats.BGR565:
                case TranscoderTextureFormats.RGBA4444:
                    return false;
                default:
                    return true;
            }
        }
    }

    public enum BlockFormats : UInt32 {
		cETC1,								// ETC1S RGB 
		cETC2_RGBA,							// full ETC2 EAC RGBA8 block
		cBC1,									// DXT1 RGB 
		cBC3,									// BC4 block followed by a four color BC1 block
		cBC4,									// DXT5A (alpha block only)
		cBC5,									// two BC4 blocks
		cPVRTC1_4_RGB,						// opaque-only PVRTC1 4bpp
		cPVRTC1_4_RGBA,					// PVRTC1 4bpp RGBA
		cBC7,									// Full BC7 block, any mode
		cBC7_M5_COLOR,						// RGB BC7 mode 5 color (writes an opaque mode 5 block)
		cBC7_M5_ALPHA,						// alpha portion of BC7 mode 5 (cBC7_M5_COLOR output data must have been written to the output buffer first to set the mode/rot fields etc.)
		cETC2_EAC_A8,						// alpha block of ETC2 EAC (first 8 bytes of the 16-bit ETC2 EAC RGBA format)
		cASTC_4x4,							// ASTC 4x4 (either color-only or color+alpha). Note that the transcoder always currently assumes sRGB is not enabled when outputting ASTC 
												// data. If you use a sRGB ASTC format you'll get ~1 LSB of additional error, because of the different way ASTC decoders scale 8-bit endpoints to 16-bits during unpacking.
		
		cATC_RGB,
		cATC_RGBA_INTERPOLATED_ALPHA,
		cFXT1_RGB,							// Opaque-only, has oddball 8x4 pixel block size

		cPVRTC2_4_RGB,
		cPVRTC2_4_RGBA,

		cETC2_EAC_R11,
		cETC2_EAC_RG11,
												
		cIndices,							// Used internally: Write 16-bit endpoint and selector indices directly to output (output block must be at least 32-bits)

		cRGB32,								// Writes RGB components to 32bpp output pixels
		cRGBA32,								// Writes RGB255 components to 32bpp output pixels
		cA32,									// Writes alpha component to 32bpp output pixels
				
		cRGB565,
		cBGR565,
		
		cRGBA4444_COLOR,
		cRGBA4444_ALPHA,
		cRGBA4444_COLOR_OPAQUE,
		cRGBA4444,
						
		cTotalBlockFormats
    }

    public enum TranscoderTextureFormats : UInt32 {
		// Compressed formats

		// ETC1-2
		ETC1_RGB = 0,							// Opaque only, returns RGB or alpha data if cDecodeFlagsTranscodeAlphaDataToOpaqueFormats flag is specified
		ETC2_RGBA = 1,							// Opaque+alpha, ETC2_EAC_A8 block followed by a ETC1 block, alpha channel will be opaque for opaque .basis files

		// BC1-5, BC7 (desktop, some mobile devices)
		BC1_RGB = 2,							// Opaque only, no punchthrough alpha support yet, transcodes alpha slice if cDecodeFlagsTranscodeAlphaDataToOpaqueFormats flag is specified
		BC3_RGBA = 3, 							// Opaque+alpha, BC4 followed by a BC1 block, alpha channel will be opaque for opaque .basis files
		BC4_R = 4,								// Red only, alpha slice is transcoded to output if cDecodeFlagsTranscodeAlphaDataToOpaqueFormats flag is specified
		BC5_RG = 5,								// XY: Two BC4 blocks, X=R and Y=Alpha, .basis file should have alpha data (if not Y will be all 255's)
		BC7_RGBA = 6,							// RGB or RGBA, mode 5 for ETC1S, modes (1,2,3,5,6,7) for UASTC

		// PVRTC1 4bpp (mobile, PowerVR devices)
		PVRTC1_4_RGB = 8,						// Opaque only, RGB or alpha if cDecodeFlagsTranscodeAlphaDataToOpaqueFormats flag is specified, nearly lowest quality of any texture format.
		PVRTC1_4_RGBA = 9,					// Opaque+alpha, most useful for simple opacity maps. If .basis file doesn't have alpha cTFPVRTC1_4_RGB will be used instead. Lowest quality of any supported texture format.

		// ASTC (mobile, Intel devices, hopefully all desktop GPU's one day)
		ASTC_4x4_RGBA = 10,					// Opaque+alpha, ASTC 4x4, alpha channel will be opaque for opaque .basis files. Transcoder uses RGB/RGBA/L/LA modes, void extent, and up to two ([0,47] and [0,255]) endpoint precisions.

		// ATC (mobile, Adreno devices, this is a niche format)
		ATC_RGB = 11,							// Opaque, RGB or alpha if cDecodeFlagsTranscodeAlphaDataToOpaqueFormats flag is specified. ATI ATC (GL_ATC_RGB_AMD)
		ATC_RGBA = 12,							// Opaque+alpha, alpha channel will be opaque for opaque .basis files. ATI ATC (GL_ATC_RGBA_INTERPOLATED_ALPHA_AMD) 

		// FXT1 (desktop, Intel devices, this is a super obscure format)
		FXT1_RGB = 17,							// Opaque only, uses exclusively CC_MIXED blocks. Notable for having a 8x4 block size. GL_3DFX_texture_compression_FXT1 is supported on Intel integrated GPU's (such as HD 630).
														// Punch-through alpha is relatively easy to support, but full alpha is harder. This format is only here for completeness so opaque-only is fine for now.
														// See the BASISU_USE_ORIGINAL_3DFX_FXT1_ENCODING macro in basisu_transcoder_internal.h.

		PVRTC2_4_RGB = 18,					// Opaque-only, almost BC1 quality, much faster to transcode and supports arbitrary texture dimensions (unlike PVRTC1 RGB).
		PVRTC2_4_RGBA = 19,					// Opaque+alpha, slower to encode than cTFPVRTC2_4_RGB. Premultiplied alpha is highly recommended, otherwise the color channel can leak into the alpha channel on transparent blocks.

		ETC2_EAC_R11 = 20,					// R only (ETC2 EAC R11 unsigned)
		ETC2_EAC_RG11 = 21,					// RG only (ETC2 EAC RG11 unsigned), R=opaque.r, G=alpha - for tangent space normal maps

		// Uncompressed (raw pixel) formats
		RGBA32 = 13,							// 32bpp RGBA image stored in raster (not block) order in memory, R is first byte, A is last byte.
		RGB565 = 14,							// 16bpp RGB image stored in raster (not block) order in memory, R at bit position 11
		BGR565 = 15,							// 16bpp RGB image stored in raster (not block) order in memory, R at bit position 0
		RGBA4444 = 16,							// 16bpp RGBA image stored in raster (not block) order in memory, R at bit position 12, A at bit position 0

		TotalTextureFormats = 22,
    }

    [Flags]
    public enum DecodeFlags : UInt32 {
		// PVRTC1: decode non-pow2 ETC1S texture level to the next larger power of 2 (not implemented yet, but we're going to support it). Ignored if the slice's dimensions are already a power of 2.
		PVRTCDecodeToNextPow2 = 2,

		// When decoding to an opaque texture format, if the basis file has alpha, decode the alpha slice instead of the color slice to the output texture format.
		// This is primarily to allow decoding of textures with alpha to multiple ETC1 textures (one for color, another for alpha).
		TranscodeAlphaDataToOpaqueFormats = 4,

		// Forbid usage of BC1 3 color blocks (we don't support BC1 punchthrough alpha yet).
		// This flag is used internally when decoding to BC3.
		BC1ForbidThreeColorBlocks = 8,

		// The output buffer contains alpha endpoint/selector indices. 
		// Used internally when decoding formats like ASTC that require both color and alpha data to be available when transcoding to the output format.
		OutputHasAlphaIndices = 16,

		HighQuality = 32
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ImageInfo
    {
        public UInt32 image_index;
        public UInt32 total_levels;	
        public UInt32 orig_width;
        public UInt32 orig_height;
        public UInt32 width;
        public UInt32 height;
        public UInt32 num_blocks_x;
        public UInt32 num_blocks_y;
        public UInt32 total_blocks;
        public UInt32 first_slice_index;	
        public bool   alpha_flag;		// true if the image has alpha data
        public bool   iframe_flag;		// true if the image is an I-Frame
    };

    [StructLayout(LayoutKind.Sequential)]
    public struct ImageLevelInfo
    {
        public UInt32 image_index;
        public UInt32 level_index;
        public UInt32 orig_width;
        public UInt32 orig_height;
        public UInt32 width;
        public UInt32 height;
        public UInt32 num_blocks_x;
        public UInt32 num_blocks_y;
        public UInt32 total_blocks;
        public UInt32 first_slice_index;	
        public bool   alpha_flag;		// true if the image has alpha data
        public bool   iframe_flag;		// true if the image is an I-Frame
    };

    public unsafe sealed class BasisFile : IDisposable {
        public unsafe sealed class ImageCollection {
            public readonly BasisFile File;

            internal ImageCollection (BasisFile image) {
                File = image;
            }

            public Image this [uint index] {
                get {
                    ImageInfo info;
                    if (Transcoder.GetImageInfo(File.pTranscoder, File.pData, File.DataSize, index, out info) == 0)
                        throw new Exception("Failed to get image info");
                    return new Image(File, index, ref info);
                }
            }
        }

        const string ktx2Magic = "«KTX 20»\r\n\x1A\n";

        private GCHandle DataPin;
        internal IntPtr pTranscoder;
        protected MemoryMappedFile MappedFile { get; private set; }
        protected MemoryMappedViewAccessor MappedView { get; private set; }
        protected MemoryMappedViewStream MappedViewStream { get; private set; }
        protected byte[] Data { get; private set; }
        public uint DataSize { get; private set; }
        public void* pData { get; private set; }
        public bool IsDisposed { get; private set; }
        public bool OwnsStream { get; private set; }

        internal bool IsStarted;

        public readonly ImageCollection Images;

        public BasisFile (string filename)
            : this(File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete), true) {
        }

        public BasisFile (Stream stream, bool ownsStream) {
            DataSize = (uint)stream.Length;
            if (DataSize < ktx2Magic.Length)
                throw new InvalidDataException("Basis file is shorter than the ktx2 magic header");

            if (stream is FileStream fs) {
                // FIXME: Does this inherit the stream position? Does it matter?
                MappedFile = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, !ownsStream);
                MappedView = MappedFile.CreateViewAccessor(0, fs.Length, MemoryMappedFileAccess.Read);
                byte* _pData = null;
                MappedView.SafeMemoryMappedViewHandle.AcquirePointer(ref _pData);
                pData = _pData;
            } else if (stream is MemoryMappedViewStream mmvs) {
                OwnsStream = ownsStream;
                MappedViewStream = mmvs;
                byte* _pData = null;
                mmvs.SafeMemoryMappedViewHandle.AcquirePointer(ref _pData);
                pData = _pData;
            } else {
                Data = new byte[stream.Length];
                try {
                    stream.Read(Data, 0, Data.Length);
                    DataPin = GCHandle.Alloc(Data, GCHandleType.Pinned);
                    pData = DataPin.AddrOfPinnedObject().ToPointer();
                } finally {
                    if (ownsStream)
                        stream.Dispose();
                }
            }

            bool ktx2;
            {
                var magicBuffer = new byte[ktx2Magic.Length];
                var pos = stream.Position;
                stream.Read(magicBuffer, 0, magicBuffer.Length);
                stream.Position = pos;
                var magicChars = new char[magicBuffer.Length];
                for (int i = 0; i < magicBuffer.Length; i++)
                    magicChars[i] = (char)magicBuffer[i];
                ktx2 = (ktx2Magic == new string(magicChars));
            }
            
            pTranscoder = Transcoder.New(ktx2);
            if (pTranscoder == IntPtr.Zero)
                throw new Exception("Failed to create transcoder instance");

            Images = new ImageCollection(this);
        }

        public uint ImageCount {
            get {
                fixed (byte* pData = Data)
                    return Transcoder.GetTotalImages(pTranscoder, pData, DataSize);
            }
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            pData = null;
            if (OwnsStream)
                MappedViewStream?.Dispose();
            MappedView?.Dispose();
            MappedFile?.Dispose();
            if (pTranscoder != default)
                Transcoder.Delete(pTranscoder);
            pTranscoder = default;

            GC.SuppressFinalize(this);
        }

        ~BasisFile () {
            if (pTranscoder != default)
                Transcoder.Delete(pTranscoder);
        }
    }

    public sealed class Image {
        public unsafe sealed class LevelCollection {
            public readonly BasisFile File;
            public readonly Image Image;

            internal LevelCollection (Image image) {
                File = image.File;
                Image = image;
            }

            public ImageLevel this [uint index] {
                get {
                    ImageLevelInfo info;
                    lock (File) {
                        if (Transcoder.GetImageLevelInfo(File.pTranscoder, File.pData, File.DataSize, Image.Index, index, out info) == 0)
                            throw new Exception("Failed to get image level info");
                        return new ImageLevel(Image, index, ref info);
                    }
                }
            }
        }

        public readonly BasisFile File;
        public readonly uint Index;
        public readonly ImageInfo Info;

        public readonly LevelCollection Levels;

        internal Image (BasisFile file, uint index, ref ImageInfo info) {
            File = file;
            Index = index;
            Info = info;
            Levels = new LevelCollection(this);
        }

        public void GetFormatInfo (TranscoderTextureFormats format, out uint bytesPerBlockOrPixel, out bool isBlockTextureFormat) {
            lock (File) {
                bytesPerBlockOrPixel = Transcoder.GetBytesPerBlockOrPixel(format);
                isBlockTextureFormat = Transcoder.IsBlockTextureFormat(format);
            }
        }
    }

    public unsafe sealed class ImageLevel {
        public readonly BasisFile File;
        public readonly Image Image;
        public readonly uint Index;
        public readonly ImageLevelInfo Info;

        internal ImageLevel (Image image, uint index, ref ImageLevelInfo info) {
            File = image.File;
            Image = image;
            Index = index;
            Info = info;
        }

        public uint GetTranscodedSizeInBytes (TranscoderTextureFormats format) {
            uint origWidth, origHeight, totalBlocks;
            lock (File) {
                var blockSize = Transcoder.GetBytesPerBlockOrPixel(format);
                if (Transcoder.GetImageLevelDesc(
                    File.pTranscoder, File.pData, File.DataSize, Image.Index, Index, out origWidth, out origHeight, out totalBlocks
                ) == 0)
                    return 0;
                if (Transcoder.IsBlockTextureFormat(format))
                    return totalBlocks * blockSize;
                else
                    return origWidth * origHeight * blockSize;
            }
        }

        public bool TryTranscode (
            TranscoderTextureFormats format, IntPtr output, int outputSize, DecodeFlags decodeFlags, out ImageLevelInfo levelInfo,
            uint outputRowPitch = 0, uint outputHeightInPixels = 0
        ) {
            lock (File) {
                if (!File.IsStarted) {
                    if (Transcoder.Start(File.pTranscoder, File.pData, File.DataSize) == 0) {
                        levelInfo = default;
                        return false;
                    }
                    File.IsStarted = true;
                }

                if (Transcoder.GetImageLevelInfo(File.pTranscoder, File.pData, File.DataSize, Image.Index, Index, out levelInfo) == 0)
                    return false;

                var blockSize = Transcoder.GetBytesPerBlockOrPixel(format);
                var numBlocks = (uint)(outputSize / blockSize);

                if (Transcoder.TranscodeImageLevel(
                    File.pTranscoder, File.pData, File.DataSize,
                    Image.Index, Index, (void*)output, numBlocks,
                    format, decodeFlags, outputRowPitch, outputHeightInPixels
                ) == 0)
                    return false;
            }

            return true;
        }
    }
}

namespace Squared.Zstd {
    public static class API {
        /// <returns>Number of bytes decompressed, or -1 on error</returns>
        [DllImport(Render.Basis.Transcoder.DllName, CallingConvention = CallingConvention.Cdecl)]
        public static extern unsafe int ZstdDecompress (byte* result, int resultSize, byte* source, int sourceSize);
    }
}
