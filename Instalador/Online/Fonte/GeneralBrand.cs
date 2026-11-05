using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("Mod Loader")]
[assembly: AssemblyProduct("Mod Loader")]
[assembly: AssemblyVersion(GeneralUpdateChecker.CurrentVersion+".0")]
[assembly: AssemblyFileVersion(GeneralUpdateChecker.CurrentVersion+".0")]

internal static class ModLoaderBrand {
    sealed class Shape {internal Color Fill;internal GraphicsPath Path;}
    static readonly List<Shape> shapes=new List<Shape>();
    static readonly Dictionary<string,Icon> icons=new Dictionary<string,Icon>();
    static readonly float width,height;
    [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
    internal static Icon AppIcon {
        get {
            string key=GeneralStyle.Foreground.ToArgb()+":"+GeneralStyle.Accent.ToArgb();Icon icon;
            if(!icons.TryGetValue(key,out icon)){
                using(var bitmap=new Bitmap(32,32)){
                    using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Transparent);DrawIcon(graphics,new Rectangle(0,0,32,32),true);}
                    IntPtr native=bitmap.GetHicon();
                    try{using(var borrowed=Icon.FromHandle(native))icon=(Icon)borrowed.Clone();}finally{DestroyIcon(native);}
                }
                icons.Add(key,icon);
            }
            return icon;
        }
    }
    static ModLoaderBrand(){
        var document=new XmlDocument{XmlResolver=null};
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ModLoader.Logo.svg"))
        using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))document.Load(reader);
        var root=document.DocumentElement;
        var view=Regex.Split(root.GetAttribute("viewBox").Trim(),@"[\s,]+").Select(v=>Single.Parse(v,CultureInfo.InvariantCulture)).ToArray();
        if(view.Length!=4||view[0]!=0||view[1]!=0||view[2]!=440||view[3]!=400)throw new InvalidDataException("Logo viewBox inválido.");
        width=view[2];height=view[3];
        foreach(XmlElement element in root.SelectNodes(".//*[local-name()='path']")){
            var group=element.ParentNode as XmlElement;
            string fill=element.HasAttribute("fill")?element.GetAttribute("fill"):group.GetAttribute("fill");
            if(fill!="#194660"&&fill!="#76C5E3")throw new InvalidDataException("Cor da logo inválida.");
            shapes.Add(new Shape{Fill=ColorTranslator.FromHtml(fill),Path=ReadPath(element.GetAttribute("d"))});
        }
        if(shapes.Count!=4)throw new InvalidDataException("Logo incompleta.");
    }
    // Only absolute M/L/A/Z commands from the embedded approved artwork.
    static GraphicsPath ReadPath(string data){
        var tokens=Regex.Matches(data,@"[MLAZ]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?").Cast<Match>().Select(m=>m.Value).ToArray();
        if(Regex.Replace(data,@"[MLAZ]|[-+]?(?:\d*\.\d+|\d+\.?\d*)(?:[eE][-+]?\d+)?|[\s,]+","").Length!=0)throw new InvalidDataException("Comando SVG não suportado.");
        int at=0;var result=new GraphicsPath();var point=new PointF();
        Func<float> number=()=>{if(at>=tokens.Length)throw new InvalidDataException("Coordenada SVG ausente.");return Single.Parse(tokens[at++],CultureInfo.InvariantCulture);};
        while(at<tokens.Length){
            string command=tokens[at++];
            if(command=="Z"){result.CloseFigure();continue;}
            if(command=="M"){point=new PointF(number(),number());result.StartFigure();continue;}
            if(command=="L"){var next=new PointF(number(),number());result.AddLine(point,next);point=next;continue;}
            if(command!="A")throw new InvalidDataException("Comando SVG inválido.");
            float radius=number(),ry=number(),rotation=number(),large=number(),sweep=number();var end=new PointF(number(),number());
            if(radius<=0||Math.Abs(radius-ry)>.00001||rotation!=0||(large!=0&&large!=1)||(sweep!=0&&sweep!=1))throw new InvalidDataException("Arco SVG inválido.");
            double dx=(point.X-end.X)/2d,dy=(point.Y-end.Y)/2d,distance=dx*dx+dy*dy;
            if(distance<1e-12){point=end;continue;}
            double r=Math.Max(radius,Math.Sqrt(distance)),factor=(large==sweep?-1:1)*Math.Sqrt(Math.Max(0,(r*r-distance)/distance));
            double cx=(point.X+end.X)/2d+factor*dy,cy=(point.Y+end.Y)/2d-factor*dx;
            double start=Math.Atan2(point.Y-cy,point.X-cx),finish=Math.Atan2(end.Y-cy,end.X-cx),delta=finish-start;
            if(sweep==1&&delta<0)delta+=2*Math.PI;if(sweep==0&&delta>0)delta-=2*Math.PI;
            result.AddArc((float)(cx-r),(float)(cy-r),(float)(2*r),(float)(2*r),(float)(start*180/Math.PI),(float)(delta*180/Math.PI));point=end;
        }
        return result;
    }
    internal static void Draw(Graphics graphics,Rectangle bounds,bool themed=false){DrawCore(graphics,bounds,themed,false);}
    internal static void DrawIcon(Graphics graphics,Rectangle bounds,bool themed=false){DrawCore(graphics,bounds,themed,true);}
    static void DrawCore(Graphics graphics,Rectangle bounds,bool themed,bool square){
        if(bounds.Width<=0||bounds.Height<=0)return;
        float canvasHeight=square?440:height,scale=Math.Min(bounds.Width/width,bounds.Height/canvasHeight);var saved=graphics.Save();
        try{
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X+(bounds.Width-width*scale)/2,bounds.Y+(bounds.Height-canvasHeight*scale)/2);graphics.ScaleTransform(scale,scale);
            if(square)graphics.TranslateTransform(-7.5f,20);
            foreach(var shape in shapes){
                Color fill=themed?(shape.Fill.ToArgb()==ColorTranslator.FromHtml("#76C5E3").ToArgb()?GeneralStyle.Accent:GeneralStyle.Foreground):shape.Fill;
                using(var brush=new SolidBrush(fill))graphics.FillPath(brush,shape.Path);
            }
        }finally{graphics.Restore(saved);}
    }
}

internal sealed class ModLoaderLogo : Control {
    internal ModLoaderLogo(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;AccessibleName="Mod Loader";TabStop=false;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);ModLoaderBrand.Draw(e.Graphics,ClientRectangle,true);}
}
