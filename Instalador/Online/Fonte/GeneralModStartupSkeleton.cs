using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed class GeneralModStartupSkeleton : Control {
    readonly Stopwatch clock=new Stopwatch();
    readonly Timer timer=new Timer{Interval=40};

    internal GeneralModStartupSkeleton(){
        Name="StartupModSkeleton";
        AccessibleName=GeneralStyle.T("Carregando projeto","Loading project");
        BackColor=GeneralStyle.Background;
        AccessibleRole=AccessibleRole.StaticText;
        Dock=DockStyle.Fill;
        TabStop=false;
        SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|
            ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw|ControlStyles.Opaque,true);
        SetStyle(ControlStyles.Selectable,false);
        timer.Tick+=(s,e)=>Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e){
        base.OnVisibleChanged(e);
        if(Visible&&!SystemInformation.HighContrast&&SystemInformation.IsMenuAnimationEnabled){clock.Restart();timer.Start();}
        else{timer.Stop();clock.Reset();}
    }

    protected override void OnPaint(PaintEventArgs e){
        PaintFrame(e,clock.IsRunning?clock.ElapsedMilliseconds:(long?)null);
    }

    internal void PaintFrame(PaintEventArgs e,long? elapsed){
        var graphics=e.Graphics;
        using(var background=new SolidBrush(GeneralStyle.Background))graphics.FillRectangle(background,e.ClipRectangle);
        int left=Scale(32),right=Width-left,top=Scale(140),bottom=Height-Scale(134);
        var table=Rectangle.FromLTRB(left,top,right,bottom);
        if(table.Width<Scale(500)||table.Height<Scale(170))return;
        using(var surface=new SolidBrush(GeneralStyle.Surface))graphics.FillRectangle(surface,table);
        using(var bars=new GraphicsPath()){
            AddHeader(bars,left,right);
            AddTable(bars,table);
            AddFooter(bars,left,right,bottom);
            graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using(var tone=new SolidBrush(GeneralStyle.SoftLine))graphics.FillPath(tone,bars);
            if(elapsed.HasValue)PaintShine(graphics,bars,elapsed.Value);
        }
    }

    int Scale(int value){return value*DeviceDpi/96;}

    void AddHeader(GraphicsPath bars,int left,int right){
        AddBar(bars,left,Scale(24),Scale(44),Scale(44));
        AddBar(bars,left+Scale(56),Scale(31),Scale(264),Scale(30));
        AddBar(bars,right-Scale(208),Scale(24),Scale(108),Scale(44));
        AddBar(bars,right-Scale(78),Scale(37),Scale(18),Scale(18));
        AddBar(bars,right-Scale(34),Scale(37),Scale(18),Scale(18));
        AddBar(bars,left,Scale(78),Scale(292),Scale(10));
        AddBar(bars,left,Scale(111),Scale(426),Scale(10));
    }

    void AddTable(GraphicsPath bars,Rectangle table){
        int titleX=table.Left+Scale(14),right=table.Right;
        AddBar(bars,titleX,table.Top+Scale(17),Scale(264),Scale(20));
        AddBar(bars,right-Scale(76),table.Top+Scale(19),Scale(14),Scale(14));
        AddBar(bars,right-Scale(32),table.Top+Scale(19),Scale(14),Scale(14));
        AddBar(bars,titleX,table.Top+Scale(68),table.Width-Scale(28),Scale(44));

        int headerY=table.Top+Scale(126),rowHeight=Scale(44),height=Scale(10);
        int markX=table.Left+Scale(40),nameX=table.Left+Scale(75),originX=right-Scale(36);
        int statusX=originX-Math.Min(Scale(235),table.Width/4);
        int sizeX=statusX-Math.Min(Scale(155),table.Width/6);
        int nameWidth=Math.Min(sizeX-nameX-Scale(20),Scale(320));
        int shortBar=Scale(62);
        foreach(int x in new[]{nameX,sizeX,statusX})AddBar(bars,x,headerY+Scale(17),shortBar,height);
        int sectionY=headerY+rowHeight;
        AddBar(bars,titleX,sectionY+Scale(19),Scale(140),Scale(14));
        for(int y=sectionY+Scale(54),row=0;y+rowHeight<=table.Bottom;y+=rowHeight,row++){
            int middle=y+(rowHeight-height)/2;
            AddBar(bars,markX,middle,height,height);
            AddBar(bars,nameX,middle,nameWidth*(5+row%4)/10,height);
            AddBar(bars,sizeX,middle,shortBar/2,height);
            AddBar(bars,statusX,middle,shortBar,height);
            AddBar(bars,originX,middle,height,height);
        }
    }

    void AddFooter(GraphicsPath bars,int left,int right,int top){
        int gap=Scale(16),half=(right-left-gap)/2;
        foreach(int x in new[]{left,left+half+gap}){
            AddBar(bars,x,top+Scale(13),Math.Min(Scale(180),half),Scale(10));
            AddBar(bars,x,top+Scale(31),half,Scale(6));
        }
        int width=Scale(226),height=Scale(48);
        AddBar(bars,left,Height-Scale(78),width,height);
        AddBar(bars,right-width,Height-Scale(78),width,height);
    }

    static void AddBar(GraphicsPath bars,int x,int y,int width,int height){
        if(width<=0||height<=0)return;
        using(var shape=GeneralStyle.Round(new Rectangle(x,y,width,height),height/2f))bars.AddPath(shape,false);
    }

    void PaintShine(Graphics graphics,GraphicsPath bars,long elapsed){
        int width=Math.Max(1,Width/3);
        int left=(int)((Width+width)*(elapsed%1450)/1450)-width;
        var band=new Rectangle(left,0,width,Height);
        var ink=GeneralStyle.Dark?GeneralStyle.Line:Color.White;
        var transparent=Color.FromArgb(0,ink);
        var state=graphics.Save();
        try{
            graphics.SetClip(band,CombineMode.Intersect);
            using(var gradient=new LinearGradientBrush(band,transparent,transparent,LinearGradientMode.Horizontal)){
                gradient.InterpolationColors=new ColorBlend{
                    Positions=new[]{0f,.5f,1f},
                    Colors=new[]{transparent,Color.FromArgb(105,ink),transparent}
                };
                graphics.FillPath(gradient,bars);
            }
        }finally{graphics.Restore(state);}
    }

    protected override void Dispose(bool disposing){
        if(disposing)timer.Dispose();
        base.Dispose(disposing);
    }
}
