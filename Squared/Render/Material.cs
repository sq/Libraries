using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Squared.Render {
    public class Material {
        public Vector4[] Colors;
        public Texture2D[] Textures;

        public static Material FromColors (params Vector4[] colors) {
            return new Material { Colors = colors };
        }

        public static Material FromTextures (params Texture2D[] textures) {
            return new Material { Textures = textures };
        }
    }
}
