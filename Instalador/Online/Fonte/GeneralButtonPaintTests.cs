using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;

internal static class GeneralButtonPaintTests {
    internal static int Run(string parent){
        int passed=0;
        var records=new List<object>();
        bool before=GeneralStyle.Dark;
        try{
            foreach(bool dark in new[]{false,true})foreach(int kind in new[]{0,1,2}){
                bool primary=kind!=0;
                GeneralStyle.Dark=dark;
                using(var surface=new Panel{BackColor=GeneralStyle.Background,Size=new Size(360,120)})
                using(var nested=new Panel{BackColor=Color.Transparent,Size=surface.Size})
                using(var button=new GeneralButton{Text="Teste",Primary=primary,Destructive=kind==2,Size=new Size(140,44)}){
                    surface.Controls.Add(nested);nested.Controls.Add(button);
                    foreach(string state in new[]{"normal","hover","pressed","disabled","resized","repaint"}){
                        button.Enabled=state!="disabled";
                        if(state=="hover")typeof(Control).GetMethod("OnMouseEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{EventArgs.Empty});
                        if(state=="pressed")typeof(Control).GetMethod("OnMouseDown",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{new MouseEventArgs(MouseButtons.Left,1,20,20,0)});
                        if(state=="resized")button.Size=new Size(230,52);
                        using(var bitmap=new Bitmap(button.Width,button.Height))
                        using(var graphics=Graphics.FromImage(bitmap)){
                            graphics.Clear(Color.Magenta);
                            typeof(GeneralButton).GetMethod("OnPaint",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(button,new object[]{new PaintEventArgs(graphics,button.ClientRectangle)});
                            var expected=surface.BackColor.ToArgb();
                            foreach(var point in new[]{new Point(0,0),new Point(bitmap.Width-1,0),new Point(0,bitmap.Height-1),new Point(bitmap.Width-1,bitmap.Height-1)})
                                if(bitmap.GetPixel(point.X,point.Y).ToArgb()!=expected)throw new InvalidOperationException("Restos de pintura no canto: "+state);
                            var center=bitmap.GetPixel(bitmap.Width/2,8);
                            if(center.ToArgb()==Color.Magenta.ToArgb())throw new InvalidOperationException("A face do botão não foi pintada.");
                            if(kind==2&&button.Enabled&&center.ToArgb()!=GeneralStyle.DangerFill.ToArgb())throw new InvalidOperationException("A ação destrutiva preenchida precisa manter sua cor própria.");
                            passed++;records.Add(new{Dark=dark,Primary=primary,Destructive=kind==2,State=state,OpaqueCorners=true});
                        }
                    }
                }
            }
        }finally{GeneralStyle.Dark=before;}
        Safe.WriteJson(System.IO.Path.Combine(parent,"resultado-pintura-botoes.json"),new{Passed=true,Tests=records,RealGameTouched=false});
        return passed;
    }
}
