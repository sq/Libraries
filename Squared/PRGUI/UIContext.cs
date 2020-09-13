using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Squared.PRGUI.Layout;

namespace Squared.PRGUI {
    public class UIContext : IDisposable {
        public readonly LayoutContext Layout = new LayoutContext();

        public void Update () {
            Layout.Update();
        }

        public void Dispose () {
            Layout.Dispose();
        }
    }
}
