namespace Arbiter.Interop.Win32;

[Flags]
public enum Win32ProcessAccessFlags
{
    None = 0x0,
    Terminate = 0x01,
    CreateThread = 0x2,
    VmOperation = 0x8,
    VmRead = 0x10,
    VmWrite = 0x20,
    QueryInformation = 0x400
}