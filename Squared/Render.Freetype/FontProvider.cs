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
        public FreeTypeFontProvider (Assembly assembly, RenderCoordinator coordinator) 
            : this(new EmbeddedResourceStreamProvider(assembly), coordinator) {
        }

        public FreeTypeFontProvider (IResourceProviderStreamSource provider, RenderCoordinator coordinator) 
            // FIXME: Enable threaded create?
            : base(provider, coordinator, enableThreadedCreate: false, enableThreadedPreload: true) {
        }

        protected override Future<FreeTypeFont> CreateInstance (Stream stream, object data, object preloadedData, bool async) {
            // FIXME
            var f = new Future<FreeTypeFont>(new FreeTypeFont(Coordinator, stream));
            return f;
        }
    }
}
