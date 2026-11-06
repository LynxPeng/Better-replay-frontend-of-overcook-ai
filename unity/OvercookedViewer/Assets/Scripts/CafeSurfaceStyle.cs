namespace OvercookedReplay {
// Physical dimensions shared by the one-metre floor tiles and grid counters.
public static class CafeSurfaceStyle {
    public const float JointWidth=.024f;
    public const float CounterWidth=1-JointWidth;
    public const float CounterCornerRadius=.055f;
    public const float CabinetBodyScale=.96f;
    public const float CabinetCornerRadius=CounterCornerRadius-CounterWidth*.5f*(1-CabinetBodyScale);
    public const float TileCornerRadius=.03f;
    public const float TileBevelWidth=.01f;
}
}
