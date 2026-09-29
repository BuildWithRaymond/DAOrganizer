using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace DAOrganizer.App;

// Inventory moves stay inside this grid; no operating-system drag session is needed.
public sealed class SlotButton:Button
{
    protected override Type StyleKeyOverride=>typeof(Button);
    private Point? _pressed;
    private bool _dragging,_suppressClick;
    public bool CanDrag { get; init; }
    public bool GestureActive=>_pressed!=null;
    public event Action<SlotButton>? DroppedOn;
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if(CanDrag&&e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _pressed=e.GetPosition(TopLevel.GetTopLevel(this));_dragging=false;
        }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if(_pressed is not {} start||!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)return;
        var point=e.GetPosition(TopLevel.GetTopLevel(this));
        if(Math.Abs(point.X-start.X)+Math.Abs(point.Y-start.Y)<8)return;
        _dragging=true;Opacity=.55;Cursor=new Cursor(StandardCursorType.SizeAll);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        var dragged=_dragging;
        var root=TopLevel.GetTopLevel(this);
        var hit=root?.InputHitTest(e.GetPosition(root)) as Visual;
        var target=hit as SlotButton??hit?.FindAncestorOfType<SlotButton>();
        _suppressClick=dragged;
        try{base.OnPointerReleased(e);}finally{Reset();_suppressClick=false;}
        if(dragged&&target!=null&&target!=this)DroppedOn?.Invoke(target);
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e){base.OnPointerCaptureLost(e);Reset();}
    protected override void OnClick(){if(!_suppressClick)base.OnClick();}
    private void Reset(){_pressed=null;_dragging=false;Opacity=1;Cursor=null;}
}
