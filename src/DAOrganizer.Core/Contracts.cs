namespace DAOrganizer.Core;
public sealed record Item(int Slot,string Name,long Quantity,ushort Sprite=0,byte Color=0,long? Durability=null,long? MaxDurability=null,string Category="Other",bool? IsStackable=null);
public readonly record struct Tile(int X,int Y);
public sealed record Portal(int From,int X,int Y,int To,int ToX,int ToY,bool WorldMap=false);
public sealed record WorldMap(int Id,string Name,int Width,int Height,IReadOnlyList<Portal> Portals);
public sealed record StoredItem(string Character,string Location,Item Item,DateTimeOffset Updated);
public sealed record CharacterSummary(string Name,string InventoryState,string BankState,DateTimeOffset? LastSeen);
public enum CoexistencePolicy { Unknown, No, Yes }
public sealed record GameAccount(long Id,string Label,CoexistencePolicy SameAccountCoexistence)
{
    public override string ToString()=>Label;
}
public sealed record StorageRole(long Id,string Character,string Label,int Priority,bool Enabled);
public enum StorageMatchKind { All, Category, Item }
public sealed record StorageRoleRule(long Id,long RoleId,StorageMatchKind MatchKind,string MatchValue,long MinimumToKeep);
public enum Tradeability { Unknown, Tradeable, NonTradeable, EvidenceTradeable, EvidenceNonTradeable }
public enum TradeOutcome { Success, ExplicitRejection, Inconclusive }
public sealed record ItemMetadata(string? CanonicalCategory,bool? Stackable,long? StackLimit,Tradeability Tradeability,string Provenance,string? CommunityCategory=null);
public sealed record ItemOverride(string? Category,string? DestinationCharacter,bool NeverMove);
public sealed record TradeEvidence(Tradeability State,int Successes,int ExplicitRejections,int Inconclusive,DateTimeOffset? LastVerified);
