using System;
using Squared.Util;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using System.Reflection;

namespace Squared.Render {
    public interface IXnaRenderContext : IDisposable {
        GraphicsDevice Device { get; }
        PresentationParameters PresentationParameters { get; set; }
        Matrix ProjectionMatrix { get; }
        VertexElement[] VertexFormat { get; set; }
    }

    public static class XnaRenderContextExtensionMethods {
        public static void SetVertexType (this IXnaRenderContext context, Type type) {
            var info = type.GetField("VertexElements");
            if (info == null)
                throw new ArgumentException("Specified type is not a vertex format", "type");

            var elements = (VertexElement[])info.GetValue(null);

            context.VertexFormat = elements;
        }
    }

    public class RenderContextDeviceService : IGraphicsDeviceService {
        IXnaRenderContext _Context;

        public RenderContextDeviceService (IXnaRenderContext context) {
            _Context = context;
        }

        public event EventHandler DeviceCreated;
        public event EventHandler DeviceDisposing;
        public event EventHandler DeviceReset;
        public event EventHandler DeviceResetting;

        public GraphicsDevice GraphicsDevice {
            get { return _Context.Device; }
        }
    }
}