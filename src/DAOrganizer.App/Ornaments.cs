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
