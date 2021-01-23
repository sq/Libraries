using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Squared.Render.Text;

namespace Squared.Render {
    public class EmbeddedFreeTypeFontProvider : EmbeddedResourceProvider<FreeTypeFont> {
        public EmbeddedFreeTypeFontProvider (Assembly assembly, RenderCoordinator coordinator) 
            : base(assembly, coordinator) {
        }

        public EmbeddedFreeTypeFontProvider (RenderCoordinator coordinator) 
            : base(Assembly.GetCallingAssembly(), coordinator) {
        }

        protected override FreeTypeFont CreateInstance (Stream stream, object data, object preloadedData) {
            return new FreeTypeFont(Coordinator, stream);
        }
    }
}
