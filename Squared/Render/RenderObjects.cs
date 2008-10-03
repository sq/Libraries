using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Render;
using Squared.Util;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Squared.Render.Objects {
    public struct RenderPoint : IRenderObject {
        public Vector2 Position;
        public Vector2 TextureOffset;
        public Material Material;
        public float? Radius;

        void IRenderObject.DrawTo (IRenderContextInternal context) {
            var dev = context.Device;
            dev.RenderState.PointSize = Radius.GetValueOrDefault(1.0f);
            using (var e = context.ApplyMaterial(Material)) {
                dev.DrawUserPrimitives<VertexPositionColorTexture>(
                    PrimitiveType.PointList,
                    new VertexPositionColorTexture[1] {
                            new VertexPositionColorTexture(
                                Position.ToXna3D(), 
                                new Color(Material.Colors[0].ToXna()), 
                                TextureOffset.ToXna()
                            )
                        },
                    0,
                    1
                );
            }
        }
    }

    public struct RenderRect : IRenderObject {
        public Vector2 Position;
        public Vector2 Size;
        public Vector2 TextureOffset;
        public Material Material;

        void IRenderObject.DrawTo (IRenderContextInternal context) {
            var dev = context.Device;
            using (var e = context.ApplyMaterial(Material)) {
                var c = new Color(Material.Colors[0].ToXna());
                var sz = Size.ToXna();
                var tl = Position.ToXna3D();
                var tr = tl + new Vector3(sz.X, 0, 0);
                var br = (Position + Size).ToXna3D();
                var bl = tl + new Vector3(0, sz.Y, 0);
                var tex_tl = TextureOffset.ToXna();
                var tex_tr = tex_tl + new Vector2(sz.X, 0);
                var tex_br = tex_tl + sz;
                var tex_bl = tex_tl + new Vector2(0, sz.Y);
                dev.DrawUserPrimitives<VertexPositionColorTexture>(
                    PrimitiveType.TriangleStrip,
                    new VertexPositionColorTexture[4] {
                        new VertexPositionColorTexture(tr, c, tex_tr),
                        new VertexPositionColorTexture(tl, c, tex_tl),
                        new VertexPositionColorTexture(br, c, tex_br),
                        new VertexPositionColorTexture(bl, c, tex_bl),
                    },
                    0,
                    2
                );
            }
        }
    }

    public struct RenderString : IRenderObject {
        public Vector2 Position;
        public string Text;
        public SpriteFont Font;
        public Material Material;

        void IRenderObject.DrawTo (IRenderContextInternal context) {
            SpriteBatch batch = new SpriteBatch(context.Device);
            batch.Begin();
            batch.DrawString(Font, Text, Position.ToXna(), new Color(Material.Colors[0].ToXna()));
            batch.End();
        }
    }
}
