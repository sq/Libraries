using System;
using Squared.Util;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Squared.Render {
    public interface IRenderContext {
        void BeginDraw ();
        void EndDraw ();

        void Clear (Vector4 color);

        void Draw (IRenderObject obj);
    }

    public interface IRenderContextInternal {
        IDisposable ApplyMaterial (Material material);

        GraphicsDevice Device { get; }
    }

    public interface IRenderObject {
        void DrawTo (IRenderContextInternal context);
    }
}