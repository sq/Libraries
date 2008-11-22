using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Squared.Util;
using System.Windows.Forms;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;

namespace Squared.Render {
    public abstract class XnaRenderContextBase : IXnaRenderContext {
        protected GraphicsDevice _Device;
        protected GraphicsAdapter _Adapter;
        protected Matrix _ProjectionMatrix;
        protected PresentationParameters _Parameters;
        protected VertexElement[] _VertexElements;
        protected VertexDeclaration _VertexDeclaration;

        public event Action ParametersChanged;

        protected void CreateDevice (IntPtr windowHandle, PresentationParameters presentationParameters) {
            _Adapter = GraphicsAdapter.DefaultAdapter;

            _Device = new GraphicsDevice(
                _Adapter,
                DeviceType.Hardware,
                windowHandle,
                presentationParameters
            );

            _Parameters = presentationParameters;

            OnParametersChanged();
        }

        private void OnParametersChanged () {
            if (_VertexElements != null)
                VertexFormat = _VertexElements;

            _ProjectionMatrix = Matrix.CreateOrthographicOffCenter(
                0, _Parameters.BackBufferWidth,
                _Parameters.BackBufferHeight, 0,
                0.0f, 1.0f
            );

            if (ParametersChanged != null)
                ParametersChanged();
        }

        public PresentationParameters PresentationParameters {
            get {
                return _Parameters;
            }

            set {
                _Parameters = value;
                _Device.Reset(value, _Adapter);
            }
        }

        public Matrix ProjectionMatrix {
            get {
                return _ProjectionMatrix;
            }
        }

        public GraphicsDevice Device {
            get {
                return _Device;
            }
        }

        public VertexElement[] VertexFormat {
            get {
                return _VertexElements;
            }
            set {
                _VertexElements = value;

                if (_VertexDeclaration != null)
                    _VertexDeclaration.Dispose();

                if (_Device != null) {
                    _VertexDeclaration = new VertexDeclaration(_Device, _VertexElements);
                    _Device.VertexDeclaration = _VertexDeclaration;
                }
            }
        }

        public VertexDeclaration VertexDeclaration {
            get {
                return _VertexDeclaration;
            }
        }

        public void Dispose () {
            _Device.Dispose();
            _Adapter.Dispose();
        }
    }

    public class XnaWinFormsRenderContext : XnaRenderContextBase {
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
            PresentationParameters = BuildParameters(_Control);
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
