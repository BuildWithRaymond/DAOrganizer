using Arbiter.IO.Assets;
using Arbiter.Imaging.Formats;
using DAOrganizer.Core;

namespace DAOrganizer.App;

public sealed record PortraitPixels(int Width,int Height,byte[] Pixels);

public sealed class CharacterPortraitRenderer
{
    private const int CropX=17,CropY=21,CropWidth=24,CropHeight=26;
    private readonly DatAssetCatalog _catalog;
    private readonly Dictionary<(char,ushort,char),int> _paletteIds=[];
    private readonly Dictionary<string,Palette?> _palettes=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string,EpfFile?> _sprites=new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<byte,byte[]> _dyes=[];

    private static byte SkinIndex(byte index)=>index switch
    {
        61=>22,62=>24,63=>26,160=>27,161=>28,162=>29,163=>30,
        164=>32,165=>33,166=>34,167=>35,168=>37,169=>38,170=>39,171=>40,
        _=>index
    };

    public CharacterPortraitRenderer(string gameDirectory)
    {
        _catalog=DatAssetCatalog.Load(gameDirectory);
        foreach(var name in _catalog.Names.Where(x=>x.Length==8&&x.StartsWith("pal",StringComparison.OrdinalIgnoreCase)&&x.EndsWith(".tbl",StringComparison.OrdinalIgnoreCase)))
        {
            if(!_catalog.TryGet(name,out var asset))continue;
            using var reader=new StreamReader(asset.OpenRead());
            while(reader.ReadLine() is { } line)
            {
                var fields=line.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
                if(fields.Length is not (2 or 3)||!ushort.TryParse(fields[0],out var sprite)||!int.TryParse(fields[1],out var palette)||palette<0)continue;
                var gender=fields.Length==2?'*':fields[2] switch{"-2"=>'w',"-1"=>'m',_=>'?'};
                if(gender!='?')_paletteIds[(char.ToLowerInvariant(name[3]),sprite,gender)]=palette;
            }
        }
        if(_catalog.TryGet("color0.tbl",out var dyeAsset))
        {
            using var reader=new StreamReader(dyeAsset.OpenRead());
            if(int.TryParse(reader.ReadLine(),out var count)&&count>0&&count<=256)
            {
                while(reader.ReadLine() is { } idLine)
                {
                    if(!byte.TryParse(idLine,out var id))continue;
                    var colors=new byte[count*3];var valid=true;
                    for(var i=0;i<count;i++)
                    {
                        var parts=reader.ReadLine()?.Split(',',StringSplitOptions.TrimEntries);
                        if(parts?.Length!=3||!byte.TryParse(parts[0],out colors[i*3])||!byte.TryParse(parts[1],out colors[i*3+1])||!byte.TryParse(parts[2],out colors[i*3+2]))valid=false;
                    }
                    if(valid)_dyes[id]=colors;
                }
            }
        }
    }

    public PortraitPixels? Render(CharacterAppearance look)
    {
        var gender=look.BodySprite is 0x20 or 0x40 or 0x60 or 0x90 or 0xB0?'w':'m';
        var pixels=new byte[CropWidth*CropHeight*4];var any=false;
        void Draw(char kind,int id,byte dye=0,char? sex=null)
        {
            if(id<=0||id>9999)return;
            var spriteName=$"{sex??gender}{kind}{id:D3}01.epf";
            if(!_sprites.TryGetValue(spriteName,out var epf))
            {
                epf=null;
                if(_catalog.TryGet(spriteName,out var asset))
                {
                    try{using var stream=asset.OpenRead();epf=EpfFile.Load(stream);}
                    catch(InvalidDataException){}
                }
                _sprites[spriteName]=epf;
            }
            // Walk frames 0-4 show the back; frames 5-9 show the face.
            var frame=epf?.Frames.Skip(5).FirstOrDefault(x=>x!=null)??epf?.Frames.FirstOrDefault(x=>x!=null);
            if(frame==null)return;
            var paletteKind=kind switch{'a' or 'j' or 'n' or 'o'=>'b','g'=>'c','f'=>'h','s'=>'w',_=>kind};
            var paletteId=_paletteIds.GetValueOrDefault((paletteKind,(ushort)id,sex??gender),_paletteIds.GetValueOrDefault((paletteKind,(ushort)id,'*')));
            var usesPalm=kind is 'm' or 'o' or 'a' or 'j';
            var paletteName=usesPalm?$"palm{look.SkinColor:D3}.pal":$"pal{paletteKind}{paletteId:D3}.pal";
            if(!_palettes.TryGetValue(paletteName,out var palette))
            {
                palette=null;
                if(!_catalog.TryGet(paletteName,out var asset)&&usesPalm)_catalog.TryGet("palm000.pal",out asset);
                if(asset!=null)
                {
                    try{using var stream=asset.OpenRead();palette=Palette.Load(stream);}
                    catch(InvalidDataException){}
                }
                _palettes[paletteName]=palette;
            }
            if(palette==null)return;
            Span<byte> color=stackalloc byte[4];
            var xOffset=kind is 'c' or 'g' or 'w' or 'p'?-27:0;
            for(var y=0;y<frame.Height;y++)for(var x=0;x<frame.Width;x++)
            {
                var targetX=frame.Left+x+xOffset-CropX;var targetY=frame.Top+y-CropY;
                if((uint)targetX>=CropWidth||(uint)targetY>=CropHeight)continue;
                var index=frame.Pixels.Span[y*frame.Width+x];
                if(index==0)continue;
                var colorIndex=kind is 'm' or 'a' or 'j'?SkinIndex(index):index;
                palette.GetColor(colorIndex,false,color);
                if(color[0]==255&&color[1]==0&&color[2]==255)continue;
                if(dye!=0&&index is >=98 and <=103&&_dyes.TryGetValue(dye,out var colors)&&colors.Length>=18)
                    colors.AsSpan((index-98)*3,3).CopyTo(color);
                var offset=(targetY*CropWidth+targetX)*4;
                color.CopyTo(pixels.AsSpan(offset,4));any=true;
            }
        }
        var headSex=look.HeadSprite==103?'m':gender;
        var body=look.BodySprite is 0x80 or 0x90?5:1;
        Draw('f',look.HeadSprite,look.HairColor,headSex);
        Draw('g',look.Accessory2Sprite,look.Accessory2Color);
        Draw('m',body);
        Draw('o',look.FaceShape,look.SkinColor);
        Draw('h',look.HeadSprite,look.HairColor,headSex);
        if(look.ArmorSprite>999)Draw('i',look.ArmorSprite-999);
        else Draw('u',look.ArmorSprite);
        Draw('e',look.HeadSprite,look.HairColor,headSex);
        if(look.OvercoatSprite>0)Draw('i',look.OvercoatSprite>999?look.OvercoatSprite-999:look.OvercoatSprite);
        if(look.ArmsSprite>999)Draw('j',look.ArmsSprite-999);
        else Draw('a',look.ArmsSprite);
        Draw('c',look.Accessory1Sprite,look.Accessory1Color);
        Draw('c',look.Accessory3Sprite,look.Accessory3Color);
        return any?new PortraitPixels(CropWidth,CropHeight,pixels):null;
    }
}
