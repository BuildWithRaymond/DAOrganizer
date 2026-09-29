using DAOrganizer.Core;
namespace DAOrganizer.App;

public static class DemoCollection
{
    public static void Seed(Organizer app)
    {
        string[] names=["Aisling","Bran","Maeve","Niamh","Ronan","Saoirse"];
        string[] supplies=["Komadium","Red Potion","Exkuranum","Dibenomum","Hemloch"];
        string[] weapons=["Hy-brasyl Sword","Veltain Staff","Ruby Whip","Scimitar","Andor Bow","Snow Secret"];
        string[] armor=["Leather Bliaut","Mythril Boots","Iron Shield","Silk White Dugon","Leather Greaves","Gold Gauntlet"];
        string[] jewels=["Ruby Ring","Gold Earrings","Silver Necklace","Emerald Ring","Beryl Pendant","Jade Ring"];
        string[] materials=["Emerald","Ruby","Wolf Pelt","Beryl","Iron Ore","Silk"];
        for(var n=0;n<names.Length;n++)
        {
            var inventory=supplies.Select((name,i)=>new Item(i+1,name,(n+1)*(i+3))).ToList();
            inventory.AddRange([new(13,weapons[n],1),new(14,armor[n],1),new(15,jewels[n],1),new(25,"Mileth Song",12),new(26,"Blue Tonic",20),new(37,materials[n],24+n*6),new(49,"Apple",8),new(50,"Amusement Park Ticket",2)]);
            inventory.AddRange([new(16,"Mythril Boots",1),new(17,"Iron Shield",1),new(18,"Veltain Staff",1),new(27,"Recall Scroll",8),new(28,"Ability Rune",3),new(29,"Andor Chest",2),new(38,"Ruby",12),new(39,"Wolf Pelt",7),new(40,"Iron Ore",18),new(41,"Silk",6),new(51,"Papaya",15)]);
            app.Store.SaveSnapshot(names[n],"Inventory",inventory,true);
            app.Store.SaveSnapshot(names[n],"Equipment",[new(1,weapons[n],1),new(2,"Leather Armor",1)],true);
            app.Store.SaveSnapshot(names[n],"Bank",[
                new(1,"Emerald",30+n*12),new(2,"Ruby",20+n*4),new(3,"Komadium",60+n*10),new(4,"Red Potion",100+n*20),
                new(5,weapons[(n+1)%6],1),new(6,armor[(n+1)%6],1),new(7,"Recall Scroll",10),new(8,"Ability Rune",5),new(9,"Andor Chest",3),new(10,"Papaya",15)],true);
            app.Store.Put("gold/"+names[n].ToLowerInvariant(),(uint)(1268000+n*58300));
            app.Store.Put("lastUpdate/"+names[n].ToLowerInvariant(),new AccountProgress(names[n],"Complete","Demo collection captured"));
            app.Accounts.SetUpdate(names[n],n<3);
        }
        app.Rules.Set(new(1,"Wolf Pelt",1),ItemAction.AutoDeposit);
        app.Rules.Set(new(1,"Amusement Park Ticket",1),ItemAction.Junk);
    }
}
