using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DAOrganizer.Core;
namespace DAOrganizer.App;

// Original vector artwork. No game sprites or third-party icon assets are embedded.
public sealed class CelticSeal:Control
{
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size=Math.Min(Bounds.Width,Bounds.Height);
        using var transform=context.PushTransform(Matrix.CreateScale(size/100,size/100));
        var gold=new SolidColorBrush(Color.Parse("#D6B46A"));
        var dim=new SolidColorBrush(Color.Parse("#64502D"));
        context.DrawEllipse(new SolidColorBrush(Color.Parse("#15130F")),new Pen(dim,1),new Rect(4,4,92,92));
        context.DrawEllipse(null,new Pen(gold,.6),new Rect(9,9,82,82));
        for(var i=0;i<3;i++)
        {
            using var rotation=context.PushTransform(Matrix.CreateTranslation(-50,-50)*Matrix.CreateRotation(i*Math.PI*2/3)*Matrix.CreateTranslation(50,50));
            var knot=Geometry.Parse("M 50,17 C 76,32 82,59 59,71 C 44,78 28,61 36,48 C 43,38 63,39 67,52 C 72,69 54,80 39,70 C 17,55 28,32 50,17 Z");
            context.DrawGeometry(null,new Pen(new SolidColorBrush(Color.Parse("#101113")),5),knot);
            context.DrawGeometry(null,new Pen(gold,1.6),knot);
        }
        foreach(var p in new[]{new Point(50,5),new Point(95,50),new Point(50,95),new Point(5,50)})context.DrawEllipse(gold,null,p,1.5,1.5);
        context.DrawGeometry(gold,null,Geometry.Parse("M50 43 L55 50 L50 57 L45 50 Z"));
    }
}

public sealed class CelticRule:Control
{
    public override void Render(DrawingContext context)
    {
        var pen=new Pen(new SolidColorBrush(Color.Parse("#55462B")),.8);
        var mid=Bounds.Height/2;
        context.DrawLine(pen,new Point(0,mid),new Point(Bounds.Width,mid));
        for(var x=8d;x<Bounds.Width-10;x+=22)
        {
            context.DrawLine(pen,new Point(x-7,mid),new Point(x,mid-4));
            context.DrawLine(pen,new Point(x,mid-4),new Point(x+7,mid));
            context.DrawLine(pen,new Point(x+7,mid),new Point(x,mid+4));
            context.DrawLine(pen,new Point(x,mid+4),new Point(x-7,mid));
        }
    }
}

public sealed class ItemGlyph(Item item):Control
{
    public override void Render(DrawingContext context)
    {
        var size=Math.Min(Bounds.Width,Bounds.Height);
        using var scale=context.PushTransform(Matrix.CreateTranslation((Bounds.Width-size)/2,(Bounds.Height-size)/2)*Matrix.CreateScale(size/48,size/48));
        var gold=new SolidColorBrush(Color.Parse("#D6B46A"));var line=new Pen(gold,1.35);
        var fill=new SolidColorBrush(Color.Parse("#302C23"));
        var category=item.Category=="Other"?ItemCategories.Infer(item.Name):item.Category;
        var family=ItemCategories.Family(item with{Category=category});
        void Shape(string path,IBrush? color=null)=>context.DrawGeometry(color??fill,line,Geometry.Parse(path));
        if(category=="Consumables"&&family=="Potions")
        {
            Shape("M19 7 L29 7 L29 17 C29 21 37 24 37 32 C37 40 31 43 24 43 C17 43 11 40 11 32 C11 24 19 21 19 17 Z");
            var color=item.Name.Contains("Red",StringComparison.OrdinalIgnoreCase)?"#A9424A":item.Name.Contains("Exkuranum",StringComparison.OrdinalIgnoreCase)?"#4476A6":item.Name.Contains("Hemloch",StringComparison.OrdinalIgnoreCase)?"#7F668E":"#47896C";
            context.DrawGeometry(new SolidColorBrush(Color.Parse(color)),null,Geometry.Parse("M14 30 C21 27 29 33 34 29 C38 42 10 44 14 30 Z"));
            context.DrawRectangle(gold,null,new Rect(18,5,12,5),2,2);
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#F2E8D2")),1),new(17,26),new(16,33));
        }
        else if(category=="Weapons"&&family=="Bows")
        {
            context.DrawGeometry(null,new Pen(gold,2.3),Geometry.Parse("M13 5 C42 10 42 38 13 43"));
            context.DrawGeometry(null,line,Geometry.Parse("M13 5 L20 24 L13 43 M8 24 L41 24 M35 19 L42 24 L35 29"));
        }
        else if(category=="Weapons"&&family=="Staves")
        {
            Shape("M21 17 L25 17 L28 44 L23 44 Z");
            context.DrawEllipse(new SolidColorBrush(Color.Parse("#526A91")),line,new Rect(15,3,17,17));
            context.DrawGeometry(null,line,Geometry.Parse("M12 9 L19 21 L29 21 L35 9 M23 4 L26 10 L23 16 L20 10 Z"));
        }
        else if(category=="Weapons"&&family=="Whips")
        {
            context.DrawGeometry(null,new Pen(gold,2),Geometry.Parse("M17 33 C4 8 32 1 36 13 C44 35 16 37 22 17"));
            Shape("M13 31 L20 32 L17 46 L11 45 Z");
        }
        else if(category=="Weapons"&&family=="Secrets & claws")
        {
            Shape("M8 29 L27 10 L31 14 L14 35 Z");Shape("M16 34 L34 16 L38 20 L22 40 Z");
            context.DrawGeometry(null,line,Geometry.Parse("M6 27 L19 41 M12 21 L25 35"));
        }
        else if(category=="Weapons")
        {
            Shape("M31 4 L39 6 L38 14 L20 32 L15 27 Z");
            context.DrawLine(line,new(34,9),new(18,29));
            Shape("M10 26 L13 23 L26 36 L23 39 Z");Shape("M14 32 L19 37 L9 45 L5 41 Z");
            context.DrawEllipse(gold,null,new Point(7,43),2.5,2.5);
        }
        else if(category=="Armor"&&family=="Boots")
        {
            Shape("M11 5 L29 5 L28 27 L40 33 L41 41 L10 41 L9 31 Z");
            context.DrawGeometry(null,line,Geometry.Parse("M12 13 L28 13 M12 21 L28 21 M10 36 L40 36"));
        }
        else if(category=="Armor"&&family=="Body armor")
        {
            Shape("M15 5 L20 10 L28 10 L33 5 L44 17 L35 24 L33 43 L15 43 L13 24 L4 17 Z");
            context.DrawGeometry(null,line,Geometry.Parse("M24 12 L24 40 M14 28 L34 28 M17 17 L31 17"));
        }
        else if(category=="Armor"&&family=="Headwear")
        {
            Shape("M10 29 C8 0 39 0 38 29 L32 41 L16 41 Z");
            Shape("M12 23 L36 23 L32 31 L16 31 Z",new SolidColorBrush(Color.Parse("#101113")));
            context.DrawLine(line,new(24,7),new(24,40));
        }
        else if(category=="Armor")
        {
            Shape("M24 5 L40 12 L38 28 C36 37 28 41 24 44 C20 41 12 37 10 28 L8 12 Z");
            Shape("M24 10 L34 15 L32 28 L24 37 L16 28 L14 15 Z");
            context.DrawLine(line,new(24,13),new(24,32));context.DrawLine(line,new(18,22),new(30,22));
        }
        else if(category=="Accessories")
        {
            context.DrawEllipse(null,new Pen(gold,3),new Rect(12,17,25,25));
            Shape("M24 4 L32 11 L28 21 L20 21 L16 11 Z",new SolidColorBrush(Color.Parse("#497A6D")));
            context.DrawLine(line,new(16,11),new(32,11));context.DrawLine(line,new(24,4),new(24,21));
        }
        else if(category=="Materials"&&family=="Hides & cloth")
        {
            Shape("M15 5 L24 10 L33 5 L38 15 L32 23 L38 37 L31 43 L24 37 L17 43 L10 37 L16 23 L10 15 Z");
            context.DrawGeometry(null,line,Geometry.Parse("M20 14 L19 22 M27 17 L29 27 M22 26 L20 33"));
        }
        else if(category=="Materials")
        {
            Shape("M15 7 L33 7 L42 21 L24 43 L6 21 Z",new SolidColorBrush(Color.Parse(item.Name.Contains("Ruby")?"#743D49":"#35665B")));
            context.DrawLine(line,new(6,21),new(42,21));context.DrawLine(line,new(15,7),new(24,43));context.DrawLine(line,new(33,7),new(24,43));
        }
        else if(category=="Books & scrolls"||family=="Teleport songs")
        {
            Shape("M12 6 L34 6 L37 39 L13 42 L9 10 Z");context.DrawLine(line,new(16,8),new(18,39));
            context.DrawGeometry(null,line,Geometry.Parse("M23 16 L31 16 M23 21 L31 21 M24 26 L30 26"));
        }
        else if(category=="Tools")
        {
            Shape("M7 13 L41 13 L41 20 C34 20 34 28 41 28 L41 35 L7 35 L7 28 C14 28 14 20 7 20 Z");
            context.DrawGeometry(null,line,Geometry.Parse("M29 17 L29 21 M29 24 L29 28 M29 31 L29 33"));
        }
        else
        {
            Shape("M12 8 L36 8 L31 17 C42 28 43 40 33 43 L15 43 C5 40 6 28 17 17 Z");
            context.DrawLine(line,new(15,17),new(33,17));Shape("M24 24 L30 31 L24 38 L18 31 Z");
        }
    }
}
