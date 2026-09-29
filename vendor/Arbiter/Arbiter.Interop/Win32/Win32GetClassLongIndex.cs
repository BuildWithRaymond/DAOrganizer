namespace Arbiter.Interop.Win32;

internal enum Win32GetClassLongIndex : int
{
    Atom = -32,
    ClassExtraSize = -20,
    WindowExtraSize = -18,
    BackgroundBrushHandle = -10,
    CursorHandle = -12,
    IconHandle = -14,
    SmallIconHandle = -34,
    ModuleHandle = -16,
    MenuName = -8,
    WindowStyle = -26,
    WindowProc = -24
}