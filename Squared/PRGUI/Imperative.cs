using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.PRGUI.Imperative {
    public struct ContainerBuilder {
        public UIContext Context { get; internal set; }
        public IControlContainer Container { get; internal set; }
    }
}
