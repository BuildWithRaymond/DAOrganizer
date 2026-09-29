using System.Text.RegularExpressions;
namespace DAOrganizer.Core;

public static class ItemCategories
{
    public static readonly string[] Names=["Weapons","Armor","Accessories","Consumables","Materials","Books & scrolls","Tools","Cosmetics","Pets","Other"];
    // Selected game names and families checked against Vorlof, 2026-09-29.
    // Local classification rules, not a complete item database. See docs/RESEARCH.md.
    private static readonly (string Category,string Family,string Names)[] NamedGroups=[
        ("Consumables","Potions","Komadium|Red Potion|Exkuranum|Dibenomum|Hemloch|Satchel of Hemloch|Kurum|Sposen|Carvien|fior srad|fior athar|fior sal|fior creag"),
        ("Consumables","Food & drink","Papaya|Apricot|Pafrica|Rambutan|Banas|Tangerines|Tentacle|Starfish Arm|Green Tentacle|Red Tentacle|Cheese|Spices|Liver|Purple Whopper|Bee's Honey|Pure Honey|Royal Honey|Raw Honey|Mold|Small Mold|Herring|Sardine|Anchovy|Belladonna|Baguette|Mouldy Baguette|Marinade|Frosting|Broth|Flour|Salt"),
        ("Consumables","Mass items","Anarchist's Tome|Ceannlaidir's Valor|Embrace of Glioca|Deoch's Flame|Gramail's Spirit|Fiosachd's Gift|Luathas' Spectacles|Footstep's of Cail"),
        ("Weapons","Swords","Eppe|Scimitar|Cutlass"),
        ("Weapons","Daggers","Dirk"),
        ("Weapons","Staves","Magus Ares|Magus Zeus|Magus Gaea|Magus Diana|Magus Kronos|Magus Apollo|Assassin's Cross|Dark Star"),
        ("Weapons","Secrets & claws","Snow Secret|Center Secret|Blossom Secret|Moon Secret|Light Secret|Sun Secret|Lotus Secret"),
        ("Armor","Headwear","Mushroom Cap|Death Knell|Nagetier Jaw"),
        ("Armor","Body armor","Nagetier Cloak|Aosdic Pattern Walker"),
        ("Armor","Gauntlets & bracers","Lockpicks"),
        ("Armor","Belts","Ancusa ceir"),
        ("Cosmetics","Effects","Heart Effect|Broken Heart Display|Fairy Dust|Legendary Shine")];
    private static readonly Dictionary<string,(string Category,string Family)> Known=NamedGroups
        .SelectMany(g=>g.Names.Split('|').Select(name=>(Name:name,g.Category,g.Family)))
        .ToDictionary(x=>x.Name,x=>(x.Category,x.Family),StringComparer.OrdinalIgnoreCase);

    // Specific wearable/tool names precede generic material/food words.
    private static readonly (string Category,string Family,string Pattern)[] Patterns=[
        ("Tools","Tickets & passes",@"\b(tickets?|passes?)\b"),
        ("Tools","Keys",@"\bkeys?\b"),
        ("Tools","Maps",@"\bmaps?\b"),
        ("Tools","Gathering tools",@"\b(pickaxe|shovel|fishing rod)\b"),
        ("Pets","Pets",@"\b(pet|ducklings|chicks|puppy|buddy)\b"),
        ("Cosmetics","Effects",@"\b(effect|sparkles|display)\b"),
        ("Cosmetics","Costumes",@"\b(costume|spectacles|face|beard|wings|cloak)\b"),
        ("Armor","Headwear",@"\b(helm|helmet|hat|cap|toque|wimple|dugon)\b"),
        ("Armor","Body armor",@"\b(armor|armour|coat|overcoat|robe|mantle|uniform|bliaut|cuirass|cotehardie|hauberk|talma|aiquil|skirt|benusta|stoller|yainar|dobok|quilas|kano|cotte|brigandine|corsette|jekin|kanon)\b"),
        ("Armor","Shields",@"\bshield\b"),
        ("Armor","Gauntlets & bracers",@"\b(gauntlets?|bracers?)\b"),
        ("Armor","Greaves",@"\b(greaves|shinguards|buskins)\b"),
        ("Armor","Boots",@"\b(boots?|shoes)\b"),
        ("Armor","Belts",@"\b(belt|cincture|sash)\b"),
        ("Accessories","Necklaces",@"\b(necklace|pendant|torc)\b"),
        ("Accessories","Earrings",@"\bearrings?\b"),
        ("Accessories","Rings",@"\b(ring|band)\b"),
        ("Weapons","Swords",@"\b(sword|blade|saber|claidhmore|kindjal|escalon)\b"),
        ("Weapons","Staves",@"\b(staff|staves|wand|stick)\b"),
        ("Weapons","Spears",@"\b(spear|harpoon)\b"),
        ("Weapons","Daggers",@"\bdagger\b"),
        ("Weapons","Axes",@"\baxe\b"),
        ("Weapons","Bows",@"\bbow\b"),
        ("Weapons","Arrows",@"\barrows?\b"),
        ("Weapons","Secrets & claws",@"\bclaw\b"),
        ("Weapons","Maces",@"\bmace\b"),
        ("Weapons","Whips",@"\bwhip\b"),
        ("Consumables","Potions",@"\b(potion|tonic|hitonic|extonic|deum|lotion)\b"),
        ("Consumables","Teleport songs",@"\b(song|recall scroll)\b"),
        ("Consumables","Bonuses & runes",@"\b(experience|ability|exp|bonus|rune|exp-ap|expap)\b"),
        ("Consumables","Chests & gifts",@"\b(chest|treasure bag|gift|satchel of goods)\b"),
        ("Consumables","Food & drink",@"\b(wine|tea|apple|mushroom|food|bread|grapes|fruit|lobster|sandwich|steak|beef|chicken|turkey|cake|pie|soup|tomato|strawberry|bananas|candy|egg|trout|bass|fish|meat|honey|chocolate|lollipops|rum|brandy|cherry|vegetable|beer|ale)\b"),
        ("Books & scrolls","Books",@"\b(book|tome)\b"),
        ("Books & scrolls","Scrolls",@"\bscroll\b"),
        ("Books & scrolls","Recipes & notes",@"\b(plan|recipe|note)\b"),
        ("Materials","Gems: ruby",@"\bruby\b"),
        ("Materials","Gems: emerald",@"\bemerald\b"),
        ("Materials","Gems: beryl",@"\bberyl\b"),
        ("Materials","Gems",@"\b(gem|gemstone|sapphire|amethyst|diamond|coral|pearl|jade|spinel|lapis)\b"),
        ("Materials","Ores & stones",@"\b(ore|rock|stone)\b"),
        ("Materials","Hides & cloth",@"\b(pelt|fur|silk|leather|hide|cloth)\b"),
        ("Materials","Monster parts",@"\b(feather|tentacle|skull|hair|bone|scale|venom|sac|ear|spine|eye)\b"),
        ("Materials","Crafting supplies",@"\bwax\b")];
    private static readonly (string Category,string Family,Regex Pattern)[] Rules=Patterns
        .Select(x=>(x.Category,x.Family,new Regex(x.Pattern,RegexOptions.IgnoreCase|RegexOptions.CultureInvariant|RegexOptions.Compiled))).ToArray();

    public static string Infer(string name)=>Known.TryGetValue(name.Trim(),out var known)?known.Category:
        Rules.FirstOrDefault(x=>x.Pattern.IsMatch(name)).Category??"Other";
    public static string Family(Item item)
    {
        if(Known.TryGetValue(item.Name.Trim(),out var known)&&known.Category==item.Category)return known.Family;
        return Rules.FirstOrDefault(x=>x.Category==item.Category&&x.Pattern.IsMatch(item.Name)).Family??"General";
    }
    public static string Label(Item item)=>Family(item) is var family&&family!="General"?$"{item.Category} · {family}":item.Category;
    public static string Equipment(int slot)=>slot switch{1=>"Weapons",>=2 and <=4 or >=9 and <=13=>"Armor",>=5 and <=8=>"Accessories",>=14 and <=18=>"Cosmetics",_=>"Other"};
    public static int Order(string category){var index=Array.IndexOf(Names,category);return index<0?Names.Length-1:index==Names.Length-1?Names.Length:index;}
}
