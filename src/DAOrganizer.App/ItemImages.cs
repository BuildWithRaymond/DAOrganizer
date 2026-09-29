using System.Runtime.InteropServices;
using Arbiter.Imaging.Sprites;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
namespace DAOrganizer.App;

public sealed class ItemImages:IDisposable
{
    private GameSpriteData? _data;
    private readonly Dictionary<(uint,byte),WriteableBitmap> _atlases=[];
    private readonly Dictionary<(ushort,byte),IImage?> _images=[];
    public async Task Load(string path)=>_data=await Task.Run(()=>GameSpriteDataLoader.Load(Path.GetDirectoryName(path)!));
    public IImage? Get(ushort sprite,byte color)
    {
        try{return GetImage(sprite,color);}
        catch(InvalidDataException){_images[(sprite,color)]=null;return null;}
        catch(ArgumentException){_images[(sprite,color)]=null;return null;}
        catch(IndexOutOfRangeException){_images[(sprite,color)]=null;return null;}
    }
    private IImage? GetImage(ushort sprite,byte color)
    {
        if(_images.TryGetValue((sprite,color),out var image))return image;
        var atlas=_data?.Items?.Find(sprite);if(atlas==null)return null;
        var source=atlas.BuildColorVariant(color);var key=(atlas.FirstItemId,color);
        if(!_atlases.TryGetValue(key,out var bitmap))
        {
            bitmap=new(new(source.Width,source.Height),new(96,96),PixelFormat.Rgba8888,AlphaFormat.Unpremul);
            using(var buffer=bitmap.Lock())
            {
                var pixels=source.Pixels.ToArray();
                for(var row=0;row<source.Height;row++)Marshal.Copy(pixels,row*source.Width*4,buffer.Address+row*buffer.RowBytes,source.Width*4);
            }
            _atlases[key]=bitmap;
        }
        image=source.TryGetFrame((int)(sprite-atlas.FirstItemId),out var r)?new CroppedBitmap(bitmap,new PixelRect(r.X,r.Y,r.Width,r.Height)):null;
        _images[(sprite,color)]=image;return image;
    }
    public void Dispose(){foreach(var bitmap in _atlases.Values)bitmap.Dispose();}
}
