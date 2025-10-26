using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Squared.Render.Resources;
using Squared.Render.Text;
using Squared.Threading;

namespace Squared.Render {
    public class FreeTypeFontProvider : ResourceProvider<FreeTypeFont> {
        public FreeTypeFontFormat DefaultFormat;
        // FIXME: Leaked forever
        private readonly List<Stream> RetainedStreams = new ();

        public FreeTypeFontProvider (Assembly assembly, RenderCoordinator coordinator) 
            : this(new EmbeddedResourceStreamProvider(assembly), coordinator) {
        }

        public FreeTypeFontProvider (IResourceProviderStreamSource provider, RenderCoordinator coordinator) 
            // FIXME: Enable threaded create?
            : base(provider, coordinator, enableThreadedCreate: false, enableThreadedPreload: true) {
        }

        protected override Future<FreeTypeFont> CreateInstance (string name, Stream stream, object data, object preloadedData, bool async) {
            // FIXME
            var font = new FreeTypeFont(Coordinator, stream, ownsStream: true) {
                Format = DefaultFormat,
            };
            if (font.BaseStream != null)
                RetainedStreams.Add(font.BaseStream);
            var f = new Future<FreeTypeFont>(font);
            return f;
        }

        protected override void DisposeStream (Stream stream) {
            if (RetainedStreams.Contains(stream))
                return;
            else
                base.DisposeStream(stream);
        }
    }
}
