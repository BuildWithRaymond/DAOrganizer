using System.Text;
namespace DAOrganizer.Core;
public static class LoginPayload
{
    public static byte[] ReplacePassword(byte[] original,string name,string ticket,string password)
    {
        if(original.Length<2)throw new InvalidDataException("Incomplete login packet.");
        var nameLength=original[0];var passwordOffset=1+nameLength;
        if(original.Length<=passwordOffset)throw new InvalidDataException("Incomplete login name.");
        var passwordLength=original[passwordOffset];var suffix=passwordOffset+1+passwordLength;
        if(original.Length-suffix<8)throw new InvalidDataException("Incomplete login metadata.");
        if(!Encoding.ASCII.GetString(original,1,nameLength).Equals(name,StringComparison.OrdinalIgnoreCase)||Encoding.ASCII.GetString(original,passwordOffset+1,passwordLength)!=ticket)
            throw new InvalidDataException("Saved login ticket does not match this request.");
        if(password.Length is <1 or >255||password.Any(x=>x>127))throw new InvalidDataException("Password must contain 1–255 ASCII characters.");
        using var output=new MemoryStream();output.Write(original,0,passwordOffset);output.WriteByte((byte)password.Length);
        output.Write(Encoding.ASCII.GetBytes(password));output.Write(original,suffix,original.Length-suffix);return output.ToArray();
    }
}
