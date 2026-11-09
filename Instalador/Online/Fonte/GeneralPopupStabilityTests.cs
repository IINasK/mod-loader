using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal static class GeneralPopupStabilityTests {
    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint command);
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"popup-stability-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        bool dark=GeneralStyle.Dark,red=GeneralStyle.RedTheme,black=GeneralStyle.BlackTheme,animations=GeneralStyle.AnimationsEnabled;string language=GeneralStyle.Language;
        Exception failure=null;bool completed=false;
        var thread=new System.Threading.Thread(()=>{
            try{
                using(var owner=new Form{ClientSize=new Size(620,420),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)}){
                    owner.Shown+=async(s,e)=>{
                        try{
                            var desktop=new Rectangle(0,0,1366,768);var options=new Rectangle(100,100,620,420);
                            var languageAnchor=new Rectangle(420,226,268,44);var pickerSize=new Size(300,146);
                            var placed=GeneralPopupPlacement.Resolve(languageAnchor,pickerSize,options,desktop,false);
                            var bounds=new Rectangle(placed,pickerSize);
                            check(bounds.Right<=options.Right-24&&bounds.Left>=options.Left+24&&bounds.Top>=options.Top+24&&bounds.Bottom<=options.Bottom-24,"Options620x420: language dropdown keeps24px clear of all owner edges.");
                            var rightOwner=new Rectangle(100,100,620,420);var large=new Size(400,450);
                            var toRight=new Rectangle(GeneralPopupPlacement.Resolve(languageAnchor,large,rightOwner,desktop,false),large);
                            check(toRight.Left>=rightOwner.Right+24&&ScreenContains(desktop,toRight),"A dropdown too tall for its owner moves clearly outside to the right.");
                            var leftOwner=new Rectangle(700,100,620,420);
                            var toLeft=new Rectangle(GeneralPopupPlacement.Resolve(new Rectangle(1100,200,180,44),large,leftOwner,desktop,true),large);
                            check(toLeft.Right<=leftOwner.Left-24&&ScreenContains(desktop,toLeft),"When the right side has no room, the popup moves24px outside to the left.");
                            var fallbackSize=new Size(900,650);
                            var fallback=new Rectangle(GeneralPopupPlacement.Resolve(new Rectangle(850,550,90,44),fallbackSize,new Rectangle(16,16,900,600),desktop,true),fallbackSize);
                            check(ScreenContains(desktop,fallback),"Screen fallback keeps16px margins when no owner-relative placement fits.");
                            var edgeOwner=new Rectangle(800,100,540,600);var menuSize=new Size(330,440);
                            var edgeMenu=new Rectangle(GeneralPopupPlacement.Resolve(new Rectangle(1260,110,44,44),menuSize,edgeOwner,desktop,true),menuSize);
                            check(edgeMenu.Right<=edgeOwner.Right-24&&edgeMenu.Left>=edgeOwner.Left+24&&ScreenContains(desktop,edgeMenu),"A menu anchored near the right window edge keeps a deliberate inset.");
                            var noOwner=new Rectangle(GeneralPopupPlacement.Resolve(new Rectangle(1350,750,1,1),new Size(300,200),Rectangle.Empty,desktop,false),new Size(300,200));
                            check(ScreenContains(desktop,noOwner),"A popup without an owner remains inside the working area.");

                            foreach(string theme in new[]{"light","dark","light-red","black"}){
                                GeneralStyle.UseTheme(theme);
                                foreach(var size in new[]{new Size(120,80),new Size(280,180)})using(var bitmap=new Bitmap(size.Width,size.Height))using(var graphics=Graphics.FromImage(bitmap)){
                                    graphics.Clear(Color.Magenta);GeneralStyle.PaintCard(graphics,size);
                                    CheckBand(bitmap,theme+" card "+size.Width,check);
                                    check(bitmap.GetPixel(0,0).ToArgb()==GeneralStyle.Surface.ToArgb(),"Card corners clear old pixels in "+theme+" at "+size.Width+"px.");
                                    bitmap.Save(Path.Combine(root,"card-"+theme+"-"+size.Width+".png"),System.Drawing.Imaging.ImageFormat.Png);
                                }
                                foreach(int width in new[]{120,280}){
                                    using(var surface=new GeneralSurface{Size=new Size(width,44),BackColor=GeneralStyle.Surface}){
                                        surface.Controls.Add(new Panel{Dock=DockStyle.Fill,BackColor=GeneralStyle.Surface});
                                        CheckControl(surface,theme+" filled surface "+width,check);
                                    }
                                    using(var button=new GeneralButton{Size=new Size(width,44),Text=""})CheckControl(button,theme+" button "+width,check);
                                    using(var picker=new GeneralPicker{Size=new Size(width,44)})CheckControl(picker,theme+" picker "+width,check);
                                }
                            }
                            GeneralStyle.UseTheme("light-red");GeneralStyle.AnimationsEnabled=true;
                            using(var quick=new GeneralDialog("Quick toggle"){ClientSize=new Size(320,210),StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)}){
                                quick.Show(owner);
                                const int extendedStyle=-20,appWindow=0x40000;
                                const uint ownerWindow=4;
                                check(!quick.ShowInTaskbar&&(GetWindowLong(quick.Handle,extendedStyle)&appWindow)==0&&GetWindow(quick.Handle,ownerWindow)==owner.Handle,"Owned popups have no separate Windows taskbar window.");
                                check(HasSmallRegion(quick),"A just-opened dialog is still clipped before the first expansion tick.");
                                quick.Complete(DialogResult.Cancel);check(HasSmallRegion(quick),"Immediate close contracts from current progress without jumping to a full card.");
                                await Task.Delay(270);check(!quick.Visible,"Immediate open/close finishes normally without applying anything.");
                            }
                            for(int cycle=0;cycle<2;cycle++)using(var dialog=new GeneralDialog("Animation"){ClientSize=new Size(320,210),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)}){
                                bool beforeShown=false,atShown=false;
                                dialog.VisibleChanged+=(ss,ee)=>{if(dialog.Visible)beforeShown=HasSmallRegion(dialog);};
                                dialog.Shown+=(ss,ee)=>atShown=HasSmallRegion(dialog);
                                dialog.Show(owner);Application.DoEvents();
                                check(beforeShown&&atShown,"Dialog prepares a small region before its first visible/shown event, cycle"+cycle+".");
                                await Task.Delay(270);check(HasFullCenter(dialog),"Dialog opening animation reaches the full card, cycle"+cycle+".");
                                dialog.Complete(DialogResult.Cancel);check(dialog.Visible,"Dialog keeps its closing animation instead of disappearing immediately.");
                                await Task.Delay(270);check(!dialog.Visible,"Dialog closing animation finishes without reopening.");
                            }
                            using(var menu=new GeneralPopupMenu{ShowImageMargin=false,DropShadowEnabled=false}){
                                int openings=0,clicks=0;bool openedPrepared=true;
                                var item=menu.Items.Add("One");item.Click+=(ss,ee)=>clicks++;
                                menu.Opening+=(ss,ee)=>openings++;
                                menu.Opened+=(ss,ee)=>openedPrepared&=HasFullCenter(menu);
                                for(int cycle=0;cycle<2;cycle++){
                                    menu.Show(owner,new Point(20,20));
                                    check(HasFullCenter(menu),"Context popup opens completely, cycle"+cycle+".");menu.Close();check(!menu.Visible,"Context popup closes in the same turn.");
                                }
                                check(openings==2&&openedPrepared&&clicks==0,"Reopening shows the complete rounded menu and never invokes an item.");
                            }
                            using(var picker=new GeneralPicker{Size=new Size(268,44),Location=new Point(320,126)}){
                                owner.Controls.Add(picker);picker.Items.AddRange(new object[]{"Português","English"});picker.SelectedIndex=0;
                                var expected=GeneralPopupPlacement.Location(picker,picker.ClientRectangle,new Size(300,104),false);
                                picker.Toggle();var popup=picker.OpenPopup;check(popup.Visible&&HasFullCenter(popup),"Picker shows a complete card from the first frame.");
                                check(popup.Location==expected,"Picker.Show uses the shared screen placement instead of the old fixed offset.");
                                check(!popup.Region.IsVisible(0,0),"Picker clips the corners before display.");
                                picker.Toggle();check(!popup.Visible&&picker.SelectedIndex==0,"Closing the picker does not change its selection.");
                                picker.Toggle();var reopened=picker.OpenPopup;check(reopened!=popup&&HasFullCenter(reopened),"A reopened picker shows the complete card without a reveal timer.");
                                picker.Toggle();check(!reopened.Visible&&picker.SelectedIndex==0,"Repeated picker opening/closing preserves the chosen language.");
                            }
                            completed=true;
                        }catch(Exception ex){failure=ex;}finally{Application.ExitThread();}
                    };
                    Application.Run(owner);
                }
            }catch(Exception ex){failure=ex;}
        });
        thread.IsBackground=true;thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.Start();
        try{if(!thread.Join(60000))throw new IOException("Popup stability tests timed out.");if(failure!=null)throw failure;if(!completed)throw new IOException("Popup stability tests did not finish.");}
        finally{GeneralStyle.Dark=dark;GeneralStyle.RedTheme=red;GeneralStyle.BlackTheme=black;GeneralStyle.Language=language;GeneralStyle.AnimationsEnabled=animations;}
        Safe.WriteJson(Path.Combine(root,"resultado-popup-stability.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static bool ScreenContains(Rectangle screen,Rectangle popup){screen.Inflate(-16,-16);return screen.Contains(popup);}
    static bool HasSmallRegion(Control control){return control.Region!=null&&!control.Region.IsVisible(control.ClientSize.Width/2,control.ClientSize.Height/2);}
    static bool HasFullCenter(Control control){return control.Region!=null&&control.Region.IsVisible(control.ClientSize.Width/2,control.ClientSize.Height/2);}
    static void CheckControl(Control control,string label,Action<bool,string> check){
        using(var bitmap=new Bitmap(control.Width,control.Height)){control.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));CheckBand(bitmap,label,check);}
    }
    static void CheckBand(Bitmap bitmap,string label,Action<bool,string> check){
        int x=bitmap.Width/2,y=bitmap.Height/2;
        int inset=GeneralStyle.RoundedFaceInset;
        Func<int,int[]> edges=depth=>new[]{bitmap.GetPixel(x,depth).ToArgb(),bitmap.GetPixel(x,bitmap.Height-1-depth).ToArgb(),
            bitmap.GetPixel(depth,y).ToArgb(),bitmap.GetPixel(bitmap.Width-1-depth,y).ToArgb()};
        check(edges(0).All(pixel=>pixel==bitmap.GetPixel(0,0).ToArgb()),label+": outer pixels leave space on all four edges.");
        check(edges(inset+1).All(pixel=>pixel==GeneralStyle.Line.ToArgb()),label+": all four edges retain the full border. "+String.Join("/",edges(inset+1).Select(pixel=>pixel.ToString("X8"))));
        check(edges(inset+3).All(pixel=>pixel==GeneralStyle.Surface.ToArgb()),label+": fill starts cleanly after the border.");
    }
}
