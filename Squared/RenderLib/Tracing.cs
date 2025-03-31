// Some contents Copyright (c) 2010-2013 SharpDX - Alexandre Mutel
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Squared.Render.Tracing {
    public interface INameableGraphicsObject {
    }

    public static class ObjectNames {
        public static readonly ConditionalWeakTable<object, string> Table = 
            new ConditionalWeakTable<object, string>();

        public static void SetName (this Microsoft.Xna.Framework.Graphics.GraphicsResource obj, string name) {
            obj.Name = name;
            SetName((object)obj, name);
        }

        public static void SetName (this INameableGraphicsObject obj, string name) {
            SetName((object)obj, name);
        }

        public static void SetName (object obj, string name) {
            lock (Table) {
                Table.Remove(obj);
                Table.Add(obj, name);
            }
        }

        public static bool TryGetName (this INameableGraphicsObject obj, out string result) {
            lock (Table)
                return Table.TryGetValue(obj, out result);
        }

        public static bool TryGetName (object obj, out string result) {
            lock (Table)
                return Table.TryGetValue(obj, out result);
        }

        public static string ToObjectID (this INameableGraphicsObject obj) {
            return ToObjectID((object)obj);
        }

        public static string ToObjectID (object obj) {
            string result;
            if (TryGetName(obj, out result))
                return result;
            else
                return string.Format("{0:X4}", obj.GetHashCode());
        }
    }

    public static class D3D9 {
        /// <summary>
        /// Marks the beginning of a user-defined event. PIX can use this event to trigger an action.
        /// </summary>
        /// <param name="color">The Event color.</param>
        /// <param name="name">The Event Name.</param>
        /// <returns>The zero-based level of the hierarchy that this event is starting in. If an error occurs, the return value will be negative.</returns>
        /// <unmanaged>D3DPERF_BeginEvent</unmanaged>
        public static int BeginEvent (int color, string name) {
            return D3DPERF_BeginEvent(color, name);
        }

        /// <summary>
        /// Marks the beginning of a user-defined event. PIX can use this event to trigger an action.
        /// </summary>
        /// <param name="color">The Event color.</param>
        /// <param name="name">The Event formatted Name.</param>
        /// <param name="parameters">The parameters to use for the formatted name.</param>
        /// <returns>
        /// The zero-based level of the hierarchy that this event is starting in. If an error occurs, the return value will be negative.
        /// </returns>
        /// <unmanaged>D3DPERF_BeginEvent</unmanaged>
        public static int BeginEvent (int color, string name, params object[] parameters) {
            return D3DPERF_BeginEvent(color, string.Format(name, parameters));
        }

        /// <summary>
        /// Mark the end of a user-defined event. PIX can use this event to trigger an action.
        /// </summary>
        /// <returns>The level of the hierarchy in which the event is ending. If an error occurs, this value is negative.</returns>
        /// <unmanaged>D3DPERF_EndEvent</unmanaged>
        public static int EndEvent () {
            return D3DPERF_EndEvent();
        }

        /// <summary>
        /// Mark an instantaneous event. PIX can use this event to trigger an action.
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="name">The name.</param>
        /// <unmanaged>D3DPERF_SetMarker</unmanaged>
        public static void SetMarker (int color, string name) {
            D3DPERF_SetMarker(color, name);
        }

        /// <summary>
        /// Mark an instantaneous event. PIX can use this event to trigger an action.
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="name">The name to format.</param>
        /// <param name="parameters">The parameters to use to format the name.</param>
        /// <unmanaged>D3DPERF_SetMarker</unmanaged>
        public static void SetMarker (int color, string name, params object[] parameters) {
            D3DPERF_SetMarker(color, string.Format(name, parameters));
        }

        /// <summary>
        /// Set this to false to notify PIX that the target program does not give permission to be profiled.
        /// </summary>
        /// <param name="enableFlag">if set to <c>true</c> PIX profiling is authorized. Default to true.</param>
        /// <unmanaged>D3DPERF_SetOptions</unmanaged>
        public static void AllowProfiling (bool enableFlag) {
            D3DPERF_SetOptions(enableFlag ? 0 : 1);
        }

        /// <summary>
        /// Gets a value indicating whether this instance is currently profiled by PIX.
        /// </summary>
        /// <value>
        /// <c>true</c> if this instance is currently profiled; otherwise, <c>false</c>.
        /// </value>
        /// <unmanaged>D3DPERF_GetStatus</unmanaged>
        public static bool IsCurrentlyProfiled {
            get {
                return D3DPERF_GetStatus() != 0;
            }
        }

        [DllImport("d3d9.dll", EntryPoint = "D3DPERF_BeginEvent", CharSet = CharSet.Unicode)]
        private extern static int D3DPERF_BeginEvent (int color, string name);

        [DllImport("d3d9.dll", EntryPoint = "D3DPERF_EndEvent", CharSet = CharSet.Unicode)]
        private extern static int D3DPERF_EndEvent ();

        [DllImport("d3d9.dll", EntryPoint = "D3DPERF_SetMarker", CharSet = CharSet.Unicode)]
        private extern static void D3DPERF_SetMarker (int color, string wszName);

        [DllImport("d3d9.dll", EntryPoint = "D3DPERF_SetOptions", CharSet = CharSet.Unicode)]
        private extern static void D3DPERF_SetOptions (int options);

        [DllImport("d3d9.dll", EntryPoint = "D3DPERF_GetStatus", CharSet = CharSet.Unicode)]
        private extern static int D3DPERF_GetStatus ();
    }

    internal sealed class MarkerBatch : Batch {
        public readonly string Text;

        // HACK: For the allocator stuff
        public MarkerBatch () {
        }

        public MarkerBatch (int layer, string text) {
            Layer = layer;
            Text = text;

            State.IsInitialized = true;
        }

        protected override void Prepare (PrepareManager manager) {
        }

        public override void Issue(DeviceManager manager) {
            base.Issue(manager);

            RenderTrace.ImmediateMarker(manager.Device, Text);
        }
    }

    public static class RenderDoc {
        [DllImport("renderdoc.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int RENDERDOC_GetAPI (Version version, out IntPtr apiPointers);

        public enum Version : int {
          eRENDERDOC_API_Version_1_0_0 = 10000,    // RENDERDOC_API_1_0_0 = 1 00 00
          eRENDERDOC_API_Version_1_0_1 = 10001,    // RENDERDOC_API_1_0_1 = 1 00 01
          eRENDERDOC_API_Version_1_0_2 = 10002,    // RENDERDOC_API_1_0_2 = 1 00 02
          eRENDERDOC_API_Version_1_1_0 = 10100,    // RENDERDOC_API_1_1_0 = 1 01 00
          eRENDERDOC_API_Version_1_1_1 = 10101,    // RENDERDOC_API_1_1_1 = 1 01 01
          eRENDERDOC_API_Version_1_1_2 = 10102,    // RENDERDOC_API_1_1_2 = 1 01 02
          eRENDERDOC_API_Version_1_2_0 = 10200,    // RENDERDOC_API_1_2_0 = 1 02 00
          eRENDERDOC_API_Version_1_3_0 = 10300,    // RENDERDOC_API_1_3_0 = 1 03 00
          eRENDERDOC_API_Version_1_4_0 = 10400,    // RENDERDOC_API_1_4_0 = 1 04 00
          eRENDERDOC_API_Version_1_4_1 = 10401,    // RENDERDOC_API_1_4_1 = 1 04 01
        };

        private static IntPtr? _API;

        public static IntPtr API {
            get {
                // FIXME: Thread safety
                if (!_API.HasValue) {
                    // HACK: This call will always fail under the debugger since RenderDoc can't *also* be attached, unless
                    //  you attached a debugger after starting the app under renderdoc, which is kind of ridiculous.
                    // Anyway, we don't want to annoy the developer with spurious DllNotFoundExceptions every run, do we?
                    // If for some reason you want to attach a debugger after attaching renderdoc, use the --rendertrace command line argument
                    if (Debugger.IsAttached)
                        _API = IntPtr.Zero;
                    else
                        try {
                            RENDERDOC_GetAPI(Version.eRENDERDOC_API_Version_1_0_0, out IntPtr temp);
                            _API = temp;
                        } catch (DllNotFoundException) {
                            _API = IntPtr.Zero;
                        }
                }
                return _API.Value;
            }
        }
    }

    public enum RenderTraceDetailLevel : int {
        Silent = 0,
        Concise = 1,
        Verbose = 2
    }

    public static class RenderTrace {
        public static RenderTraceDetailLevel DetailLevel = RenderTraceDetailLevel.Silent;

        private static volatile int TracingBroken = 0;

        private static bool? _EnableTracing;

        public static bool EnableTracing {
            get {
                // FIXME: Thread safety
                if (!_EnableTracing.HasValue) {
                    var ca = Environment.GetCommandLineArgs().FirstOrDefault(arg => arg.StartsWith("--rendertrace", StringComparison.OrdinalIgnoreCase));
                    var explicitlyEnabled = !string.IsNullOrWhiteSpace(ca);
                    if (Squared.Threading.Profiling.Superluminal.Enabled)
                        // This may clear the enabled flag if loading it fails
                        Squared.Threading.Profiling.Superluminal.LoadAPI();
                    _EnableTracing = explicitlyEnabled || (RenderDoc.API != IntPtr.Zero) || Squared.Threading.Profiling.Superluminal.Enabled;
                    var equalsLocation = ca?.IndexOf("=") ?? 0;
                    if (equalsLocation > 0) {
                        var detailLevel = ca.Substring(equalsLocation + 1);
                        if (int.TryParse(detailLevel, out int i))
                            DetailLevel = (RenderTraceDetailLevel)i;
                        else if (!Enum.TryParse(detailLevel, out DetailLevel)) {
                            var msg = $"Invalid tracing detail level '{detailLevel}'";
                            Console.Error.WriteLine(msg);
                            Debug.WriteLine(msg);
                        }
                    }
                }

                return _EnableTracing.Value && (TracingBroken == 0);
            }
            set {
                // You're the boss
                _EnableTracing = true;
            }
        }

        public static void BeforeFrame () {
        }

        public static void Marker<T1> (IBatchContainer container, int layer, string format, T1 value1) {
            if (!EnableTracing)
                return;

            Marker(container, layer, String.Format(format, value1));
        }

        public static void Marker<T1, T2> (IBatchContainer container, int layer, string format, T1 value1, T2 value2) {
            if (!EnableTracing)
                return;

            Marker(container, layer, String.Format(format, value1, value2));
        }

        public static void Marker<T1, T2, T3> (IBatchContainer container, int layer, string format, T1 value1, T2 value2, T3 value3) {
            if (!EnableTracing)
                return;

            Marker(container, layer, String.Format(format, value1, value2, value3));
        }

        public static void Marker<T1, T2, T3, T4> (IBatchContainer container, int layer, string format, T1 value1, T2 value2, T3 value3, T4 value4) {
            if (!EnableTracing)
                return;

            Marker(container, layer, String.Format(format, value1, value2, value3, value4));
        }

        public static void Marker (IBatchContainer container, int layer, string format, params object[] values) {
            if (!EnableTracing)
                return;

            Marker(container, layer, String.Format(format, values));
        }

        public static void Marker (IBatchContainer container, int layer, string name) {
            if (!EnableTracing)
                return;

            var batch = new MarkerBatch(layer, name);
            container.Add(batch);
        }

        public static void ImmediateMarker<T1> (Microsoft.Xna.Framework.Graphics.GraphicsDevice device, string format, T1 value1) {
            if (!EnableTracing)
                return;

            ImmediateMarker(device, String.Format(format, value1));
        }

        public static void ImmediateMarker<T1, T2> (Microsoft.Xna.Framework.Graphics.GraphicsDevice device, string format, T1 value1, T2 value2) {
            if (!EnableTracing)
                return;

            ImmediateMarker(device, String.Format(format, value1, value2));
        }

        public static void ImmediateMarker<T1, T2, T3> (Microsoft.Xna.Framework.Graphics.GraphicsDevice device, string format, T1 value1, T2 value2, T3 value3) {
            if (!EnableTracing)
                return;

            ImmediateMarker(device, String.Format(format, value1, value2, value3));
        }

        public static void ImmediateMarker<T1, T2, T3, T4> (Microsoft.Xna.Framework.Graphics.GraphicsDevice device, string format, T1 value1, T2 value2, T3 value3, T4 value4) {
            if (!EnableTracing)
                return;

            ImmediateMarker(device, String.Format(format, value1, value2, value3, value4));
        }

        public static void ImmediateMarker (Microsoft.Xna.Framework.Graphics.GraphicsDevice device, string format, params object[] values) {
            if (!EnableTracing)
                return;

            ImmediateMarker(device, String.Format(format, values));
        }

        public static void ImmediateMarker (Microsoft.Xna.Framework.Graphics.GraphicsDevice device, string name) {
            if (!EnableTracing)
                return;

            try {
                device.SetStringMarkerEXT(name);
            } catch (Exception exc) {
                Console.WriteLine("Render tracing disabled: {0}", exc);
                TracingBroken = 1;
            }
        }
    }
}
