using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Assembly_CSharp.TasInfo.mm.Source {
    [Flags]
    public enum TasInfoFlags {
        None = 0,
        SetFFUnsafe = 1,
        SetFFSafe = 2,
        IsFFUnsafe = 4
    }
}
