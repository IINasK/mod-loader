using System;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class GeneralScrollBar : Control {
    int total,page,value,drag=-1,remainder;
    internal Action<int> Changed;
    internal int WheelUnit=1;
    internal int Value {get{return value;}}
    internal int Limit {get{return Math.Max(0,total-page);}}
    internal GeneralScrollBar(){Width=14;TabStop=true;Cursor=Cursors.Hand;AccessibleRole=AccessibleRole.ScrollBar;AccessibleName=GeneralStyle.T("Rolagem","Scroll");SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    internal void Configure(int count,int visible,int position){
        count=Math.Max(0,count);visible=Math.Max(1,visible);position=Math.Max(0,Math.Min(Math.Max(0,count-visible),position));
        if(total==count&&page==visible&&value==position)return;total=count;page=visible;value=position;Invalidate();
    }
    internal Rectangle Thumb {
        get{int height=Math.Max(0,Height-8),length=Limit==0?height:Math.Min(height,Math.Max(28,(int)(height*(double)page/Math.Max(1,total))));return new Rectangle(4,4+(Limit==0?0:(int)((height-length)*(double)value/Limit)),Math.Max(3,Width-8),length);}
    }
    internal void MoveTo(int next){next=Math.Max(0,Math.Min(Limit,next));if(next==value)return;value=next;Invalidate();if(Changed!=null)Changed(next);}
    internal void Wheel(int delta){if(SystemInformation.MouseWheelScrollLines==0)return;remainder+=delta;int steps=remainder/SystemInformation.MouseWheelScrollDelta;remainder-=steps*SystemInformation.MouseWheelScrollDelta;MoveTo(value-steps*(SystemInformation.MouseWheelScrollLines<0?page:SystemInformation.MouseWheelScrollLines*WheelUnit));}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(GeneralStyle.Surface);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;if(Limit==0)return;using(var path=GeneralStyle.Round(Thumb,4))using(var brush=new SolidBrush(Capture||Focused?GeneralStyle.Accent:GeneralStyle.Muted))e.Graphics.FillPath(brush,path);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();if(Thumb.Contains(e.Location)){drag=e.Y-Thumb.Top;Capture=true;}else MoveTo(value+(e.Y<Thumb.Top?-page:page));}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(drag<0||!Capture)return;int travel=Math.Max(1,Height-8-Thumb.Height);MoveTo((int)Math.Round((e.Y-drag-4)*(double)Limit/travel));}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);drag=-1;Capture=false;Invalidate();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)drag=-1;}
    protected override void OnMouseWheel(MouseEventArgs e){Wheel(e.Delta);}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override AccessibleObject CreateAccessibilityInstance(){return new ScrollAccess(this);}
    sealed class ScrollAccess : ControlAccessibleObject {
        readonly GeneralScrollBar owner;
        internal ScrollAccess(GeneralScrollBar owner):base(owner){this.owner=owner;}
        public override string Value {get{return owner.Value.ToString();}set{int position;if(Int32.TryParse(value,out position))owner.MoveTo(position);}}
        public override string Description {get{return GeneralStyle.T("Posição de rolagem: ","Scroll position: ")+owner.Value+" / "+owner.Limit;}}
    }
    protected override bool IsInputKey(Keys key){return key==Keys.Up||key==Keys.Down||key==Keys.PageUp||key==Keys.PageDown||key==Keys.Home||key==Keys.End||base.IsInputKey(key);}
    protected override void OnKeyDown(KeyEventArgs e){base.OnKeyDown(e);if(e.KeyCode==Keys.Up)MoveTo(value-1);else if(e.KeyCode==Keys.Down)MoveTo(value+1);else if(e.KeyCode==Keys.PageUp)MoveTo(value-page);else if(e.KeyCode==Keys.PageDown)MoveTo(value+page);else if(e.KeyCode==Keys.Home)MoveTo(0);else if(e.KeyCode==Keys.End)MoveTo(Limit);else return;e.Handled=true;}
}

internal sealed class GeneralScrollView : Panel {
    readonly Panel viewport=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};
    readonly GeneralScrollBar bar=new GeneralScrollBar{Dock=DockStyle.Right,Margin=Padding.Empty};
    readonly Control content;
    readonly bool panelContent,reserveBarSpace,fitPanelContent;
    NativeEvents events;
    bool queued,refreshing;
    internal Control Content {get{return content;}}
    internal GeneralScrollBar Bar {get{return bar;}}
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr first,IntPtr second);
    internal GeneralScrollView(Control content,bool panelContent=false,bool reserveBarSpace=false,bool fitPanelContent=false){
        this.panelContent=panelContent;this.reserveBarSpace=reserveBarSpace;this.fitPanelContent=fitPanelContent;
        this.content=content;Dock=DockStyle.Fill;Margin=Padding.Empty;Padding=Padding.Empty;BackColor=GeneralStyle.Surface;
        content.Dock=DockStyle.None;content.Margin=Padding.Empty;viewport.Controls.Add(content);Controls.Add(viewport);Controls.Add(bar);
        var text=content as TextBox;if(text!=null&&text.Multiline){text.ScrollBars=ScrollBars.None;text.WordWrap=true;}
        var grid=content as DataGridView;if(grid!=null){grid.ScrollBars=ScrollBars.None;grid.Scroll+=(s,e)=>Queue();grid.RowsAdded+=(s,e)=>Queue();grid.RowsRemoved+=(s,e)=>Queue();}
        var list=content as ListBox;if(list!=null){list.HorizontalScrollbar=false;list.IntegralHeight=false;}
        bar.Changed=ScrollTo;content.HandleCreated+=(s,e)=>Attach();content.HandleDestroyed+=(s,e)=>{if(events!=null)events.ReleaseHandle();};content.TextChanged+=(s,e)=>Queue();content.FontChanged+=(s,e)=>Queue();content.Resize+=(s,e)=>Queue();
        if(fitPanelContent)content.Layout+=(s,e)=>{if(!refreshing)Queue();};
        viewport.Resize+=(s,e)=>RefreshScroll();HandleCreated+=(s,e)=>Attach();if(content.IsHandleCreated)Attach();
    }
    void Attach(){if(!content.IsHandleCreated)return;if(events!=null)events.ReleaseHandle();events=new NativeEvents(this,content.Handle);Queue();}
    void Queue(){if(queued||IsDisposed||Disposing||!IsHandleCreated)return;queued=true;BeginInvoke((Action)(()=>{queued=false;if(!IsDisposed&&!Disposing&&IsHandleCreated)RefreshScroll();}));}
    internal void RefreshScroll(){
        if(refreshing||IsDisposed||!content.IsHandleCreated)return;refreshing=true;
        try{
            int total=0,page=1,value=0;
            var grid=content as DataGridView;var list=content as ListBox;var text=content as TextBox;
            if(panelContent){
                int measureWidth = reserveBarSpace ? ClientSize.Width - bar.Width : viewport.ClientSize.Width;
                total = fitPanelContent ? content.GetPreferredSize(new Size(Math.Max(1, measureWidth), 0)).Height : content.MinimumSize.Height;
                bar.WheelUnit=Math.Max(1,content.Font.Height);
                page = Math.Max(1, viewport.ClientSize.Height);
                value = Math.Min(Math.Max(0, -content.Top), Math.Max(0, total - page));
            }
            else if(grid!=null){
                total=grid.RowCount;page=Math.Max(1,grid.DisplayedRowCount(false));value=Math.Max(0,grid.FirstDisplayedScrollingRowIndex);
                if(grid.Rows.GetRowsHeight(DataGridViewElementStates.Visible)<=grid.ClientSize.Height-grid.ColumnHeadersHeight)page=Math.Max(1,total);
            }
            else if(list!=null){total=list.Items.Count;page=Math.Max(1,list.ClientSize.Height/Math.Max(1,list.ItemHeight));value=list.TopIndex;}
            else if(text!=null&&text.Multiline){total=(int)SendMessage(text.Handle,0x00ba,IntPtr.Zero,IntPtr.Zero);page=Math.Max(1,text.ClientSize.Height/Math.Max(1,text.Font.Height));value=(int)SendMessage(text.Handle,0x00ce,IntPtr.Zero,IntPtr.Zero);}
            bar.Configure(total,page,value);bool visible=total>page;if(bar.Visible!=visible){bar.Visible=visible;PerformLayout();}
            int nativeWidth=list==null?0:Math.Max(0,list.Width-list.ClientSize.Width);
            int width=reserveBarSpace?ClientSize.Width-bar.Width:viewport.ClientSize.Width+nativeWidth;
            var bounds=new Rectangle(0,panelContent?-Math.Min(value,Math.Max(0,total-page)):0,Math.Max(1,width),Math.Max(1,panelContent?Math.Max(total,page):viewport.ClientSize.Height));
            if(content.Bounds!=bounds)content.Bounds=bounds;
        }finally{refreshing=false;}
    }
    internal void ScrollTo(int value){
        if(content.IsDisposed)return;
        var grid=content as DataGridView;var list=content as ListBox;var text=content as TextBox;
        if(panelContent)content.Top=-Math.Max(0,value);
        else if(grid!=null&&grid.RowCount>0)grid.FirstDisplayedScrollingRowIndex=Math.Min(value,grid.RowCount-1);
        else if(list!=null&&list.Items.Count>0)list.TopIndex=Math.Min(value,list.Items.Count-1);
        else if(text!=null){int current=(int)SendMessage(text.Handle,0x00ce,IntPtr.Zero,IntPtr.Zero);SendMessage(text.Handle,0x00b6,IntPtr.Zero,(IntPtr)(value-current));}
        RefreshScroll();
    }
    protected override void Dispose(bool disposing){if(disposing&&events!=null)events.ReleaseHandle();base.Dispose(disposing);}
    sealed class NativeEvents : NativeWindow {
        readonly GeneralScrollView view;
        internal NativeEvents(GeneralScrollView view,IntPtr handle){this.view=view;AssignHandle(handle);}
        protected override void WndProc(ref Message message){
            if(message.Msg==0x020a){
                view.RefreshScroll();
                if(view.bar.Limit>0){view.bar.Wheel((short)((long)message.WParam>>16));message.Result=IntPtr.Zero;return;}
            }
            base.WndProc(ref message);
            if(message.Msg==0x0115||message.Msg==0x0005||message.Msg==0x0102||message.Msg==0x000c||message.Msg==0x0101||message.Msg==0x0180||message.Msg==0x0181||message.Msg==0x0182||message.Msg==0x0184)view.Queue();
        }
    }
}
