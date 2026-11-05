using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal static class GeneralStyle {
    internal static bool Dark;
    internal static string Language="pt";
    static readonly PrivateFontCollection fonts=new PrivateFontCollection();
    static readonly IntPtr fontMemory;
    [DllImport("gdi32.dll")]static extern IntPtr AddFontMemResourceEx(IntPtr memory,uint size,IntPtr reserved,ref uint count);
    static GeneralStyle(){
        using(var resource=typeof(GeneralStyle).Assembly.GetManifestResourceStream("ModLoader.Jost.ttf")){
            if(resource==null)return;
            var bytes=new byte[resource.Length];resource.Read(bytes,0,bytes.Length);
            fontMemory=Marshal.AllocCoTaskMem(bytes.Length);Marshal.Copy(bytes,0,fontMemory,bytes.Length);
            fonts.AddMemoryFont(fontMemory,bytes.Length);uint count=0;AddFontMemResourceEx(fontMemory,(uint)bytes.Length,IntPtr.Zero,ref count);
        }
    }
    internal static string T(string pt,string en){return Language=="en"?en:pt;}
    internal static Font Font(float size,bool bold=false){
        return fonts.Families.Length>0?new Font(fonts.Families[0],size,bold?FontStyle.Bold:FontStyle.Regular):
            new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);
    }
    internal static Color Background {get{return ColorTranslator.FromHtml(Dark?"#15121d":"#ffffff");}}
    internal static Color Surface {get{return ColorTranslator.FromHtml(Dark?"#242032":"#ffffff");}}
    internal static Color Foreground {get{return ColorTranslator.FromHtml(Dark?"#ece8f6":"#221d33");}}
    internal static Color Muted {get{return ColorTranslator.FromHtml(Dark?"#b9b1cf":"#625b7c");}}
    internal static Color Accent {get{return ColorTranslator.FromHtml(Dark?"#b8a4ff":"#5b3fd8");}}
    internal static Color Primary {get{return ColorTranslator.FromHtml("#5b3fd8");}}
    internal static Color Line {get{return ColorTranslator.FromHtml(Dark?"#827592":"#897d9d");}}
    internal static Color SoftLine {get{return ColorTranslator.FromHtml(Dark?"#3a334e":"#e9e5f0");}}
    internal static Color Selection {get{return ColorTranslator.FromHtml(Dark?"#3e335b":"#eee9fc");}}
    internal static Color Danger {get{return ColorTranslator.FromHtml(Dark?"#ffadb9":"#b42348");}}
    internal static GraphicsPath Round(RectangleF box,float radius){
        float d=Math.Min(radius*2,Math.Min(box.Width,box.Height));
        var path=new GraphicsPath();
        if(d<=0){path.AddRectangle(box);return path;}
        path.AddArc(box.X,box.Y,d,d,180,90);path.AddArc(box.Right-d,box.Y,d,d,270,90);
        path.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);path.AddArc(box.X,box.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    internal static Button Button(string text,Action action,bool primary=false){
        var b=new GeneralButton{Text=text,Primary=primary,AutoSize=true,MinimumSize=new Size(80,44),Padding=new Padding(16,0,16,0)};
        b.Click+=(s,e)=>action();return b;
    }
    internal static void Apply(Control control,bool dark){
        Dark=dark;
        control.BackColor=control is Form?Background:Surface;control.ForeColor=Foreground;
        if(control.Font==null||control.Font.FontFamily.Name=="Microsoft Sans Serif"||control.Font.FontFamily.Name=="Segoe UI")control.Font=Font(12);
        var button=control as Button;
        if(button!=null){button.MinimumSize=new Size(Math.Max(button.MinimumSize.Width,44),Math.Max(button.MinimumSize.Height,44));button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Line;button.FlatAppearance.BorderSize=1;button.UseVisualStyleBackColor=false;}
        var combo=control as ComboBox;
        if(combo!=null){combo.FlatStyle=FlatStyle.Flat;combo.BackColor=Surface;combo.ForeColor=Foreground;combo.DrawMode=DrawMode.OwnerDrawFixed;}
        var text=control as TextBox;
        if(text!=null){text.BackColor=Surface;text.ForeColor=Foreground;text.BorderStyle=BorderStyle.FixedSingle;}
        var checkbox=control as CheckBox;
        if(checkbox!=null)checkbox.UseVisualStyleBackColor=false;
        var progress=control as GeneralProgress;
        if(progress!=null){progress.Invalidate();return;}
        foreach(Control child in control.Controls)Apply(child,dark);
        control.Invalidate();
    }
    internal static string Status(string value){
        if(Language!="en"||String.IsNullOrEmpty(value))return value;
        string found;
        if(statuses.TryGetValue(value,out found))return found;
        if(value.StartsWith("Instalado: "))return "Installed: "+value.Substring(11);
        return value;
    }
    static readonly Dictionary<string,string> statuses=new Dictionary<string,string>{
        {"Instalado","Installed"},{"Não instalado","Not installed"},{"Instalado correto","Installed"},
        {"Pronto para instalar","Ready to install"},{"Não iniciado","Not started"},{"Não conferido","Not checked"},
        {"Falhou","Failed"},{"Pulado","Skipped"},{"Parcial/pulado","Partial / skipped"},{"Na fila","Queued"},
        {"Verificando","Checking"},{"Instalando","Installing"},{"Baixando","Downloading"},
        {"Aguardando arquivo","Waiting for file"},{"Aguardando download","Waiting for download"},
        {"Conferindo download","Checking download"},{"Não verificável","Unable to verify"},{"Verificação pulada","Check skipped"},
        {"Arquivo não encontrado","File not found"},{"Arquivos alterados","Modified files"},{"Diferente/alterado","Modified files"},
        {"Instalação parcial","Partial installation"},{"Parcial","Partial"},{"Requer Phantom Liberty","Requires Phantom Liberty"},
        {"Requer Cyberpunk 2077","Requires Cyberpunk 2077"},{"Incompatível","Incompatible"}
    };
}

internal sealed class GeneralPicker : ComboBox {
    internal GeneralPicker(){DrawMode=DrawMode.OwnerDrawFixed;FlatStyle=FlatStyle.Flat;}
    protected override void OnDrawItem(DrawItemEventArgs e){
        Color fill=(e.State&DrawItemState.Selected)!=0?GeneralStyle.Selection:GeneralStyle.Surface;
        using(var brush=new SolidBrush(fill))e.Graphics.FillRectangle(brush,e.Bounds);
        string text=e.Index>=0?Convert.ToString(Items[e.Index]):Text;
        var bounds=e.Bounds;bounds.X+=7;bounds.Width-=12;
        TextRenderer.DrawText(e.Graphics,text,Font,bounds,GeneralStyle.Foreground,TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);
        base.OnDrawItem(e);
    }
}

internal sealed class GeneralSurface : Panel {
    internal GeneralSurface(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=GeneralStyle.Round(new RectangleF(1,1,Width-3,Height-3),18)){
            using(var brush=new SolidBrush(GeneralStyle.Surface))e.Graphics.FillPath(brush,path);
            using(var pen=new Pen(GeneralStyle.Line))e.Graphics.DrawPath(pen,path);
        }
        base.OnPaint(e);
    }
}

internal sealed class GeneralButton : Button {
    internal bool Primary,Destructive;
    internal string IconKind;
    internal GeneralButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);BackColor=GeneralStyle.Background;Cursor=Cursors.Hand;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;UseVisualStyleBackColor=false;}
    protected override void OnPaintBackground(PaintEventArgs e){}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        // ButtonBase can leave stale themed pixels outside the rounded face.
        Control surface=Parent;while(surface!=null&&surface.BackColor.A<255)surface=surface.Parent;
        g.Clear(surface==null?GeneralStyle.Background:surface.BackColor);
        Color fill=Primary?GeneralStyle.Primary:GeneralStyle.Surface;
        Color ink=Primary?Color.White:Destructive?GeneralStyle.Danger:GeneralStyle.Foreground;
        if(!Enabled){fill=GeneralStyle.Selection;ink=GeneralStyle.Muted;}
        using(var path=GeneralStyle.Round(new RectangleF(1,1,Width-3,Height-3),Math.Min(22,Height/2))){
            using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
            using(var pen=new Pen(Focused?GeneralStyle.Accent:Primary?fill:GeneralStyle.Line,Focused?2:1))g.DrawPath(pen,path);
        }
        var textRect=new Rectangle(12,0,Width-24,Height);
        if(!String.IsNullOrEmpty(IconKind)){
            int x=String.IsNullOrEmpty(Text)?Width/2-9:14,y=Height/2-9;
            using(var p=new Pen(ink,1.8f)){
                p.StartCap=LineCap.Round;p.EndCap=LineCap.Round;
                if(IconKind=="folder"){g.DrawLines(p,new[]{new Point(x,y+5),new Point(x,y),new Point(x+7,y),new Point(x+10,y+4),new Point(x+19,y+4),new Point(x+17,y+17),new Point(x,y+17),new Point(x,y+5),new Point(x+18,y+5)});}
                if(IconKind=="refresh"){g.DrawArc(p,x+1,y+1,16,16,40,285);g.DrawLines(p,new[]{new Point(x+13,y),new Point(x+18,y+1),new Point(x+17,y+6)});}
                if(IconKind=="options"){g.DrawEllipse(p,x+2,y+2,14,14);g.DrawEllipse(p,x+7,y+7,4,4);for(int i=0;i<8;i++){double a=i*Math.PI/4;g.DrawLine(p,x+9+(float)Math.Cos(a)*7,y+9+(float)Math.Sin(a)*7,x+9+(float)Math.Cos(a)*10,y+9+(float)Math.Sin(a)*10);}}
                if(IconKind=="menu"){g.DrawLine(p,x,y+3,x+18,y+3);g.DrawLine(p,x,y+9,x+18,y+9);g.DrawLine(p,x+6,y+15,x+18,y+15);}
            }
            if(!String.IsNullOrEmpty(Text))textRect=new Rectangle(42,0,Width-52,Height);
        }
        TextRenderer.DrawText(g,Text,Font,textRect,ink,TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.SingleLine);
    }
}

internal sealed class GeneralProgress : Control {
    int value;
    internal int Value {get{return value;}set{this.value=Math.Max(0,Math.Min(100,value));Invalidate();}}
    internal bool Indeterminate;
    internal string Caption;
    internal GeneralProgress(){Height=37;SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        TextRenderer.DrawText(g,Caption??"",GeneralStyle.Font(10.5f),new Rectangle(0,0,Width-52,23),GeneralStyle.Muted,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g,Indeterminate?"—":Value+"%",GeneralStyle.Font(10.5f,true),new Rectangle(Width-52,0,52,23),GeneralStyle.Accent,TextFormatFlags.Right|TextFormatFlags.SingleLine);
        var track=new RectangleF(0,27,Math.Max(1,Width-1),6);
        using(var path=GeneralStyle.Round(track,3))using(var brush=new SolidBrush(GeneralStyle.SoftLine))g.FillPath(brush,path);
        float width=track.Width*(Indeterminate ? .16f : Value/100f);
        if(width>0)using(var path=GeneralStyle.Round(new RectangleF(0,27,width,6),3))using(var brush=new SolidBrush(GeneralStyle.Accent))g.FillPath(brush,path);
    }
}

internal sealed class GeneralMenuRenderer : ToolStripProfessionalRenderer {
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){e.Graphics.Clear(GeneralStyle.Surface);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){using(var p=new Pen(GeneralStyle.Line))using(var shape=GeneralStyle.Round(new RectangleF(0,0,e.ToolStrip.Width-1,e.ToolStrip.Height-1),16))e.Graphics.DrawPath(p,shape);}
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
        if(!e.Item.Selected)return;
        using(var brush=new SolidBrush(GeneralStyle.Selection))using(var shape=GeneralStyle.Round(new RectangleF(6,2,e.Item.Width-12,e.Item.Height-4),12))e.Graphics.FillPath(brush,shape);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?GeneralStyle.Foreground:GeneralStyle.Muted;base.OnRenderItemText(e);}
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var p=new Pen(GeneralStyle.SoftLine))e.Graphics.DrawLine(p,14,e.Item.Height/2,e.Item.Width-14,e.Item.Height/2);}
    internal static ContextMenuStrip Create(){
        var menu=new ContextMenuStrip{Renderer=new GeneralMenuRenderer(),ShowImageMargin=false,DropShadowEnabled=false,Font=GeneralStyle.Font(12),Padding=new Padding(6),BackColor=GeneralStyle.Surface};
        menu.Opened+=(s,e)=>{using(var path=GeneralStyle.Round(new RectangleF(0,0,menu.Width,menu.Height),16))menu.Region=new Region(path);};
        return menu;
    }
    internal static ToolStripItem Add(ContextMenuStrip menu,string text,Action action){
        var item=menu.Items.Add(text);item.AutoSize=false;item.Size=new Size(254,44);item.Click+=(s,e)=>action();return item;
    }
}
