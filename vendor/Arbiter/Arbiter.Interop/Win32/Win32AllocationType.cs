
namespace Arbiter.Interop.Win32;

[Flags]
internal enum Win32AllocationType : uint
{
    Commit = 0x1000,
    Reserve = 0x2000,
}