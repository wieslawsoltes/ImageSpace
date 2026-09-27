namespace ImageSpace.Core;

/// <summary>Sparse 128x128 RGBA tiles. Snapshots share immutable generations until the first write.</summary>
public sealed class PixelSurface
{
    public const int TileSize=128;
    public const int MaximumDimension=8192;
    public const long MaximumPixels=16_777_216;
    private static long _nextOwner;
    private long _owner=Interlocked.Increment(ref _nextOwner);
    private readonly Dictionary<int,Tile> _tiles;
    private sealed class Tile(long owner,byte[] pixels)
    {
        public long Owner{get;}=owner;
        public byte[] Pixels{get;}=pixels;
        public long Revision{get;set;}
    }
    public int Width{get;}public int Height{get;}public long Revision{get;private set;}
    public int TilesAcross=>(Width+TileSize-1)/TileSize;
    public int AllocatedTiles=>_tiles.Count;
    public long AllocatedBytes=>(long)_tiles.Count*TileSize*TileSize*4;
    public PixelSurface(int width,int height){ValidateSize(width,height);Width=width;Height=height;_tiles=[];}
    private PixelSurface(PixelSurface source){Width=source.Width;Height=source.Height;Revision=source.Revision;_tiles=new(source._tiles);source._owner=Interlocked.Increment(ref _nextOwner);}
    public static void ValidateSize(int width,int height)
    {
        if(width<1||height<1||width>MaximumDimension||height>MaximumDimension||(long)width*height>MaximumPixels)throw new ArgumentOutOfRangeException(nameof(width),"Images must be 1–8192 pixels per side and at most 16 megapixels.");
    }
    public PixelSurface Snapshot()=>new(this);
    public long GetTileRevision(int x,int y)=>_tiles.TryGetValue(y/TileSize*TilesAcross+x/TileSize,out var tile)?tile.Revision:-1;
    public Rgba32 Get(int x,int y)
    {
        if((uint)x>=(uint)Width||(uint)y>=(uint)Height)return Rgba32.Transparent;
        if(!_tiles.TryGetValue(y/TileSize*TilesAcross+x/TileSize,out var tile))return Rgba32.Transparent;
        var i=((y%TileSize)*TileSize+x%TileSize)*4;var p=tile.Pixels;return new(p[i],p[i+1],p[i+2],p[i+3]);
    }
    public void Set(int x,int y,Rgba32 color)
    {
        if((uint)x>=(uint)Width||(uint)y>=(uint)Height)return;
        var key=y/TileSize*TilesAcross+x/TileSize;
        if(!_tiles.TryGetValue(key,out var tile)){if(color.A==0)return;_tiles[key]=tile=new(_owner,new byte[TileSize*TileSize*4]);}
        else if(tile.Owner!=_owner)_tiles[key]=tile=new(_owner,(byte[])tile.Pixels.Clone()){Revision=tile.Revision};
        var i=((y%TileSize)*TileSize+x%TileSize)*4;var p=tile.Pixels;
        if(p[i]==color.R&&p[i+1]==color.G&&p[i+2]==color.B&&p[i+3]==color.A)return;
        p[i]=color.R;p[i+1]=color.G;p[i+2]=color.B;p[i+3]=color.A;Revision++;tile.Revision++;
    }
    public void Fill(Rgba32 color)
    {
        _tiles.Clear();Revision++;if(color.A==0)return;
        for(var y=0;y<Height;y++)for(var x=0;x<Width;x++)Set(x,y,color);
    }
    public IEnumerable<(int X,int Y,ReadOnlyMemory<byte> Pixels,object Identity)> EnumerateTiles()
    {
        foreach(var(key,tile)in _tiles)yield return(key%TilesAcross*TileSize,key/TilesAcross*TileSize,tile.Pixels,tile.Pixels);
    }
    public byte[] ToRgba()
    {
        var output=new byte[checked(Width*Height*4)];foreach(var(tx,ty,pixels,_)in EnumerateTiles())for(var y=0;y<Math.Min(TileSize,Height-ty);y++)pixels.Span.Slice(y*TileSize*4,Math.Min(TileSize,Width-tx)*4).CopyTo(output.AsSpan(((ty+y)*Width+tx)*4));return output;
    }
    public static PixelSurface FromRgba(int width,int height,ReadOnlySpan<byte> bytes)
    {
        var result=new PixelSurface(width,height);if(bytes.Length!=checked(width*height*4))throw new ArgumentException("RGBA byte length does not match dimensions.");
        for(int y=0,i=0;y<height;y++)for(var x=0;x<width;x++,i+=4)if(bytes[i+3]!=0)result.Set(x,y,new(bytes[i],bytes[i+1],bytes[i+2],bytes[i+3]));return result;
    }
}
