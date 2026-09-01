namespace AdminForge.Tools;

/// <summary>
/// Marker type used to hand this assembly to the tool scanner. Nothing else in the
/// application needs a reference to any individual tool, which is what keeps tools
/// additive: a new class in this project is discovered with no registration step.
/// </summary>
public static class ToolsAssembly;
