using System.Text;
using DAOrganizer.App;
using DAOrganizer.Core;
using Xunit;

namespace DAOrganizer.Tests;

public class PortraitTests
{
    [Fact]
    public void FaceSkipsMagentaPaletteMarker()
    {
        var folder=Path.Combine(Path.GetTempPath(),"da-portrait-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Archive(Path.Combine(folder,"test.dat"),new Dictionary<string,byte[]>
            {
                ["mo00101.epf"]=Epf(25,28,10,10),["palm000.pal"]=PaletteAt(10,255,0,255)
            });
            Assert.Null(new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(0,1,0x10,0,0,0,0,0,0,0,0,0,0,0)));
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact]
    public void RearHairLayerUsesHeadPaletteTable()
    {
        var folder=Path.Combine(Path.GetTempPath(),"da-portrait-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Archive(Path.Combine(folder,"test.dat"),new Dictionary<string,byte[]>
            {
                ["mf00101.epf"]=Epf(25,28),["palh.tbl"]=Encoding.ASCII.GetBytes("1 5\n"),
                ["palh005.pal"]=PaletteAt(2,30,150,70),["palf000.pal"]=PaletteAt(2,210,20,20)
            });
            var result=new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(1,0,0x10,0,0,0,0,0,0,0,0,0,0,0));
            Assert.NotNull(result);
            Assert.Equal(new byte[]{30,150,70,255},result.Pixels.AsSpan((7*result.Width+8)*4,4).ToArray());
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact]
    public void BodyUsesPalmSkinAndRemapsBlueFillIndex()
    {
        var folder=Path.Combine(Path.GetTempPath(),"da-portrait-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var skin=PaletteAt(22,180,110,75);skin[61*3]=10;skin[61*3+1]=20;skin[61*3+2]=220;
            Archive(Path.Combine(folder,"test.dat"),new Dictionary<string,byte[]>
            {
                ["mm00101.epf"]=Epf(25,28,61,61),["palm001.pal"]=skin
            });
            var result=new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(0,0,0x10,0,1,0,0,0,0,0,0,0,0,0));
            Assert.NotNull(result);
            Assert.Equal(new byte[]{180,110,75,255},result.Pixels.AsSpan((7*result.Width+8)*4,4).ToArray());
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact]
    public void FaceUsesPalmPaletteForSelectedSkinColor()
    {
        var folder=Path.Combine(Path.GetTempPath(),"da-portrait-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Archive(Path.Combine(folder,"test.dat"),new Dictionary<string,byte[]>
            {
                ["mo00101.epf"]=Epf(25,28,16,16),
                ["palm000.pal"]=PaletteAt(16,240,190,140),
                ["palm001.pal"]=PaletteAt(16,120,70,45),
                ["palb000.pal"]=PaletteAt(16,0,0,255)
            });
            var result=new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(0,1,0x10,0,1,0,0,0,0,0,0,0,0,0));
            Assert.NotNull(result);
            Assert.Equal(new byte[]{120,70,45,255},result.Pixels.AsSpan((7*result.Width+8)*4,4).ToArray());
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact]
    public void AccessoryUsesGameOffsetToReachFaceCrop()
    {
        var folder=Path.Combine(Path.GetTempPath(),"da-portrait-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Archive(Path.Combine(folder,"test.dat"),new Dictionary<string,byte[]>
            {
                ["mc00101.epf"]=Epf(52,28),["palc000.pal"]=Palette(255,0,0)
            });
            var result=new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(0,0,0x10,0,0,0,0,0,1,0,0,0,0,0));
            Assert.NotNull(result);
            Assert.Equal(new byte[]{220,180,20,255},result.Pixels.AsSpan((7*result.Width+8)*4,4).ToArray());
        }
        finally{Directory.Delete(folder,true);}
    }
    [Fact]
    public void InstalledGamePortraitUsesRealAssetsWhenAvailable()
    {
        var folder=Environment.GetEnvironmentVariable("DAORGANIZER_GAME_DATA");
        if(string.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder))return;
        var portrait=new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(1,1,0x10,0,0,0,0,0,0,0,0,0,0,0));
        Assert.NotNull(portrait);
        Assert.True(portrait.Pixels.Where((_,index)=>index%4==3).Count(alpha=>alpha!=0)>40);
        var alternateSkin=new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(1,1,0x10,0,1,0,0,0,0,0,0,0,0,0));
        Assert.NotNull(alternateSkin);
        Assert.False(portrait.Pixels.SequenceEqual(alternateSkin.Pixels));
    }
    [Fact]
    public void HeadLayerUsesMappedPaletteAndDrawsAboveFace()
    {
        var folder=Path.Combine(Path.GetTempPath(),"da-portrait-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            Archive(Path.Combine(folder,"test.dat"),new Dictionary<string,byte[]>
            {
                ["mh00101.epf"]=Epf(25,28),["mo00101.epf"]=Epf(25,28),
                ["palh.tbl"]=Encoding.ASCII.GetBytes("1 5\n"),
                ["palh000.pal"]=Palette(255,0,0),["palh005.pal"]=Palette(0,0,255),
                ["palb000.pal"]=Palette(0,255,0)
            });
            var result=new CharacterPortraitRenderer(folder).Render(new CharacterAppearance(1,1,0x10,0,0,0,0,0,0,0,0,0,0,0));
            Assert.NotNull(result);
            Assert.Equal((24,26),(result.Width,result.Height));
            Assert.Equal(new byte[]{220,180,20,255},result.Pixels.AsSpan((7*result.Width+8)*4,4).ToArray());
        }
        finally{Directory.Delete(folder,true);}
    }

    private static byte[] Epf(short x,short y,byte north=1,byte south=2)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write((short)10);writer.Write((short)1);writer.Write((short)1);writer.Write((short)0);writer.Write(10u);
        for(var i=0;i<10;i++)writer.Write(i<5?north:south);
        for(var i=0;i<10;i++){writer.Write(y);writer.Write(x);writer.Write((short)(y+1));writer.Write((short)(x+1));writer.Write((uint)i);writer.Write((uint)(i+1));}
        return stream.ToArray();
    }
    private static byte[] Palette(byte red,byte green,byte blue)
    {
        var data=new byte[256*3];data[3]=red;data[4]=green;data[5]=blue;data[6]=220;data[7]=180;data[8]=20;return data;
    }
    private static byte[] PaletteAt(int index,byte red,byte green,byte blue)
    {
        var data=new byte[256*3];data[index*3]=red;data[index*3+1]=green;data[index*3+2]=blue;return data;
    }
    private static void Archive(string path,IReadOnlyDictionary<string,byte[]> entries)
    {
        using var stream=File.Create(path);using var writer=new BinaryWriter(stream);
        writer.Write((uint)(entries.Count+1));
        var offset=4+(entries.Count+1)*17;
        foreach(var entry in entries)
        {
            writer.Write((uint)offset);var name=new byte[13];Encoding.ASCII.GetBytes(entry.Key).CopyTo(name,0);writer.Write(name);offset+=entry.Value.Length;
        }
        writer.Write((uint)offset);writer.Write(new byte[13]);
        foreach(var entry in entries)writer.Write(entry.Value);
    }
}
