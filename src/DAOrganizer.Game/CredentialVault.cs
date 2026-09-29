using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace DAOrganizer.Game;

public static class CredentialVault
{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags,Type;public string TargetName;public string? Comment;public long LastWritten;
        public uint CredentialBlobSize;public IntPtr CredentialBlob;public uint Persist,AttributeCount;
        public IntPtr Attributes;public string? TargetAlias;public string UserName;
    }
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool CredWrite(ref Credential credential,uint flags);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool CredRead(string target,uint type,uint flags,out IntPtr credential);
    [DllImport("advapi32.dll")]private static extern void CredFree(IntPtr credential);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool CredDelete(string target,uint type,uint flags);
    private static string Target(string name)=>"DAOrganizer/da0.kru.com/"+name.ToLowerInvariant();
    public static void Save(string name,string password)
    {
        var bytes=Encoding.Unicode.GetBytes(password);var pointer=Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes,0,pointer,bytes.Length);
            var credential=new Credential{Type=1,TargetName=Target(name),CredentialBlob=pointer,CredentialBlobSize=(uint)bytes.Length,Persist=2,UserName=name};
            if(!CredWrite(ref credential,0))throw new Win32Exception();
        }
        finally{for(var i=0;i<bytes.Length;i++)Marshal.WriteByte(pointer,i,0);Marshal.FreeHGlobal(pointer);Array.Clear(bytes);}
    }
    public static string? Read(string name)
    {
        if(!CredRead(Target(name),1,0,out var pointer))return null;
        try{var c=Marshal.PtrToStructure<Credential>(pointer);return Marshal.PtrToStringUni(c.CredentialBlob,(int)c.CredentialBlobSize/2);}
        finally{CredFree(pointer);}
    }
    public static bool Exists(string name)
    {
        if(!CredRead(Target(name),1,0,out var pointer))return false;
        CredFree(pointer);return true;
    }
    public static void Remove(string name)
    {
        if(!CredDelete(Target(name),1,0)&&Marshal.GetLastWin32Error()!=1168)throw new Win32Exception();
    }
}
