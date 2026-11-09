using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class LucideIcons {
    static readonly PrivateFontCollection fonts=new PrivateFontCollection();
    static readonly IntPtr memory;
    [DllImport("gdi32.dll")]static extern IntPtr AddFontMemResourceEx(IntPtr memory,uint size,IntPtr reserved,ref uint count);
    // Official lucide-static 0.468.0 glyphs; the upstream map and license are in Assets.
    static readonly Dictionary<string,string> glyphs=new Dictionary<string,string>{
        {"menu","\ue118"},{"options","\ue118"},{"list","\ue10b"},{"refresh","\ue148"},{"maximize","\ue116"},{"minimize","\ue11e"},
        {"folder","\ue246"},{"down","\ue071"},{"up","\ue074"},{"left","\ue072"},{"right","\ue073"},{"check","\ue070"},{"close","\ue1b1"},{"settings","\ue157"},{"trash","\ue18d"},{"package","\ue12c"},
        {"save","\ue150"},{"new","\ue0cd"},{"open","\ue246"},{"export","\ue19d"},{"import","\ue0b6"},{"share","\ue159"},{"copy","\ue0a2"},{"pause","\ue131"},{"play","\ue13f"},{"home","\ue0f8"},{"search","\ue154"}
    };
    static LucideIcons(){
        using(var stream=typeof(LucideIcons).Assembly.GetManifestResourceStream("ModLoader.Lucide.ttf")){
            if(stream==null)throw new InvalidOperationException("Lucide font is missing.");
            var bytes=new byte[stream.Length];stream.Read(bytes,0,bytes.Length);
            memory=Marshal.AllocCoTaskMem(bytes.Length);Marshal.Copy(bytes,0,memory,bytes.Length);fonts.AddMemoryFont(memory,bytes.Length);
            uint count=0;AddFontMemResourceEx(memory,(uint)bytes.Length,IntPtr.Zero,ref count);
        }
    }
    internal static void Draw(Graphics graphics,string name,Rectangle bounds,Color color){
        string glyph;if(!glyphs.TryGetValue(name,out glyph))throw new ArgumentException("Unknown Lucide icon: "+name);
        using(var font=new Font(fonts.Families[0],Math.Min(bounds.Width,bounds.Height),FontStyle.Regular,GraphicsUnit.Pixel))
            TextRenderer.DrawText(graphics,glyph,font,bounds,color,TextFormatFlags.NoPadding|TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine);
    }
}

internal sealed class GeneralReveal : IDisposable {
    readonly Control target;
    readonly Timer timer=new Timer{Interval=15};
    readonly Stopwatch watch=new Stopwatch();
    bool opening;
    double progress=1,start;
    Point origin;
    Action complete;
    internal bool Running {get;private set;}
    internal GeneralReveal(Control target){this.target=target;timer.Tick+=(s,e)=>Tick();target.Disposed+=(s,e)=>Dispose();}
    internal void PrepareOpen(Point anchor){
        if(target.IsDisposed)return;timer.Stop();origin=anchor;opening=true;complete=null;
        Running=GeneralStyle.AnimationsEnabled;if(Running)Frame(0);else SetRound();
    }
    internal void Begin(bool open,Point anchor,Action done=null){
        if(target.IsDisposed)return;
        timer.Stop();opening=open;origin=anchor;complete=done;
        if(!GeneralStyle.AnimationsEnabled||!target.IsHandleCreated){SetRound();Running=false;if(done!=null)done();return;}
        Running=true;start=open?0:progress;watch.Restart();Frame(start);timer.Start();
    }
    void Tick(){
        if(target.IsDisposed){Dispose();return;}
        double t=Math.Min(1,watch.Elapsed.TotalMilliseconds/210.0),eased=1-Math.Pow(1-t,3);
        Frame(opening?start+(1-start)*eased:start*(1-eased));
        if(t>=1){timer.Stop();Running=false;if(opening)SetRound();var done=complete;complete=null;if(done!=null)done();}
    }
    void Frame(double progress){
        this.progress=progress;
        int width=Math.Max(1,target.ClientSize.Width),height=Math.Max(1,target.ClientSize.Height);
        double dx=Math.Max(origin.X,width-origin.X),dy=Math.Max(origin.Y,height-origin.Y);
        float radius=(float)(Math.Sqrt(dx*dx+dy*dy)*progress+1);
        using(var round=GeneralStyle.Round(new RectangleF(0,0,width,height),18))
        using(var circle=new GraphicsPath()){
            circle.AddEllipse(origin.X-radius,origin.Y-radius,radius*2,radius*2);
            var region=new Region(round);region.Intersect(circle);Replace(region);
        }
    }
    internal void SetRound(){if(target.IsDisposed)return;progress=1;using(var round=GeneralStyle.Round(new RectangleF(0,0,Math.Max(1,target.ClientSize.Width),Math.Max(1,target.ClientSize.Height)),18))Replace(new Region(round));}
    void Replace(Region next){var previous=target.Region;target.Region=next;if(previous!=null)previous.Dispose();target.Invalidate(true);}
    public void Dispose(){timer.Stop();timer.Dispose();complete=null;Running=false;}
}

internal sealed class GeneralPopupMenu : ContextMenuStrip {
    readonly GeneralReveal reveal;
    bool closing,allowClose;
    internal bool Animating {get{return reveal.Running;}}
    internal GeneralPopupMenu(){reveal=new GeneralReveal(this);}
    protected override void OnOpening(System.ComponentModel.CancelEventArgs e){allowClose=closing=false;reveal.PrepareOpen(new Point(Width-28,0));base.OnOpening(e);}
    protected override void OnOpened(EventArgs e){base.OnOpened(e);if(closing||allowClose)return;reveal.Begin(true,new Point(Width-28,0));}
    protected override void OnClosing(ToolStripDropDownClosingEventArgs e){
        base.OnClosing(e);
        if(e.Cancel||allowClose||!Visible||!GeneralStyle.AnimationsEnabled)return;
        e.Cancel=true;if(closing)return;closing=true;
        var reason=e.CloseReason;
        reveal.Begin(false,new Point(Width-28,0),()=>{allowClose=true;Close(reason);});
    }
}

internal class GeneralDialog : Form {
    readonly Label title=new Label{Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true};
    readonly GeneralReveal reveal;
    readonly TableLayoutPanel header;
    readonly GeneralButton close;
    bool allowClose,closing;
    Point origin;
    internal Panel Content {get;private set;}
    public new Control.ControlCollection Controls {get{return Content==null?base.Controls:Content.Controls;}}
    [DllImport("user32.dll")]static extern bool ReleaseCapture();
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);
    internal GeneralDialog():this(""){}
    internal GeneralDialog(string text){
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;Font=GeneralStyle.Font(12);BackColor=GeneralStyle.Surface;
        Padding=new Padding(2);Content=new Panel{Dock=DockStyle.Fill,BackColor=GeneralStyle.Surface};
        var shell=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=Padding.Empty,Margin=Padding.Empty,BackColor=GeneralStyle.Surface};
        shell.Resize+=(s,e)=>GeneralStyle.ClipCardBody(shell);
        shell.RowStyles.Add(new RowStyle(SizeType.Absolute,52));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        header=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,Margin=Padding.Empty,Padding=new Padding(20,4,8,4)};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));
        title.Font=GeneralStyle.Font(14,true);title.ForeColor=GeneralStyle.Foreground;header.Controls.Add(title,0,0);
        close=new GeneralButton{Text="",IconKind="close",Bare=true,Size=new Size(44,44),Dock=DockStyle.Fill,Margin=Padding.Empty,AccessibleName=GeneralStyle.T("Fechar","Close")};
        close.Click+=(s,e)=>Complete(DialogResult.Cancel);header.Controls.Add(close,1,0);
        title.MouseDown+=(s,e)=>{if(e.Button==MouseButtons.Left){ReleaseCapture();SendMessage(Handle,0x00A1,(IntPtr)2,IntPtr.Zero);}};
        shell.Controls.Add(header,0,0);shell.Controls.Add(Content,0,1);base.Controls.Add(shell);Text=text;
        reveal=new GeneralReveal(this);SetStyle(ControlStyles.ResizeRedraw|ControlStyles.OptimizedDoubleBuffer,true);
    }
    protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);if(title!=null)title.Text=Text;}
    protected virtual bool AnimateAppearance { get { return true; } }
    protected override void SetVisibleCore(bool value){
        if(value&&!Visible&&reveal!=null){
            allowClose=closing=false;
            origin=new Point(Width-36,0);
            if(AnimateAppearance)reveal.PrepareOpen(origin);else reveal.SetRound();
        }
        base.SetVisibleCore(value);
    }
    protected override void OnShown(EventArgs e){base.OnShown(e);if(closing||allowClose||!AnimateAppearance)return;origin=new Point(Width-36,0);reveal.Begin(true,origin);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);GeneralStyle.PaintCard(e.Graphics,ClientSize);
    }
    protected override void OnResize(EventArgs e){base.OnResize(e);if(reveal!=null&&!reveal.Running)reveal.SetRound();}
    internal void Complete(DialogResult result){DialogResult=result;if(!Modal)Close();}
    internal void ShowCloseButton(bool visible){close.Visible=visible;header.ColumnStyles[1].Width=visible?44:0;header.Padding=new Padding(20,4,visible?8:20,4);}
    internal bool CloseButtonShown {get{return close.Visible;}}
    internal void AnchorTo(Control anchor){AnchorBelow(anchor,new Rectangle(0,0,anchor.Width,anchor.Height),true);}
    internal void AnchorBelow(Control anchor,Rectangle bounds,bool alignRight){
        StartPosition=FormStartPosition.Manual;
        Location=GeneralPopupPlacement.Location(anchor,bounds,Size,alignRight);
    }
    protected override bool ProcessCmdKey(ref Message message,Keys keyData){if(keyData==Keys.Escape){Complete(DialogResult.Cancel);return true;}return base.ProcessCmdKey(ref message,keyData);}
    protected override void OnFormClosing(FormClosingEventArgs e){
        base.OnFormClosing(e);if(e.Cancel||allowClose||!Visible||!AnimateAppearance||!GeneralStyle.AnimationsEnabled)return;
        e.Cancel=true;if(closing)return;closing=true;
        var result=DialogResult==DialogResult.None?DialogResult.Cancel:DialogResult;
        reveal.Begin(false,origin,()=>{allowClose=true;Complete(result);});
    }
    internal static DialogResult Message(IWin32Window owner,string text,string caption,MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None,MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1,Action<bool> remember=null,string rememberLabel=null,string yesLabel=null,string noLabel=null){
        using(var dialog=new GeneralDialog(caption){ClientSize=new Size(620,320),MinimumSize=new Size(420,220)}){
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(20,8,20,20)};
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var preference=new CheckBox{Text=rememberLabel??"",AutoSize=true,Dock=DockStyle.Fill,Margin=new Padding(0,8,0,0),Name="queue-errors-suppress",Visible=remember!=null};
            var body=new TextBox{Multiline=true,ReadOnly=true,BorderStyle=BorderStyle.None,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical,Text=text,BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,Font=GeneralStyle.Font(12)};
            var commands=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,14,0,0)};
            var results=buttons==MessageBoxButtons.YesNoCancel?new[]{DialogResult.Yes,DialogResult.No,DialogResult.Cancel}:buttons==MessageBoxButtons.YesNo?new[]{DialogResult.Yes,DialogResult.No}:buttons==MessageBoxButtons.OKCancel?new[]{DialogResult.OK,DialogResult.Cancel}:new[]{DialogResult.OK};
            foreach(var result in results.Reverse()){
                var chosen=result;
                string label=result==DialogResult.Yes?(yesLabel??GeneralStyle.T("Sim","Yes")):result==DialogResult.No?(noLabel??GeneralStyle.T("Não","No")):result==DialogResult.Cancel?GeneralStyle.T("Cancelar","Cancel"):"OK";
                var action=GeneralStyle.Button(label,()=>dialog.Complete(chosen),result==results[0]);
                commands.Controls.Add(action);
                if(result==DialogResult.Cancel||result==DialogResult.No&&buttons==MessageBoxButtons.YesNo)dialog.CancelButton=action;
                if(result==results[defaultButton==MessageBoxDefaultButton.Button2&&results.Length>1?1:0])dialog.AcceptButton=action;
            }
            layout.Controls.Add(new GeneralScrollView(body),0,0);layout.Controls.Add(preference,0,1);layout.Controls.Add(commands,0,2);dialog.Content.Controls.Add(layout);
            GeneralStyle.Apply(dialog,GeneralStyle.Dark);body.BorderStyle=BorderStyle.None;var answer=dialog.ShowDialog(owner);if(remember!=null&&answer==DialogResult.OK)remember(preference.Checked);return answer;
        }
    }
}

internal static class GeneralPopupPlacement {
    internal const int OwnerGap=24,ScreenGap=16;
    internal static Point Location(Control anchor,Rectangle bounds,Size popup,bool alignRight){
        var owner=anchor.FindForm();var window=owner==null?Rectangle.Empty:owner.RectangleToScreen(owner.ClientRectangle);
        return Resolve(anchor.RectangleToScreen(bounds),popup,window,Screen.FromControl(anchor).WorkingArea,alignRight);
    }
    internal static Point Resolve(Rectangle anchor,Size popup,Rectangle owner,Rectangle working,bool alignRight){
        var screen=Inset(working,ScreenGap);var inside=Inset(Rectangle.Intersect(working,owner),OwnerGap);
        int wantedX=alignRight?anchor.Right-popup.Width:anchor.Left,wantedY=anchor.Bottom+8;
        if(inside.Width>=popup.Width&&inside.Height>=popup.Height){
            if(wantedY+popup.Height>inside.Bottom&&anchor.Top-8-popup.Height>=inside.Top)wantedY=anchor.Top-8-popup.Height;
            return Clamp(new Point(wantedX,wantedY),popup,inside);
        }
        if(!owner.IsEmpty&&owner.Right+OwnerGap+popup.Width<=screen.Right&&owner.Right+OwnerGap>=screen.Left)
            return Clamp(new Point(owner.Right+OwnerGap,wantedY),popup,screen);
        if(!owner.IsEmpty&&owner.Left-OwnerGap-popup.Width>=screen.Left&&owner.Left-OwnerGap<=screen.Right)
            return Clamp(new Point(owner.Left-OwnerGap-popup.Width,wantedY),popup,screen);
        return Clamp(new Point(wantedX,wantedY),popup,screen);
    }
    static Rectangle Inset(Rectangle bounds,int margin){return new Rectangle(bounds.X+margin,bounds.Y+margin,Math.Max(0,bounds.Width-margin*2),Math.Max(0,bounds.Height-margin*2));}
    static Point Clamp(Point point,Size size,Rectangle bounds){return new Point(Math.Max(bounds.Left,Math.Min(point.X,bounds.Right-size.Width)),Math.Max(bounds.Top,Math.Min(point.Y,bounds.Bottom-size.Height)));}
}

internal sealed class GeneralPicker : UserControl {
    readonly TextBox input=new TextBox{BorderStyle=BorderStyle.None};
    readonly List<object> items=new List<object>();
    int index=-1;
    ComboBoxStyle style=ComboBoxStyle.DropDownList;
    bool writing;
    ToolStripDropDown dropdown;
    internal List<object> Items {get{return items;}}
    internal event EventHandler SelectedIndexChanged;
    internal int SelectedIndex {
        get{return index;}
        set{
            if(value< -1||value>=items.Count)throw new ArgumentOutOfRangeException("value");
            bool changed=index!=value;index=value;SetText(value>=0?Convert.ToString(items[value]):"");
            if(changed&&SelectedIndexChanged!=null)SelectedIndexChanged(this,EventArgs.Empty);
        }
    }
    internal object SelectedItem {get{return index>=0&&index<items.Count?items[index]:null;}set{SelectedIndex=items.IndexOf(value);}}
    internal ComboBoxStyle DropDownStyle {get{return style;}set{style=value;input.Visible=value==ComboBoxStyle.DropDown;PerformLayout();Invalidate();}}
    internal int MaxLength {get{return input.MaxLength;}set{input.MaxLength=value;}}
    internal ToolStripDropDown OpenPopup {get{return dropdown;}}
    internal bool IconOnly {get;set;}
    internal GeneralPicker(){
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Selectable,true);
        Height=44;MinimumSize=new Size(80,44);MaximumSize=new Size(0,44);Font=GeneralStyle.Font(12);BackColor=GeneralStyle.Surface;ForeColor=GeneralStyle.Foreground;Cursor=Cursors.Hand;
        AccessibleRole=AccessibleRole.ComboBox;Controls.Add(input);input.Visible=false;input.Font=Font;
        input.TextChanged+=(s,e)=>{if(!writing&&base.Text!=input.Text){base.Text=input.Text;index=items.FindIndex(item=>Convert.ToString(item)==input.Text);Invalidate();}};
        input.GotFocus+=(s,e)=>Invalidate();input.LostFocus+=(s,e)=>Invalidate();
    }
    public override string Text {get{return base.Text;}set{SetText(value??"");index=items.FindIndex(item=>Convert.ToString(item)==value);}}
    void SetText(string text){writing=true;try{if(base.Text!=text)base.Text=text;if(input.Text!=text)input.Text=text;}finally{writing=false;}Invalidate();}
    protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);input.SetBounds(14,Math.Max(1,(Height-input.PreferredHeight)/2),Math.Max(1,Width-64),input.PreferredHeight);input.BackColor=GeneralStyle.Surface;input.ForeColor=GeneralStyle.Foreground;}
    protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);if(input!=null)input.Font=Font;PerformLayout();}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){base.OnLostFocus(e);Invalidate();}
    protected override void OnPaint(PaintEventArgs e){
        Control surface=Parent;while(surface!=null&&surface.BackColor.A<255)surface=surface.Parent;
        e.Graphics.Clear(surface==null?GeneralStyle.Background:surface.BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        if(!IconOnly||Focused||dropdown!=null&&dropdown.Visible)GeneralStyle.PaintRoundedFace(e.Graphics,new RectangleF(0,0,Width,Height),16,Focused||input.Focused||dropdown!=null&&dropdown.Visible?GeneralStyle.Accent:GeneralStyle.Line,GeneralStyle.Surface);
        if(!IconOnly&&style==ComboBoxStyle.DropDownList)TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(14,0,Width-62,Height),Enabled?GeneralStyle.Foreground:GeneralStyle.Muted,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        LucideIcons.Draw(e.Graphics,dropdown!=null&&dropdown.Visible?"up":"down",new Rectangle(IconOnly?(Width-20)/2:Width-34,(Height-20)/2,20,20),Enabled?GeneralStyle.Accent:GeneralStyle.Muted);
    }
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(e.Button==MouseButtons.Left&&(style==ComboBoxStyle.DropDownList||e.X>=Width-44)){Focus();Toggle();}}
    protected override bool ProcessCmdKey(ref Message message,Keys keyData){
        if(keyData==Keys.F4||keyData==(Keys.Alt|Keys.Down)||style==ComboBoxStyle.DropDownList&&(keyData==Keys.Space||keyData==Keys.Enter||keyData==Keys.Down)){Toggle();return true;}
        return base.ProcessCmdKey(ref message,keyData);
    }
    internal void Toggle(){
        if(!Enabled||items.Count==0)return;
        if(dropdown!=null&&dropdown.Visible){dropdown.Close();return;}
        if(dropdown!=null)dropdown.Dispose();
        var list=new ChoiceList(index){Font=Font,BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,BorderStyle=BorderStyle.None,IntegralHeight=false,ItemHeight=42,DrawMode=DrawMode.OwnerDrawFixed};
        list.Items.AddRange(items.ToArray());list.SelectedIndex=index;
        int width=Math.Max(Width,300),height=Math.Min(340,items.Count*42)+20;
        list.Size=new Size(width-20,height-20);
        dropdown=new ChoicePopup{AutoSize=false,Padding=new Padding(10),BackColor=GeneralStyle.Surface,DropShadowEnabled=false};
        var scroll=new GeneralScrollView(list){Size=list.Size};
        var host=new ToolStripControlHost(scroll){Margin=Padding.Empty,Padding=Padding.Empty,AutoSize=false,Size=list.Size};
        dropdown.Items.Add(host);
        dropdown.Size=new Size(width,height);
        Action commit=()=>{if(list.SelectedIndex>=0){SelectedIndex=list.SelectedIndex;dropdown.Close();}};
        list.MouseClick+=(s,e)=>{if(list.IndexFromPoint(e.Location)>=0)commit();};
        list.KeyDown+=(s,e)=>{if(e.KeyCode==Keys.Enter){e.Handled=true;commit();}else if(e.KeyCode==Keys.Escape){e.Handled=true;dropdown.Close();}};
        list.MouseMove+=(s,e)=>{int at=list.IndexFromPoint(e.Location);if(at>=0&&at!=list.SelectedIndex)list.SelectedIndex=at;};
        dropdown.Closed+=(s,e)=>{Invalidate();if(!IsDisposed&&CanFocus)Focus();};
        dropdown.Opened+=(s,e)=>{scroll.RefreshScroll();list.Focus();Invalidate();};
        var point=GeneralPopupPlacement.Location(this,ClientRectangle,dropdown.Size,false);
        dropdown.Show(this,PointToClient(point));Invalidate();
    }
    protected override void Dispose(bool disposing){if(disposing&&dropdown!=null)dropdown.Dispose();base.Dispose(disposing);}
    sealed class ChoiceList : ListBox {
        readonly int chosen;
        internal ChoiceList(int chosen){this.chosen=chosen;}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);GeneralNativeTheme.Apply(this);}
        protected override void OnDrawItem(DrawItemEventArgs e){
            if(e.Index<0)return;e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
            GeneralStyle.PaintChoice(e.Graphics,e.Bounds,(e.State&DrawItemState.Selected)!=0);
            if(e.Index==chosen)LucideIcons.Draw(e.Graphics,"check",new Rectangle(e.Bounds.X+10,e.Bounds.Y+12,18,18),GeneralStyle.Accent);
            TextRenderer.DrawText(e.Graphics,Convert.ToString(Items[e.Index]),Font,new Rectangle(e.Bounds.X+38,e.Bounds.Y,e.Bounds.Width-48,e.Bounds.Height),GeneralStyle.Foreground,TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        }
    }
    sealed class ChoicePopup : ToolStripDropDown {
        readonly GeneralReveal reveal;
        bool allowClose,closing;
        internal ChoicePopup(){reveal=new GeneralReveal(this);}
        protected override void OnOpening(System.ComponentModel.CancelEventArgs e){allowClose=closing=false;reveal.PrepareOpen(new Point(24,0));base.OnOpening(e);}
        protected override void OnPaintBackground(PaintEventArgs e){GeneralStyle.PaintCard(e.Graphics,ClientSize);}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);}
        protected override void OnOpened(EventArgs e){base.OnOpened(e);if(closing||allowClose)return;reveal.Begin(true,new Point(24,0));}
        protected override void OnClosing(ToolStripDropDownClosingEventArgs e){
            base.OnClosing(e);if(e.Cancel||allowClose||!Visible||!GeneralStyle.AnimationsEnabled)return;e.Cancel=true;if(closing)return;closing=true;
            var reason=e.CloseReason;reveal.Begin(false,new Point(24,0),()=>{allowClose=true;Close(reason);});
        }
    }
}
