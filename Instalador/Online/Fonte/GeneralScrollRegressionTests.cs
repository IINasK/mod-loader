using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int ScrollProbeStyle(IntPtr handle,int index);
    [DllImport("user32.dll",EntryPoint="SendMessageW")]static extern IntPtr ScrollProbeMessage(IntPtr handle,int message,IntPtr first,IntPtr second);
    const int ScrollProbeStyles=0x00200000|0x00100000;

    internal static void RunScrollRegressionTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"scroll-regression-");Directory.CreateDirectory(root);
        string game=Directory.CreateDirectory(Safe.In(root,"g")).FullName;
        var state=new GeneralModState{Destination=game,Theme="dark",Language="pt",DownloadFolder=root};
        var checks=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new IOException(name);checks.Add(name);};
        TestForm(FixedData.Load(),state,Safe.In(root,"d"),root,async form=>{
            foreach(string theme in new[]{"dark","black","light"}) {
                form.state.Theme=theme;form.BuildInterface();
                using(var picker=new GeneralPicker{Location=new Point(24,24),Width=340,Name="CategoryScrollFixture",AccessibleName="Categorias"}) {
                    picker.LabelDefaultChoice=false;picker.DeleteChoice=_=>{};
                    foreach(string label in new[]{"Sem categoria","Núcleo e dependências","Jogabilidade","Lojas","Veículos","Aparência",
                        "Modo Foto","Roupas femininas","Roupas masculinas","Roupas para ambas","Áudio","Importados"})picker.Items.Add(label);
                    picker.SelectedIndex=0;form.Controls.Add(picker);picker.BringToFront();picker.Toggle();await Task.Delay(50);
                    var popup=picker.OpenPopup;
                    var view=(GeneralScrollView)popup.Items.OfType<ToolStripControlHost>().Single().Control;
                    var list=(ListBox)view.Content;view.RefreshScroll();
                    check(popup.Visible&&list.Items.Count==12&&view.Bar.Visible&&view.Bar.Limit>0,
                        "The actual category popup exposes its twelve options through one custom scrollbar in "+theme+".");
                    CaptureScrollControl(popup,Safe.In(root,"categories-"+theme+".png"));
                    check((ScrollProbeStyle(list.Handle,-16)&ScrollProbeStyles)==0&&list.Width==list.ClientSize.Width&&list.Width==list.Parent.ClientSize.Width,
                        "No native scrollbar, covered strip or overflow-width compensation remains in "+theme+".");
                    ScrollProbeMessage(list.Handle,0x020a,(IntPtr)(-120<<16),IntPtr.Zero);await Task.Delay(25);view.RefreshScroll();
                    check(list.TopIndex>0&&view.Bar.Value==Math.Min(view.Bar.Limit,list.TopIndex),"Mouse-wheel scrolling still moves the category list and custom thumb in "+theme+".");
                    ScrollProbeMessage(list.Handle,0x0100,(IntPtr)Keys.End,IntPtr.Zero);
                    ScrollProbeMessage(list.Handle,0x0101,(IntPtr)Keys.End,IntPtr.Zero);await Task.Delay(25);view.RefreshScroll();
                    check(list.SelectedIndex==11&&list.GetItemRectangle(11).Bottom<=list.ClientSize.Height+1,
                        "The End key reaches the last complete category without a native scrollbar in "+theme+".");
                    var original=popup.Size;
                    foreach(var size in new[]{new Size(original.Width+60,original.Height+84),new Size(original.Width+20,Math.Min(popup.MaximumSize.Height,650)),original}) {
                        popup.Size=size;await Task.Delay(25);view.RefreshScroll();
                        check((ScrollProbeStyle(list.Handle,-16)&ScrollProbeStyles)==0&&list.Width==list.ClientSize.Width,
                            "Resizing the mounted category popup does not restore or duplicate native scrollbars in "+theme+" at "+size+".");
                    }
                    view.Bar.MoveTo(0);var thumb=view.Bar.Thumb;
                    ScrollProbeMouse(view.Bar,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,thumb.Left+1,thumb.Top+2,0));
                    ScrollProbeMouse(view.Bar,"OnMouseMove",new MouseEventArgs(MouseButtons.Left,0,thumb.Left+1,view.Bar.Height-8,0));
                    ScrollProbeMouse(view.Bar,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,thumb.Left+1,view.Bar.Height-8,0));
                    check(view.Bar.Value>0&&list.TopIndex>0&&!view.Bar.Capture,"Dragging the sole custom thumb scrolls and releases capture in "+theme+".");
                    var filter=typeof(GeneralScrollView).GetField("middleScroll",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null) as IMessageFilter;
                    var message=Message.Create(list.Handle,0x0207,IntPtr.Zero,IntPtr.Zero);
                    bool started=filter.PreFilterMessage(ref message);
                    message=Message.Create(list.Handle,0x0207,IntPtr.Zero,IntPtr.Zero);bool stopped=filter.PreFilterMessage(ref message);
                    check(started&&stopped,"Middle-click scrolling can still start and stop in the category popup in "+theme+".");
                    list.Items.Clear();await Task.Delay(25);view.RefreshScroll();
                    check(!view.Bar.Visible&&(ScrollProbeStyle(list.Handle,-16)&ScrollProbeStyles)==0,
                        "Removing options hides the sole scrollbar without leaving a native scrollbar in "+theme+".");
                    foreach(var label in picker.Items)list.Items.Add(label);await Task.Delay(25);view.RefreshScroll();
                    check(view.Bar.Visible&&view.Bar.Limit>0&&(ScrollProbeStyle(list.Handle,-16)&ScrollProbeStyles)==0&&list.Width==list.ClientSize.Width,
                        "Adding overflowing options restores only the custom scrollbar in "+theme+".");
                    list.BorderStyle=BorderStyle.FixedSingle;list.BorderStyle=BorderStyle.None;await Task.Delay(25);view.RefreshScroll();
                    check((ScrollProbeStyle(list.Handle,-16)&ScrollProbeStyles)==0&&list.Width==list.ClientSize.Width,
                        "Recreating the category list handle preserves the single-bar contract in "+theme+". Styles="+
                        ScrollProbeStyle(list.Handle,-16).ToString("x")+" width="+list.Width+" client="+list.ClientSize.Width);
                    picker.Toggle();check(!popup.Visible,"The category popup still closes through its trigger in "+theme+".");
                }
            }
            form.state.Theme="dark";form.BuildInterface();
            string text=String.Join("\n\n",Enumerable.Range(1,65).Select(index=>"Mod Alpha · ocorrência "+index+"\n"+
                "Esta informação é longa para conferir a quebra de linhas, a seleção e a rolagem sem barras duplicadas."))+"\n\nÚLTIMA LINHA — Mod Alpha";
            using(var dialog=GeneralDialog.CreateMessage(form,text,"Mensagem longa de teste",modNames:new[]{"Mod Alpha"},signal:false)) {
                dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);dialog.Show(form);await Task.Delay(30);
                var body=AllControls(dialog).OfType<RichTextBox>().Single();var view=(GeneralScrollView)body.Parent.Parent;
                int last=body.Text.LastIndexOf("ÚLTIMA LINHA",StringComparison.Ordinal);
                using(var large=new Font(body.Font.FontFamily,22,FontStyle.Bold)) {body.Select(last,body.TextLength-last);body.SelectionFont=large;}
                body.Select(0,0);view.RefreshScroll();await Task.Delay(30);view.RefreshScroll();
                check(body.ScrollBars==RichTextBoxScrollBars.None&&(ScrollProbeStyle(body.Handle,-16)&ScrollProbeStyles)==0&&view.Bar.Visible&&view.Bar.Limit>0,
                    "Formatted message bodies use the common single-bar adapter rather than an inner RichEdit scrollbar.");
                check(body.Text==text,"The scroll adapter preserves all formatted message text.");
                ScrollProbeMessage(body.Handle,0x020a,(IntPtr)(-120<<16),IntPtr.Zero);await Task.Delay(25);view.RefreshScroll();
                check(view.Bar.Value>0,"Mouse-wheel input scrolls a formatted message through the shared bar.");
                view.Bar.MoveTo(view.Bar.Limit);await Task.Delay(25);view.RefreshScroll();
                Point end=body.GetPositionFromCharIndex(body.TextLength-1);
                check(end.Y>=0&&end.Y<body.ClientSize.Height&&view.Bar.Value==view.Bar.Limit,
                    "The final line remains reachable when its font is larger than the body's base font.");
                foreach(var size in new[]{new Size(760,560),new Size(440,300),new Size(620,440)}) {
                    dialog.ClientSize=size;await Task.Delay(30);view.RefreshScroll();view.Bar.MoveTo(view.Bar.Limit);await Task.Delay(25);view.RefreshScroll();
                    end=body.GetPositionFromCharIndex(body.TextLength-1);
                    check((ScrollProbeStyle(body.Handle,-16)&ScrollProbeStyles)==0&&end.Y>=0&&end.Y<body.ClientSize.Height,
                        "Formatted text reflows and keeps the final line reachable after resizing to "+size+".");
                }
                view.Bar.MoveTo(0);body.Select(body.TextLength,0);body.ScrollToCaret();await Task.Delay(25);view.RefreshScroll();
                check(view.Bar.Value>0,"Caret navigation synchronizes the formatted message's custom scrollbar.");
                dialog.Close();
            }
        });
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,NativeWindowsControls=true,SyntheticInputMessages=true});
    }

    static void ScrollProbeMouse(Control control,string method,MouseEventArgs args) {
        control.GetType().GetMethod(method,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(control,new object[]{args});
    }
    static void CaptureScrollControl(Control control,string path) {
        using(var image=new Bitmap(control.Width,control.Height)) {control.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(path,System.Drawing.Imaging.ImageFormat.Png);}
    }
}
