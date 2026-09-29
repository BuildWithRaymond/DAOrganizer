using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Arbiter.Interop.Process;
namespace DAOrganizer.Game;

public static class ClientLauncher
{
    public const string DefaultClient=@"C:\Program Files (x86)\KRU\DATester\Darkages.exe";
    public const string SupportedHash="054A5D6ADC56099C6BFD9D2A58675AFF62DC788B63209A3D906492F5B89E96C6";
    public static void Validate(string path)
    {
        if(!string.Equals(Path.GetFileName(path),"Darkages.exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Choose the supported Darkages.exe.");
        using var file=File.OpenRead(path);
        if(Convert.ToHexString(SHA256.HashData(file))!=SupportedHash)throw new InvalidDataException("Unsupported client build. Select the original DATester 7.41 executable.");
    }
    public static int Launch(string path,int port)
    {
        Validate(path);
        using var file=File.OpenRead(path);using var pe=new PEReader(file);
        byte[] Original(int address,int count)
        {
            var rva=address-0x400000;
            return pe.GetSectionData(rva).GetContent(0,count).ToArray();
        }
        using var process=SuspendedProcess.Start(path,workingDirectory:Path.GetDirectoryName(Path.GetFullPath(path)));
        try
        {
            using var memory=process.GetProcessMemoryStream();using var allocator=process.GetProcessMemoryAllocator();
            var host=allocator.AllocMemory(stream=>stream.Write(System.Text.Encoding.ASCII.GetBytes("127.0.0.1\0")));
            void Patch(int address,byte[] replacement)
            {
                memory.Position=address;var actual=new byte[replacement.Length];memory.ReadExactly(actual);
                if(!actual.SequenceEqual(Original(address,replacement.Length)))throw new InvalidDataException($"Client patch mismatch at {address:X}.");
                memory.Position=address;memory.Write(replacement);
            }
            Patch(0x57A7CE,[0x31,0xC0,0x90,0x90,0x90,0x90]);
            Patch(0x42E61F,[0x83,0xFA,0,0x90,0x90,0x90]);
            Patch(0x4B897C,[0xEB,0x6C]);
            Patch(0x4B8ACF,[0xEB,0x6D]);
            Patch(0x433392,BitConverter.GetBytes((uint)host));
            Patch(0x565628,BitConverter.GetBytes((uint)host));
            Patch(0x4333E3,[0xBA,(byte)port,(byte)(port>>8),0,0]);
            Patch(0x4333C3,[0x6A,1,0x6A,0,0x6A,0,0x6A,127]);
            process.Resume();return process.ProcessId;
        }
        catch
        {
            // Only the new suspended Darkages.exe created above is owned by this launcher.
            if(process.IsSuspended)process.Kill(1);
            throw;
        }
    }
}
