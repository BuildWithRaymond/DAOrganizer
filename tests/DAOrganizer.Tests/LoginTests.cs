using DAOrganizer.Core;
using Xunit;
namespace DAOrganizer.Tests;
public class LoginTests
{
    [Theory]
    [InlineData(0x09,0x0F,false)]
    [InlineData(0x08,0x0E,false)]
    [InlineData(0x0D,0x1C,false)]
    [InlineData(0x23,0x4F,true)]
    [InlineData(0x25,0x4B,true)]
    public void NativeKeysSupplyHardwareScanCodes(int key,int scan,bool extended)
    {
        foreach(var release in new[]{false,true})
        {
            var method=typeof(DAOrganizer.Game.NativeInput).GetMethod("CreateKeyInput",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!;
            var input=method.Invoke(null,[(ushort)key,release])!;var type=input.GetType();
            Assert.Equal((ushort)scan,(ushort)type.GetField("Scan")!.GetValue(input)!);
            Assert.Equal(8u|(extended?1u:0u)|(release?2u:0u),(uint)type.GetField("Flags")!.GetValue(input)!);
            Assert.Equal((ushort)0,(ushort)type.GetField("Key")!.GetValue(input)!);
        }
    }
    [Fact]public void ReplacingPasswordPreservesNativeLoginMetadata()
    {
        byte[] original=[3,(byte)'B',(byte)'o',(byte)'b',3,(byte)'1',(byte)'2',(byte)'3',9,8,7,6,5,4,3,2];
        var replaced=LoginPayload.ReplacePassword(original,"Bob","123","secret");
        Assert.Equal(original[^8..],replaced[^8..]);Assert.Equal(6,replaced[4]);
        Assert.Equal("secret",System.Text.Encoding.ASCII.GetString(replaced,5,6));
    }
    [Fact]public void AllSixteenNativeMetadataBytesSurvivePasswordReplacement()
    {
        byte[] metadata=[12,34,56,78,90,12,34,56,78,90,12,34,56,78,1,0];
        byte[] original=[3,(byte)'B',(byte)'o',(byte)'b',8,..System.Text.Encoding.ASCII.GetBytes("12345678"),..metadata];
        var replaced=LoginPayload.ReplacePassword(original,"Bob","12345678","secret");
        Assert.Equal(metadata,replaced[^16..]);
        Assert.Equal("secret",System.Text.Encoding.ASCII.GetString(replaced,5,6));
    }
    [Fact]public void WrongLoginTicketCannotUseSavedPassword()
    {
        byte[] original=[3,(byte)'B',(byte)'o',(byte)'b',3,(byte)'1',(byte)'2',(byte)'3',9,8,7,6,5,4,3,2];
        Assert.Throws<InvalidDataException>(()=>LoginPayload.ReplacePassword(original,"Alice","123","secret"));
    }
}
