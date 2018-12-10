using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Squared.Render.STB {
    public unsafe class Image : IDisposable {
        public void* Handle { get; private set; }

        public void Dispose () {
            if (Handle != null)

            throw new NotImplementedException();
        }
    }

    public enum ImagePrecision {
        Default = 0,
        Byte = 8,
        UInt16 = 16
    }

    public enum ImageChannels {
        Default = 0,
        Grey = 1,
        GreyAlpha = 2,
        RGB = 3,
        RGBA = 4
    }
}
