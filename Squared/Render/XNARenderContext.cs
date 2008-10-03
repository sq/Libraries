using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;
using Squared.Render.Objects;
using Microsoft.Xna.Framework.Graphics;
using System.Windows.Forms;
using Microsoft.Xna.Framework;

namespace Squared.Render {
    internal static class XnaExtensionMethods {
        public static Vector4 ToXna (this Vector4 vector) {
            return new Vector4(vector.X, vector.Y, vector.Z, vector.W);
        }

        public static Vector3 ToXna3D (this Vector2 vector) {
            return new Vector3(vector.X, vector.Y, 0);
        }

        public static Vector3 ToXna (this Vector3 vector) {
            return new Vector3(vector.X, vector.Y, vector.Z);
        }

        public static Vector2 ToXna (this Vector2 vector) {
            return new Vector2(vector.X, vector.Y);
        }
    }

    public abstract class XnaRenderContext : IRenderContext, IRenderContextInternal, IGraphicsDeviceService {
        protected struct MaterialDelegate : IDisposable {
            private Effect _Effect;

            public MaterialDelegate (Effect effect) {
                _Effect = effect;
                _Effect.Begin();
                _Effect.Techniques[0].Passes[0].Begin();
            }

            public void Dispose () {
                _Effect.Techniques[0].Passes[0].End();
                _Effect.End();
            }
        }

        protected GraphicsDevice _Device;
        protected GraphicsAdapter _Adapter;
        protected Matrix _ProjectionMatrix;
        protected PresentationParameters _Parameters;
        protected VertexDeclaration _VertexDeclaration;
        protected BasicEffect _Effect;

        protected void CreateDevice (IntPtr windowHandle, PresentationParameters presentationParameters) {
            _Adapter = GraphicsAdapter.DefaultAdapter;

            _Device = new GraphicsDevice(
                _Adapter,
                DeviceType.Hardware,
                windowHandle,
                presentationParameters
            );

            _Parameters = presentationParameters;

            _VertexDeclaration = new VertexDeclaration(_Device, VertexPositionColorTexture.VertexElements);

            _Device.RenderState.CullMode = CullMode.None;

            _Effect = new BasicEffect(_Device, null);
            _Effect.VertexColorEnabled = true;

            ParametersChanged();
        }

        protected void ParametersChanged () {
            _Device.VertexDeclaration = _VertexDeclaration;

            _ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, _Parameters.BackBufferWidth,
                _Parameters.BackBufferHeight, 0,
                0.0f, 1.0f
            );

            _Effect.Projection = _ProjectionMatrix;
        }

        protected IDisposable ApplyMaterial (Material material) {
            _Device.RenderState.SourceBlend = Blend.SourceAlpha;
            _Device.RenderState.DestinationBlend = Blend.InverseSourceAlpha;

            return new MaterialDelegate(_Effect);
        }

        #region IRenderContext

        public void Clear (Vector4 color) {
            _Device.Clear(new Color(color.ToXna()));
        }

        private void DrawRenderPoint (RenderPoint rp) {
        }

        public void Draw (IRenderObject obj) {
            obj.DrawTo(this);
        }

        public void BeginDraw () {
        }

        public void EndDraw () {
            _Device.Present();
        }

        #endregion

        #region IRenderContextInternal

        IDisposable IRenderContextInternal.ApplyMaterial (Material material) {
            return ApplyMaterial(material);
        }

        GraphicsDevice IRenderContextInternal.Device {
            get {
                return _Device;
            }
        }

        #endregion

        #region IGraphicsDeviceService Members

        event EventHandler IGraphicsDeviceService.DeviceCreated {
            add { throw new NotImplementedException(); }
            remove { throw new NotImplementedException(); }
        }

        event EventHandler IGraphicsDeviceService.DeviceDisposing {
            add { throw new NotImplementedException(); }
            remove { throw new NotImplementedException(); }
        }

        event EventHandler IGraphicsDeviceService.DeviceReset {
            add { throw new NotImplementedException(); }
            remove { throw new NotImplementedException(); }
        }

        event EventHandler IGraphicsDeviceService.DeviceResetting {
            add { throw new NotImplementedException(); }
            remove { throw new NotImplementedException(); }
        }

        GraphicsDevice IGraphicsDeviceService.GraphicsDevice {
            get { return _Device; }
        }

        #endregion
    }

    public class XnaWinFormsRenderContext : XnaRenderContext {
        protected Control _Control;

        public XnaWinFormsRenderContext (Control control) {
            _Control = control;
            base.CreateDevice(
                control.Handle, 
                BuildParameters(control)
            );

            control.Resize += new EventHandler(OnControlResize);
        }

        void OnControlResize (object sender, EventArgs e) {
            _Parameters = BuildParameters(_Control);
            _Device.Reset(_Parameters, _Adapter);
            ParametersChanged();
        }

        PresentationParameters BuildParameters (Control control) {
            int width = control.Width;
            int height = control.Height;

            if (control is Form) {
                var f = (Form)control;
                width = f.ClientSize.Width;
                height = f.ClientSize.Height;
            }

            PresentationParameters parms = new PresentationParameters();
            parms.IsFullScreen = false;
            parms.BackBufferCount = 1;
            parms.BackBufferWidth = width;
            parms.BackBufferHeight = height;
            parms.MultiSampleType = MultiSampleType.None;
            return parms;
        }
    }
}
