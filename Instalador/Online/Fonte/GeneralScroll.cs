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
    internal void SyncPosition(int next){Configure(total,page,next);}
    internal void MoveTo(int next){next=Math.Max(0,Math.Min(Limit,next));if(next==value)return;value=next;Invalidate();if(Changed!=null)Changed(next);}
    internal void Wheel(int delta){if(SystemInformation.MouseWheelScrollLines==0)return;remainder+=delta;int steps=remainder/SystemInformation.MouseWheelScrollDelta;remainder-=steps*SystemInformation.MouseWheelScrollDelta;MoveTo(value-steps*(SystemInformation.MouseWheelScrollLines<0?page:SystemInformation.MouseWheelScrollLines*WheelUnit));}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(GeneralStyle.Surface);e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;if(Limit==0)return;using(var path=GeneralStyle.Round(Thumb,4))using(var brush=new SolidBrush(Capture||Focused?GeneralStyle.Accent:GeneralStyle.Muted))e.Graphics.FillPath(brush,path);}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left)return;Focus();if(Thumb.Contains(e.Location)){drag=e.Y-Thumb.Top;Capture=true;}else MoveTo(value+(e.Y<Thumb.Top?-page:page));}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(drag<0||!Capture)return;int travel=Math.Max(1,Height-8-Thumb.Height);MoveTo((int)Math.Round((e.Y-drag-4)*(double)Limit/travel));}
    protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);drag=-1;Capture=false;Invalidate();}
    protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture)drag=-1;}
    protected override void OnMouseWheel(MouseEventArgs e){
        var view=Parent as GeneralScrollView;
        if(view==null)Wheel(e.Delta);
        else view.RouteWheel(e.Delta);
        var handled=e as HandledMouseEventArgs;
        if(handled!=null)handled.Handled=true;
    }
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
    int[] visibleGridRows;
    const DataGridViewElementStates VisibleRows=DataGridViewElementStates.Visible;
    [ThreadStatic] static MiddleScrollFilter middleScroll;
    bool queued,refreshing,wheelActivated;
    internal Control Content {get{return content;}}
    internal GeneralScrollBar Bar {get{return bar;}}
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr first,IntPtr second);
    internal GeneralScrollView(Control content,bool panelContent=false,bool reserveBarSpace=false,bool fitPanelContent=false){
        this.panelContent=panelContent;this.reserveBarSpace=reserveBarSpace;this.fitPanelContent=fitPanelContent;
        this.content=content;Dock=DockStyle.Fill;Margin=Padding.Empty;Padding=Padding.Empty;BackColor=GeneralStyle.Surface;
        content.Dock=DockStyle.None;content.Margin=Padding.Empty;viewport.Controls.Add(content);Controls.Add(viewport);Controls.Add(bar);
        var text=content as TextBox;if(text!=null&&text.Multiline){text.ScrollBars=ScrollBars.None;text.WordWrap=true;}
        var grid=content as DataGridView;
        if(grid!=null){
            grid.ScrollBars=ScrollBars.None;
            grid.Scroll+=(s,e)=>{
                if(e.ScrollOrientation==ScrollOrientation.VerticalScroll&&visibleGridRows!=null)
                    bar.SyncPosition(GridPosition(grid));
            };
            grid.RowsAdded+=(s,e)=>InvalidateGridRows();
            grid.RowsRemoved+=(s,e)=>InvalidateGridRows();
            grid.Sorted+=(s,e)=>InvalidateGridRows();
            grid.RowStateChanged+=(s,e)=>{if((e.StateChanged&VisibleRows)!=0)InvalidateGridRows();};
            grid.RowHeightChanged+=(s,e)=>Queue();
            grid.ColumnWidthChanged+=(s,e)=>Queue();
        }
        var list=content as ListBox;if(list!=null){list.HorizontalScrollbar=false;list.IntegralHeight=false;}
        bar.Changed=ScrollTo;content.HandleCreated+=(s,e)=>Attach();content.HandleDestroyed+=(s,e)=>{if(events!=null)events.ReleaseHandle();};content.TextChanged+=(s,e)=>Queue();content.FontChanged+=(s,e)=>Queue();content.Resize+=(s,e)=>Queue();
        content.MouseDown+=ActivateWheel;
        bar.MouseDown+=ActivateWheel;
        Leave+=(s,e)=>wheelActivated=false;
        if(fitPanelContent){
            content.Layout+=(s,e)=>{if(!refreshing)Queue();};
            content.VisibleChanged+=(s,e)=>Queue();
        }
        viewport.Resize+=(s,e)=>RefreshScroll();HandleCreated+=(s,e)=>Attach();if(content.IsHandleCreated)Attach();
    }
    void InvalidateGridRows(){
        visibleGridRows=null;
        Queue();
    }

    int[] GridRows(DataGridView grid){
        if(visibleGridRows!=null)return visibleGridRows;
        visibleGridRows=new int[grid.Rows.GetRowCount(VisibleRows)];
        int position=0;
        for(int row=grid.Rows.GetFirstRow(VisibleRows);row>=0;row=grid.Rows.GetNextRow(row,VisibleRows))
            visibleGridRows[position++]=row;
        return visibleGridRows;
    }

    int GridPosition(DataGridView grid){
        int index=Array.BinarySearch(GridRows(grid),grid.FirstDisplayedScrollingRowIndex);
        return index>=0?index:Math.Max(0,~index-1);
    }

    void Attach(){if(!content.IsHandleCreated)return;if(events!=null)events.ReleaseHandle();events=new NativeEvents(this,content.Handle);Queue();}
    void Queue(){if(queued||IsDisposed||Disposing||!IsHandleCreated)return;queued=true;BeginInvoke((Action)(()=>{queued=false;if(!IsDisposed&&!Disposing&&IsHandleCreated)RefreshScroll();}));}
    internal void RefreshScroll(){
        if(refreshing||IsDisposed||!content.IsHandleCreated)return;refreshing=true;
        try{
            int total=0,page=1,value=0;
            var grid=content as DataGridView;var list=content as ListBox;var text=content as TextBox;
            if(panelContent){
                total=content.MinimumSize.Height;
                if(fitPanelContent){
                    int widthForLayout=Math.Max(1,reserveBarSpace?ClientSize.Width-bar.Width:viewport.ClientSize.Width);
                    if(content.Width!=widthForLayout)content.Width=widthForLayout;
                    content.PerformLayout();
                    // A spanning table cell can inflate preferred height. Measure the laid-out fields.
                    total=content.Controls.Cast<Control>().Where(child=>child.Visible)
                        .Select(child=>child.Bottom+child.Margin.Bottom).DefaultIfEmpty(0).Max()+content.Padding.Bottom;
                }
                bar.WheelUnit=Math.Max(1,content.Font.Height);
                page = Math.Max(1, viewport.ClientSize.Height);
                value = Math.Min(Math.Max(0, -content.Top), Math.Max(0, total - page));
            }
            else if(grid!=null){
                var rows=GridRows(grid);
                total=rows.Length;
                value=GridPosition(grid);
                var horizontal=grid.Controls.OfType<HScrollBar>().FirstOrDefault(control=>control.Visible);
                int room=grid.ClientSize.Height-grid.ColumnHeadersHeight-(horizontal==null?0:horizontal.Height);
                // The last page defines the scroll limit even when earlier rows have different heights.
                page=0;
                for(int index=rows.Length-1;index>=0;index--){
                    int height=grid.Rows[rows[index]].Height;
                    if(page>0&&room<height)break;
                    room-=height;
                    page++;
                }
            }
            else if(list!=null){total=list.Items.Count;page=Math.Max(1,viewport.ClientSize.Height/Math.Max(1,list.ItemHeight));value=list.TopIndex;}
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
        if(panelContent){
            if(queued||bar.Limit==0)RefreshScroll();
            bar.SyncPosition(value);
            if(content.Top!=-bar.Value)content.Top=-bar.Value;
            return;
        }
        if(grid!=null){
            if(visibleGridRows==null)RefreshScroll();
            var rows=GridRows(grid);
            if(rows.Length==0)return;
            int row=rows[Math.Max(0,Math.Min(value,bar.Limit))];
            if(grid.FirstDisplayedScrollingRowIndex!=row)grid.FirstDisplayedScrollingRowIndex=row;
            bar.SyncPosition(GridPosition(grid));
            return;
        }
        if(list!=null&&list.Items.Count>0)list.TopIndex=Math.Min(value,list.Items.Count-1);
        else if(text!=null){int current=(int)SendMessage(text.Handle,0x00ce,IntPtr.Zero,IntPtr.Zero);SendMessage(text.Handle,0x00b6,IntPtr.Zero,(IntPtr)(value-current));}
        RefreshScroll();
    }
    void ActivateWheel(object sender,MouseEventArgs e){
        if(e.Button==MouseButtons.Left)wheelActivated=true;
    }

    GeneralScrollView OuterView(){
        for(Control owner=Parent;owner!=null;owner=owner.Parent){
            var view=owner as GeneralScrollView;
            if(view!=null)return view;
        }
        return null;
    }

    internal bool RouteWheel(int delta){
        if(delta==0||SystemInformation.MouseWheelScrollLines==0)return false;
        var outer=OuterView();
        if(outer==null||(wheelActivated&&ContainsFocus)){
            if(queued||bar.Limit==0)RefreshScroll();
            if(bar.Visible&&(delta>0?bar.Value>0:bar.Value<bar.Limit)){
                bar.Wheel(delta);
                return true;
            }
        }
        if(outer==null)return false;
        outer.RouteWheel(delta);
        return true;
    }

    protected override void OnHandleCreated(EventArgs e){
        base.OnHandleCreated(e);
        if(middleScroll==null){
            middleScroll=new MiddleScrollFilter();
            Application.AddMessageFilter(middleScroll);
        }
        middleScroll.Views++;
    }

    protected override void OnHandleDestroyed(EventArgs e){
        if(middleScroll!=null){
            if(middleScroll.Active==this)middleScroll.Stop();
            if(--middleScroll.Views==0){
                Application.RemoveMessageFilter(middleScroll);
                middleScroll.Dispose();
                middleScroll=null;
            }
        }
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing){if(disposing&&events!=null)events.ReleaseHandle();base.Dispose(disposing);}

    sealed class MiddleScrollFilter : IMessageFilter, IDisposable {
        const int LeftDown=0x0201, RightDown=0x0204, MiddleDown=0x0207, MiddleDoubleClick=0x0209, KeyDown=0x0100;
        readonly Timer timer=new Timer{Interval=20};
        internal GeneralScrollView Active {get;private set;}
        internal int Views;
        Form window;
        Cursor previousCursor;
        Point anchor;
        double remainder;

        internal MiddleScrollFilter(){timer.Tick+=(s,e)=>Scroll();}

        public bool PreFilterMessage(ref Message message){
            bool middle=message.Msg==MiddleDown||message.Msg==MiddleDoubleClick;
            bool escape=message.Msg==KeyDown&&message.WParam==(IntPtr)Keys.Escape;
            if(Active!=null&&(middle||escape||message.Msg==LeftDown||message.Msg==RightDown)){
                Stop();
                return middle||escape;
            }
            if(!middle)return false;
            for(Control control=Control.FromChildHandle(message.HWnd);control!=null;control=control.Parent){
                var view=control as GeneralScrollView;
                if(view==null||!view.Enabled||!view.Visible)continue;
                if(view.OuterView()!=null&&(!view.wheelActivated||!view.ContainsFocus))continue;
                view.RefreshScroll();
                if(view.bar.Limit==0)continue;
                Active=view;
                anchor=Cursor.Position;
                previousCursor=view.Cursor;
                view.Cursor=Cursors.NoMoveVert;
                window=view.FindForm();
                if(window!=null)window.Deactivate+=WindowDeactivated;
                timer.Start();
                return true;
            }
            return false;
        }

        void Scroll(){
            if(Active==null)return;
            if(!Active.Visible||!Active.Enabled||Active.bar.Limit==0){Stop();return;}
            int distance=Cursor.Position.Y-anchor.Y;
            int magnitude=Math.Max(0,Math.Abs(distance)-12);
            if(magnitude==0){remainder=0;return;}
            remainder+=Math.Sign(distance)*Math.Min(1.5,magnitude/90.0)*Active.bar.WheelUnit;
            int step=(int)remainder;
            if(step==0)return;
            remainder-=step;
            Active.bar.MoveTo(Active.bar.Value+step);
        }

        void WindowDeactivated(object sender,EventArgs e){Stop();}
        internal void Stop(){
            timer.Stop();
            if(window!=null)window.Deactivate-=WindowDeactivated;
            window=null;
            if(Active!=null)Active.Cursor=previousCursor;
            Active=null;
            remainder=0;
        }
        public void Dispose(){Stop();timer.Dispose();}
    }
    sealed class NativeEvents : NativeWindow {
        readonly GeneralScrollView view;
        internal NativeEvents(GeneralScrollView view,IntPtr handle){this.view=view;AssignHandle(handle);}
        protected override void WndProc(ref Message message){
            if(message.Msg==0x020a){
                int delta=(short)((long)message.WParam>>16);
                if(view.RouteWheel(delta)||view.bar.Limit>0){message.Result=IntPtr.Zero;return;}
            }
            base.WndProc(ref message);
            if(message.Msg==0x0115||message.Msg==0x0005||message.Msg==0x0102||message.Msg==0x000c||message.Msg==0x0101||message.Msg==0x0180||message.Msg==0x0181||message.Msg==0x0182||message.Msg==0x0184)view.Queue();
        }
    }
}
