using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;

[assembly: AssemblyTitle("Mod Loader")]
[assembly: AssemblyProduct("Mod Loader")]
[assembly: AssemblyVersion(GeneralUpdateChecker.CurrentVersion+".0")]
[assembly: AssemblyFileVersion(GeneralUpdateChecker.CurrentVersion+".0")]

internal static class ModLoaderBrand {
    sealed class Shape {internal Color Fill;internal PointF[] Points;}
    static readonly List<Shape> shapes=new List<Shape>();
    static readonly float width,height;
    static readonly Color background;
    internal static readonly Icon AppIcon;
    static ModLoaderBrand(){
        var document=new XmlDocument{XmlResolver=null};
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ModLoader.Logo.svg"))
        using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))document.Load(reader);
        var root=document.DocumentElement;var view=Numbers(root.GetAttribute("viewBox"));
        if(view.Length!=4||view[0]!=0||view[1]!=0||view[2]<=0||view[3]<=0)throw new InvalidDataException("Logo viewBox inválido.");
        width=view[2];height=view[3];
        foreach(XmlNode node in root.ChildNodes){
            var element=node as XmlElement;if(element==null)continue;
            if(element.LocalName=="rect")background=ColorTranslator.FromHtml(element.GetAttribute("fill"));
            else if(element.LocalName=="polygon"){
                var numbers=Numbers(element.GetAttribute("points"));
                if(numbers.Length<6||numbers.Length%2!=0)throw new InvalidDataException("Logo polygon inválido.");
                shapes.Add(new Shape{Fill=ColorTranslator.FromHtml(element.GetAttribute("fill")),Points=Enumerable.Range(0,numbers.Length/2).Select(i=>new PointF(numbers[i*2],numbers[i*2+1])).ToArray()});
            }
        }
        if(shapes.Count==0)throw new InvalidDataException("Logo vazia.");
        using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ModLoader.Logo.ico"))
        using(var icon=new Icon(stream,new Size(32,32)))AppIcon=(Icon)icon.Clone();
    }
    static float[] Numbers(string value){return Regex.Split(value.Trim(),@"[\s,]+").Select(part=>Single.Parse(part,CultureInfo.InvariantCulture)).ToArray();}
    internal static void Draw(Graphics graphics,Rectangle bounds){
        if(bounds.Width<=0||bounds.Height<=0)return;
        float scale=Math.Min(bounds.Width/width,bounds.Height/height);
        var saved=graphics.Save();
        try{
            using(var brush=new SolidBrush(background))graphics.FillRectangle(brush,bounds);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            graphics.TranslateTransform(bounds.X+(bounds.Width-width*scale)/2,bounds.Y+(bounds.Height-height*scale)/2);graphics.ScaleTransform(scale,scale);
            foreach(var shape in shapes)using(var brush=new SolidBrush(shape.Fill))graphics.FillPolygon(brush,shape.Points);
        }finally{graphics.Restore(saved);}
    }
}

internal sealed class ModLoaderLogo : Control {
    internal ModLoaderLogo(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);AccessibleName="Mod Loader";TabStop=false;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);ModLoaderBrand.Draw(e.Graphics,ClientRectangle);}
}
