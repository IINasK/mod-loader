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
    internal static bool RedTheme;
    internal static void UseTheme(string theme){Dark=theme=="dark";RedTheme=theme=="light-red";}
    internal static string Language="pt";
    internal static bool AnimationsEnabled=true;
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
    internal static Color Foreground {get{return ColorTranslator.FromHtml(Dark?"#ece8f6":RedTheme?"#2b2326":"#221d33");}}
    internal static Color Muted {get{return ColorTranslator.FromHtml(Dark?"#b9b1cf":RedTheme?"#756167":"#625b7c");}}
    internal static Color Accent {get{return ColorTranslator.FromHtml(Dark?"#b8a4ff":RedTheme?"#d92e42":"#5b3fd8");}}
    internal static Color Primary {get{return ColorTranslator.FromHtml(!Dark&&RedTheme?"#d92e42":"#5b3fd8");}}
    internal static Color Line {get{return ColorTranslator.FromHtml(Dark?"#827592":RedTheme?"#a5747d":"#897d9d");}}
    internal static Color SoftLine {get{return ColorTranslator.FromHtml(Dark?"#3a334e":RedTheme?"#f0dde1":"#e9e5f0");}}
    internal static Color Selection {get{return ColorTranslator.FromHtml(Dark?"#3e335b":RedTheme?"#fde9ec":"#eee9fc");}}
    internal static Color Danger {get{return ColorTranslator.FromHtml(Dark?"#ffadb9":"#b42348");}}
    internal static GraphicsPath Round(RectangleF box,float radius){
        float d=Math.Min(radius*2,Math.Min(box.Width,box.Height));
        var path=new GraphicsPath();
        if(d<=0){path.AddRectangle(box);return path;}
        path.AddArc(box.X,box.Y,d,d,180,90);path.AddArc(box.Right-d,box.Y,d,d,270,90);
        path.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);path.AddArc(box.X,box.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    internal static void PaintCard(Graphics graphics,Size size){
        graphics.SmoothingMode=SmoothingMode.AntiAlias;graphics.Clear(Surface);
        using(var outer=Round(new RectangleF(0,0,size.Width,size.Height),18))using(var border=new SolidBrush(Line))graphics.FillPath(border,outer);
        using(var inner=Round(new RectangleF(1,1,Math.Max(1,size.Width-2),Math.Max(1,size.Height-2)),17))using(var fill=new SolidBrush(Surface))graphics.FillPath(fill,inner);
    }
    internal static void ClipCardBody(Control control){
        using(var shape=Round(new RectangleF(0,0,Math.Max(1,control.Width),Math.Max(1,control.Height)),16)){
            var previous=control.Region;control.Region=new Region(shape);if(previous!=null)previous.Dispose();
        }
    }
    internal static Button Button(string text,Action action,bool primary=false){
        var b=new GeneralButton{Text=text,Primary=primary,AutoSize=true,MinimumSize=new Size(80,44),Padding=new Padding(16,0,16,0)};
        b.Click+=(s,e)=>action();return b;
    }
    internal static void Apply(Control control,bool dark){
        Dark=dark;
        control.BackColor=control is Form?Background:Surface;control.ForeColor=Foreground;
        if(control.Font==null||control.Font.FontFamily.Name=="Microsoft Sans Serif"||control.Font.FontFamily.Name=="Segoe UI")control.Font=Font(control.Font==null?12:control.Font.Size,control.Font!=null&&control.Font.Bold);
        var button=control as Button;
        if(button!=null){button.MinimumSize=new Size(Math.Max(button.MinimumSize.Width,44),Math.Max(button.MinimumSize.Height,44));button.FlatStyle=FlatStyle.Flat;button.FlatAppearance.BorderColor=Line;button.FlatAppearance.BorderSize=1;button.UseVisualStyleBackColor=false;}
        var combo=control as GeneralPicker;
        if(combo!=null){combo.BackColor=Surface;combo.ForeColor=Foreground;}
        var text=control as TextBox;
        if(text!=null){text.BackColor=Surface;text.ForeColor=Foreground;if(!(text.Parent is GeneralPicker)&&!(text.Parent is GeneralSurface))text.BorderStyle=BorderStyle.FixedSingle;}
        var checkbox=control as CheckBox;
        if(checkbox!=null)checkbox.UseVisualStyleBackColor=false;
        var progress=control as GeneralProgress;
        if(progress!=null){progress.Invalidate();return;}
        foreach(Control child in control.Controls)Apply(child,dark);
        GeneralNativeTheme.Apply(control);
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

internal static class GeneralNativeTheme {
    [DllImport("dwmapi.dll")]static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)]static extern int SetWindowTheme(IntPtr window,string application,string classes);
    internal static void ApplyTree(Control control){Apply(control);foreach(Control child in control.Controls)ApplyTree(child);}
    internal static void Apply(Control control){
        control.HandleCreated-=Created;control.HandleCreated+=Created;
        if(!control.IsHandleCreated)return;
        try{
            if(control is Form){
                int dark=GeneralStyle.Dark?1:0;
                if(DwmSetWindowAttribute(control.Handle,20,ref dark,4)!=0)DwmSetWindowAttribute(control.Handle,19,ref dark,4);
                int caption=GeneralStyle.Dark?ColorTranslator.ToWin32(GeneralStyle.Background):-1,text=GeneralStyle.Dark?ColorTranslator.ToWin32(GeneralStyle.Foreground):-1;
                DwmSetWindowAttribute(control.Handle,35,ref caption,4);DwmSetWindowAttribute(control.Handle,36,ref text,4);
            }
            if(control is ScrollBar||control is TextBox||control is ListBox||control is DataGridView){
                SetWindowTheme(control.Handle,GeneralStyle.Dark?"DarkMode_Explorer":"Explorer",null);
            }
        }catch(DllNotFoundException){}catch(EntryPointNotFoundException){}
    }
    static void Created(object sender,EventArgs e){Apply((Control)sender);}
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
    internal bool Primary,Destructive,Bare;
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
            if(!Bare){
            using(var brush=new SolidBrush(fill))g.FillPath(brush,path);
            using(var pen=new Pen(Focused?GeneralStyle.Accent:Primary?fill:GeneralStyle.Line,Focused?2:1))g.DrawPath(pen,path);
            }else if(Focused&&ShowFocusCues){using(var pen=new Pen(GeneralStyle.Accent,2))g.DrawLine(pen,Width/2-10,Height-6,Width/2+10,Height-6);}
        }
        var textRect=new Rectangle(12,0,Width-24,Height);
        if(!String.IsNullOrEmpty(IconKind)){
            int x=String.IsNullOrEmpty(Text)?Width/2-9:14,y=Height/2-9;
            LucideIcons.Draw(g,IconKind,new Rectangle(x,y,20,20),ink);
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
        using(var font=GeneralStyle.Font(10.5f))TextRenderer.DrawText(g,Caption??"",font,new Rectangle(0,0,Width-52,23),GeneralStyle.Muted,TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis);
        using(var font=GeneralStyle.Font(10.5f,true))TextRenderer.DrawText(g,Indeterminate?"—":Value+"%",font,new Rectangle(Width-52,0,52,23),GeneralStyle.Accent,TextFormatFlags.Right|TextFormatFlags.SingleLine);
        var track=new RectangleF(0,27,Math.Max(1,Width-1),6);
        using(var path=GeneralStyle.Round(track,3))using(var brush=new SolidBrush(GeneralStyle.SoftLine))g.FillPath(brush,path);
        float width=track.Width*(Indeterminate ? .16f : Value/100f);
        if(width>0)using(var path=GeneralStyle.Round(new RectangleF(0,27,width,6),3))using(var brush=new SolidBrush(GeneralStyle.Accent))g.FillPath(brush,path);
    }
}

internal sealed class GeneralMenuRenderer : ToolStripProfessionalRenderer {
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e){GeneralStyle.PaintCard(e.Graphics,e.ToolStrip.Size);}
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e){}
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e){
        if(!e.Item.Selected)return;
        using(var brush=new SolidBrush(GeneralStyle.Selection))using(var shape=GeneralStyle.Round(new RectangleF(6,2,e.Item.Width-12,e.Item.Height-4),12))e.Graphics.FillPath(brush,shape);
    }
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e){e.TextColor=e.Item.Enabled?GeneralStyle.Foreground:GeneralStyle.Muted;base.OnRenderItemText(e);}
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e){}
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e){LucideIcons.Draw(e.Graphics,"check",new Rectangle(10,(e.Item.Height-18)/2,18,18),GeneralStyle.Accent);}
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e){using(var p=new Pen(GeneralStyle.SoftLine))e.Graphics.DrawLine(p,14,e.Item.Height/2,e.Item.Width-14,e.Item.Height/2);}
    internal static ContextMenuStrip Create(){
        var menu=new GeneralPopupMenu{Renderer=new GeneralMenuRenderer(),ShowImageMargin=false,DropShadowEnabled=false,Font=GeneralStyle.Font(12),Padding=new Padding(10),BackColor=GeneralStyle.Surface};
        return menu;
    }
    internal static ToolStripItem Add(ContextMenuStrip menu,string text,Action action){
        var item=menu.Items.Add(text);item.AutoSize=false;item.Size=new Size(280,44);item.Padding=new Padding(12,0,12,0);item.Click+=(s,e)=>action();return item;
    }
}
