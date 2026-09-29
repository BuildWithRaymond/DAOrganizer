
namespace Arbiter.Interop.Win32;

[Flags]
internal enum Win32FreeType : uint
{
    Decommit = 0x4000,
    Release = 0x8000,
}