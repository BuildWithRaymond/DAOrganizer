using DAOrganizer.Core;
namespace DAOrganizer.App;

public static class DemoCollection
{
    // Factual sprite IDs only. Artwork is read from the user's installed game.
    private static readonly Dictionary<string,(ushort Sprite,byte Color)> Sprites=new()
    {
        ["Abominable Greaves"]=(12147,0),
        ["Ancient Dugon"]=(8181,0),
        ["Ancient Virtue Armor"]=(8168,0),
        ["Andor Chest"]=(3235,0),
        ["Andor Ore"]=(1475,0),
        ["Andor Staff"]=(2280,0),
        ["Archery Book"]=(625,0),
        ["Beryl Earrings"]=(232,0),
        ["Blackstar Night Claw"]=(13485,0),
        ["Blackstar Whip"]=(524,0),
        ["Cathonic Shield"]=(1367,0),
        ["Ciad Necklace"]=(936,0),
        ["Daily Quest Token"]=(13057,0),
        ["Dibenomum"]=(51,0),
        ["Double Ability Rune"]=(5323,0),
        ["Dragon Core Ring"]=(3326,0),
        ["Dual Crystal Arrows"]=(2258,0),
        ["Exkuranum"]=(44,0),
        ["Glowing Stone"]=(8229,0),
        ["Gold Jade Necklace"]=(195,0),
        ["Hemloch"]=(57,0),
        ["Komadium"]=(46,0),
        ["Mileth Song"]=(6,0),
        ["Nerve Stimulant"]=(8029,32),
        ["Rambutan"]=(1498,0),
        ["Raw Talgonite"]=(320,0),
        ["Red Potion"]=(44,0),
        ["Red Tonic"]=(2339,0),
        ["Regal Ring"]=(207,0),
        ["Ruby Saber"]=(847,0),
        ["Slate Gauntlet"]=(1587,0),
        ["Sparkle Ring"]=(1358,0),
        ["Spider's Silk"]=(343,0),
        ["Stone Axe"]=(514,0),
        ["Trench Boots"]=(8051,0),
        ["Uncut Beryl"]=(232,0),
        ["Uncut Ruby"]=(233,0),
        ["Vanishing Elixir"]=(3351,15),
        ["Wake Scroll"]=(3305,66),
        ["Wolf's Fur"]=(347,0),
        ["Yumi Bow"]=(2373,0)
    };
    private static Item Item(int slot,string name,int quantity)
    {
        var art=Sprites[name];return new(slot,name,quantity,art.Sprite,art.Color);
    }
    public static void Seed(Organizer app)
    {
        string[] names=["Aisling","Bran","Maeve","Niamh","Ronan","Saoirse"];
        string[] supplies=["Komadium","Red Potion","Exkuranum","Dibenomum","Hemloch"];
        string[] weapons=["Ruby Saber","Andor Staff","Blackstar Whip","Stone Axe","Yumi Bow","Blackstar Night Claw"];
        string[] armor=["Ancient Virtue Armor","Trench Boots","Cathonic Shield","Ancient Dugon","Abominable Greaves","Slate Gauntlet"];
        string[] jewels=["Regal Ring","Beryl Earrings","Ciad Necklace","Sparkle Ring","Gold Jade Necklace","Dragon Core Ring"];
        string[] materials=["Uncut Ruby","Uncut Beryl","Wolf's Fur","Andor Ore","Spider's Silk","Raw Talgonite"];
        for(var n=0;n<names.Length;n++)
        {
            var inventory=supplies.Select((name,i)=>Item(i+1,name,(n+1)*(i+3))).ToList();
            inventory.AddRange([Item(12,"Wake Scroll",1),Item(11,"Vanishing Elixir",1),Item(10,"Nerve Stimulant",1),Item(9,"Glowing Stone",1),
                Item(13,weapons[n],1),Item(14,armor[n],1),Item(15,jewels[n],1),Item(25,"Mileth Song",12),Item(26,"Red Tonic",20),Item(37,materials[n],24+n*6),Item(49,"Rambutan",8),Item(50,"Daily Quest Token",2),
                Item(16,"Trench Boots",1),Item(17,"Cathonic Shield",1),Item(18,"Andor Staff",1),Item(27,"Archery Book",1),Item(28,"Double Ability Rune",3),Item(29,"Andor Chest",2),Item(38,"Uncut Ruby",12),Item(39,"Wolf's Fur",7),Item(40,"Andor Ore",18),Item(41,"Spider's Silk",6)]);
            if(n==1)
            {
                inventory=inventory.Select(x=>x.Slot<=5?x with{Slot=x.Slot+1}:x).ToList();
                inventory.Add(Item(1,"Dual Crystal Arrows",1));
            }
            app.Store.SaveSnapshot(names[n],"Inventory",inventory,true);
            app.Store.SaveSnapshot(names[n],"Equipment",[Item(1,weapons[n],1),Item(2,"Ancient Virtue Armor",1)],true);
            app.Store.SaveSnapshot(names[n],"Bank",[
                Item(1,"Uncut Beryl",30+n*12),Item(2,"Uncut Ruby",20+n*4),Item(3,"Komadium",60+n*10),Item(4,"Red Potion",100+n*20),
                Item(5,weapons[(n+1)%6],1),Item(6,armor[(n+1)%6],1),Item(7,"Archery Book",1),Item(8,"Double Ability Rune",5),Item(9,"Andor Chest",3),Item(10,"Rambutan",15)],true);
            app.Store.Put("appearance/"+names[n].ToLowerInvariant(),new CharacterAppearance(
                (ushort)(n+1),1,(byte)(n%2==0?0x20:0x10),0,0,0,(ushort)(n+1),0,0,0,0,0,0,0));
            app.Store.Put("gold/"+names[n].ToLowerInvariant(),(uint)(1268000+n*58300));
            app.Store.Put("lastUpdate/"+names[n].ToLowerInvariant(),new AccountProgress(names[n],"Complete","Demo collection captured"));
            app.Accounts.SetUpdate(names[n],n<3);
        }
        app.Rules.Set(Item(1,"Wolf's Fur",1),ItemAction.AutoDeposit);
        app.Rules.Set(Item(1,"Daily Quest Token",1),ItemAction.Junk);
    }
}
