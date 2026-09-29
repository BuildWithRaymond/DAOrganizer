namespace DAOrganizer.Core;
public sealed record Item(int Slot,string Name,long Quantity,ushort Sprite=0,byte Color=0,long? Durability=null,long? MaxDurability=null,string Category="Other");
public readonly record struct Tile(int X,int Y);
public sealed record Portal(int From,int X,int Y,int To,int ToX,int ToY,bool WorldMap=false);
public sealed record WorldMap(int Id,string Name,int Width,int Height,IReadOnlyList<Portal> Portals);
public sealed record StoredItem(string Character,string Location,Item Item,DateTimeOffset Updated);
public sealed record CharacterSummary(string Name,string InventoryState,string BankState,DateTimeOffset? LastSeen);
