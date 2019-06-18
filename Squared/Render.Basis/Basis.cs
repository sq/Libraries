using System;
using System.Collections.Generic;
using System.IO;
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

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr New ();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern void Delete (IntPtr transcoder);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int Start (IntPtr transcoder, void * pData, UInt32 dataSize);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern UInt32 GetTotalImages (IntPtr transcoder, void * pData, UInt32 dataSize);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetImageInfo (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, out ImageInfo result
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetImageLevelInfo (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, UInt32 levelIndex, out ImageLevelInfo result
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int GetImageLevelDesc (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, UInt32 levelIndex,
            out UInt32 originalWidth, out UInt32 originalHeight, out UInt32 totalBlocks
        );

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern UInt32 GetBytesPerBlock (TextureFormats format);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
        public static extern int TranscodeImageLevel (
            IntPtr transcoder, void * pData, UInt32 dataSize, 
            UInt32 imageIndex, UInt32 levelIndex,
            void * pOutput, UInt32 outputSizeInBlocks,
            TextureFormats format, DecodeFlags decodeFlags
        );
    }

    public enum BlockFormats : UInt32 {
        ETC1, // ETC1S RGB 
        BC1, // DXT1 RGB 
        BC4, // DXT5A (alpha block only)
        PVRTC1_4_OPAQUE_ONLY, // opaque only PVRTC1 4bpp
        BC7_M6_OPAQUE_ONLY, // RGB BC7 mode 6
        ETC2_EAC_A8, // alpha block of ETC2 EAC (first 8 bytes of the 16-bit ETC2 EAC RGBA format)
    }

    public enum TextureFormats : UInt32 {
        ETC1,
        BC1,
        BC4,
        PVRTC1_4_OPAQUE_ONLY,
        BC7_M6_OPAQUE_ONLY,
        ETC2, // ETC2_EAC_A8 block followed by a ETC1 block
        BC3, // BC4 followed by a BC1 block
        BC5, // two BC4 blocks
    }

    [Flags]
    public enum DecodeFlags : UInt32 {
        /// <summary>
        /// PVRTC1: texture will use wrap addressing vs. clamp (most PVRTC viewer tools assume wrap addressing, so we default to wrap although that can cause edge artifacts)
        /// </summary>
        PVRTCWrapAddressing = 1,	
                        
        /// <summary>
        /// PVRTC1: decode non-pow2 ETC1S texture level to the next larger power of 2 (not implemented yet, but we're going to support it). Ignored if the slice's dimensions are already a power of 2.
        /// </summary>
        PVRTCDecodeToNextPow2 = 2,	
            
        /// <summary>
        /// When decoding to an opaque texture format, if the basis file has alpha, decode the alpha slice instead of the color slice to the output texture format
        /// </summary>
        TranscodeAlphaDataToOpaqueFormats = 4,

        /// <summary>
        /// Forbid usage of BC1 3 color blocks (we don't support BC1 punchthrough alpha yet).
        /// </summary>
        BC1ForbidThreeColorBlocks = 8
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

    public unsafe class BasisFile : IDisposable {
        public unsafe class ImageCollection {
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

        private GCHandle DataPin;
        internal IntPtr pTranscoder;
        public byte[] Data { get; private set; }
        public uint DataSize { get; private set; }
        public void* pData { get; private set; }
        public bool IsDisposed { get; private set; }

        internal bool IsStarted;

        public readonly ImageCollection Images;

        public BasisFile (string filename)
            : this(File.OpenRead(filename), true) {
        }

        public BasisFile (Stream stream, bool ownsStream) {
            Data = new byte[stream.Length];
            DataSize = (uint)stream.Length;
            try {
                stream.Read(Data, 0, Data.Length);
            } finally {
                if (ownsStream)
                    stream.Dispose();
            }

            DataPin = GCHandle.Alloc(Data, GCHandleType.Pinned);
            pData = DataPin.AddrOfPinnedObject().ToPointer();

            pTranscoder = Transcoder.New();
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
            Transcoder.Delete(pTranscoder);
        }
    }

    public class Image {
        public unsafe class LevelCollection {
            public readonly BasisFile File;
            public readonly Image Image;

            internal LevelCollection (Image image) {
                File = image.File;
                Image = image;
            }

            public ImageLevel this [uint index] {
                get {
                    ImageLevelInfo info;
                    if (Transcoder.GetImageLevelInfo(File.pTranscoder, File.pData, File.DataSize, Image.Index, index, out info) == 0)
                        throw new Exception("Failed to get image level info");
                    return new ImageLevel(Image, index, ref info);
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
    }

    public unsafe class ImageLevel {
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

        public uint GetTranscodedSizeInBytes (TextureFormats format) {
            uint origWidth, origHeight, totalBlocks;
            var blockSize = Transcoder.GetBytesPerBlock(format);
            if (Transcoder.GetImageLevelDesc(
                File.pTranscoder, File.pData, File.DataSize, Image.Index, Index, out origWidth, out origHeight, out totalBlocks
            ) == 0)
                return 0;

            if (format == TextureFormats.PVRTC1_4_OPAQUE_ONLY)
                throw new NotImplementedException();

            return totalBlocks * blockSize;
        }

        public bool TryTranscode (
            TextureFormats format, ArraySegment<byte> output, DecodeFlags decodeFlags
        ) {
            if (!File.IsStarted) {
                if (Transcoder.Start(File.pTranscoder, File.pData, File.DataSize) == 0)
                    return false;
                File.IsStarted = true;
            }

            var blockSize = Transcoder.GetBytesPerBlock(format);
            var numBlocks = (uint)(output.Count / blockSize);

            fixed (byte* pBuffer = output.Array) {
                var pOutput = pBuffer + output.Offset;
                if (Transcoder.TranscodeImageLevel(
                    File.pTranscoder, File.pData, File.DataSize,
                    Image.Index, Index, pOutput, numBlocks,
                    format, decodeFlags
                ) == 0)
                    return false;
            }

            return true;
        }
    }
}
