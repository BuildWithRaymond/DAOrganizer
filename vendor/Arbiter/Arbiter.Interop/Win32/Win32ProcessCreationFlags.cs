
namespace Arbiter.Interop.Win32;

[Flags]
internal enum Win32ProcessCreationFlags : uint
{
    None = 0,
    Suspended = 0x4,
    Detached = 0x8,
}