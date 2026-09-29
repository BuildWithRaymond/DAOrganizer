
namespace Arbiter.Interop.Win32;

[Flags]
internal enum Win32ProcessAccess : uint
{
    Terminate = 0x1,
    CreateThread = 0x2,
    VmOperation = 0x8,
    VmRead = 0x10,
    VmWrite = 0x20,
    DuplicateHandle = 0x40,
    SetInformation = 0x200,
    QueryInformation = 0x400,
    SuspendResume = 0x800,
}