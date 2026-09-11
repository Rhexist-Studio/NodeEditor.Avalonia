namespace NodeEditor.Avalonia.Control;

internal static class WireStyle
{
    public static double CoreThickness = 2.5;
    public static double OutlineMargin = 3;
    public static double OutlineThickness = 1.6;
    public static double OutlineDash = 6;
    public static double OutlineGap = 4;
    public static double OutlinePeriod = 0.55;

    public static double OutlineRadius => CoreThickness / 2 + OutlineMargin;
    public static double OutlineCycle => OutlineDash + OutlineGap;
}
