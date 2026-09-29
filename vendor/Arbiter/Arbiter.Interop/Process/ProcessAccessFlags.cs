
namespace Arbiter.Interop.Process;

[Flags]
public enum ProcessAccessFlags : uint
{
    None = 0,
    Read = 0x10,
    Write = 0x20,
    ReadWrite = Read | Write,
}