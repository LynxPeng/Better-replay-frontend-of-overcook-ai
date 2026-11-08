using System;
using UnityEngine;

namespace OvercookedReplay {
public static class ViewerFont {
    public static Font Create() {
        var source=Resources.Load<Font>("Fonts/NotoSansCJKsc-Regular");
        if(!source)throw new InvalidOperationException("Embedded Chinese UI font is missing");
        return UnityEngine.Object.Instantiate(source);
    }
}
}
