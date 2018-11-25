using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework.Graphics;

namespace Squared.Render {
    public class ColorLUT : IDisposable {
        public readonly Texture2D Texture;
        public readonly bool OwnsTexture;
        public readonly int SlicesX, SlicesY;

        public bool IsDisposed { get; private set; }

        public ColorLUT (
            Texture2D texture, int slicesX, int slicesY, bool ownsTexture = true
        ) {
            Texture = texture;
            if (Texture.Width != 64)
                throw new ArgumentException("texture.Width");
            if (Texture.Height != 64)
                throw new ArgumentException("texture.Height");
            OwnsTexture = ownsTexture;
            SlicesX = slicesX;
            SlicesY = slicesY;
            if (SlicesX != 4)
                throw new ArgumentException("slicesX");
            if (SlicesY != 4)
                throw new ArgumentException("slicesY");
        }

        public static implicit operator Texture2D (ColorLUT lut) {
            return lut.Texture;
        }

        public void Dispose () {
            if (IsDisposed)
                return;

            IsDisposed = true;
            if (OwnsTexture)
                Texture.Dispose();
        }
    }
}
