using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Squared.PRGUI {
    public struct ControlHeader {
        public int ID;
        public Type ControlType;
        public object UserData;

        public ControlHeader (int id, Type type, object userData = null) {
            ID = id;
            ControlType = type;
            UserData = userData;
        }
    }
}
