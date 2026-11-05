using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

internal static class GeneralMenuHostTests {
    internal static void Run(string parent){
        string root=Path.Combine(parent,"menu-host-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        bool animations=GeneralStyle.AnimationsEnabled,dark=GeneralStyle.Dark,red=GeneralStyle.RedTheme;string language=GeneralStyle.Language;
        Exception failure=null;bool completed=false;
        var thread=new System.Threading.Thread(()=>{
            try{
                GeneralStyle.AnimationsEnabled=false;
                using(var owner=new Form{ClientSize=new Size(640,420),ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)}){
                    var first=new Button{Text="Projects",Bounds=new Rectangle(16,12,120,44)};
                    var second=new Button{Text="Menu",Bounds=new Rectangle(160,12,120,44)};owner.Controls.Add(first);owner.Controls.Add(second);
                    owner.Shown+=(s,e)=>{
                        try{
                            foreach(string theme in new[]{"light","dark","light-red"})foreach(string locale in new[]{"pt","en"}){
                                GeneralStyle.UseTheme(theme);GeneralStyle.Language=locale;string label=theme+"/"+locale;
                                int created=0,applied=0;GeneralDialog selected=null;
                                Func<GeneralDialog> create=()=>{
                                    created++;var menu=new GeneralDialog("Menu"){ClientSize=new Size(300,170)};
                                    var apply=GeneralStyle.Button(GeneralStyle.T("Aplicar","Apply"),()=>menu.Complete(DialogResult.OK));apply.Name="Apply";apply.Bounds=new Rectangle(16,12,264,44);menu.Content.Controls.Add(apply);
                                    GeneralStyle.Apply(menu,GeneralStyle.Dark);return menu;
                                };
                                Action<GeneralDialog> accept=menu=>{applied++;selected=menu;};
                                using(var host=new GeneralMenuHost()){
                                    host.Toggle(owner,first,create,accept);var initial=host.Current;Offscreen(initial);Application.DoEvents();
                                    check(initial!=null&&initial.Visible&&!initial.Modal&&owner.Enabled,label+": menu is modeless and keeps its anchor enabled.");
                                    check(!initial.CloseButtonShown,label+": anchored menu has no close X.");
                                    host.PointerDown(first.PointToScreen(new Point(20,20)));
                                    typeof(Form).GetMethod("OnDeactivate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(initial,new object[]{EventArgs.Empty});
                                    check(Object.ReferenceEquals(host.Current,initial)&&initial.Visible,label+": anchor mouse-down does not dismiss before the toggle click.");
                                    host.Toggle(owner,first,create,accept);Application.DoEvents();
                                    check(host.Current==null&&initial.IsDisposed&&created==1&&applied==0,label+": second anchor click closes without reopening or applying.");
                                    host.Toggle(owner,first,create,accept);var previous=host.Current;Offscreen(previous);
                                    host.Toggle(owner,second,create,accept);var next=host.Current;Offscreen(next);Application.DoEvents();
                                    check(previous.IsDisposed&&next!=null&&next!=previous&&created==3,label+": another anchor replaces and disposes the old menu.");
                                    next.Content.Controls.OfType<Button>().Single().PerformClick();Application.DoEvents();
                                    check(host.Current==null&&applied==1&&selected==next&&selected.DialogResult==DialogResult.OK,label+": selection dispatches exactly one OK callback.");
                                    host.Toggle(owner,first,create,accept);var escape=host.Current;Offscreen(escape);
                                    object[] args={Message.Create(escape.Handle,0,IntPtr.Zero,IntPtr.Zero),Keys.Escape};
                                    bool handled=(bool)typeof(GeneralDialog).GetMethod("ProcessCmdKey",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(escape,args);Application.DoEvents();
                                    check(handled&&host.Current==null&&applied==1,label+": Escape closes without applying.");
                                    host.Toggle(owner,first,create,accept);var outside=host.Current;Offscreen(outside);
                                    host.PointerDown(new Point(outside.Right+300,outside.Bottom+300));Application.DoEvents();
                                    check(host.Current==null&&applied==1,label+": outside pointer closes without applying.");
                                    host.Toggle(owner,first,create,accept);var deactivated=host.Current;Offscreen(deactivated);
                                    typeof(Form).GetMethod("OnDeactivate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(deactivated,new object[]{EventArgs.Empty});Application.DoEvents();
                                    check(host.Current==null&&applied==1,label+": deactivation closes without applying.");
                                    host.Toggle(owner,first,create,accept);var pending=host.Current;Offscreen(pending);
                                    host.PointerDown(first.PointToScreen(new Point(20,20)));host.PointerUp();host.Dispose();Application.DoEvents();
                                    check(host.Current==null&&pending.IsDisposed&&applied==1,label+": disposal safely clears a pending pointer-up and does not apply.");
                                    int before=created;host.Toggle(owner,first,create,accept);check(created==before&&host.Current==null,label+": a disposed host does not create more windows.");
                                }
                                using(var editing=new GeneralDialog("Edit")){editing.Show(owner);Offscreen(editing);Application.DoEvents();check(editing.CloseButtonShown,label+": ordinary editing dialogs retain their close X.");editing.Close();}
                            }
                            using(var childOwner=new Form{ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Location=new Point(-30000,-30000)})using(var host=new GeneralMenuHost()){
                                var anchor=new Button{Bounds=new Rectangle(0,0,80,44)};childOwner.Controls.Add(anchor);childOwner.Show();
                                host.Toggle(childOwner,anchor,()=>new GeneralDialog("Owned"){ClientSize=new Size(240,160)},null);var menu=host.Current;Offscreen(menu);childOwner.Dispose();Application.DoEvents();
                                check(host.Current==null&&menu.IsDisposed,"Disposing the owner also disposes its popup host and popup.");
                            }
                            completed=true;
                        }catch(Exception ex){failure=ex;}finally{Application.ExitThread();}
                    };
                    Application.Run(owner);
                }
            }catch(Exception ex){failure=ex;}
        });
        thread.IsBackground=true;thread.SetApartmentState(System.Threading.ApartmentState.STA);thread.Start();
        try{if(!thread.Join(60000))throw new IOException("Popup-host tests timed out.");if(failure!=null)throw failure;if(!completed)throw new IOException("Popup-host tests did not complete.");}
        finally{GeneralStyle.AnimationsEnabled=animations;GeneralStyle.Dark=dark;GeneralStyle.RedTheme=red;GeneralStyle.Language=language;}
        Safe.WriteJson(Path.Combine(root,"resultado-menu-host.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
    static void Offscreen(Form form){form.Location=new Point(-30000,-30000);}
}
