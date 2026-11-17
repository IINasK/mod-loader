using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunHumanCloseTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"close-");Directory.CreateDirectory(root);
        string game=Path.Combine(root,"game"),downloads=Path.Combine(root,"downloads"),data=Path.Combine(root,"data");
        Directory.CreateDirectory(game);Directory.CreateDirectory(downloads);
        var fixture=new GeneralModState{Destination=game,DownloadFolder=downloads,Language="pt",List=new GeneralModList()};
        var entry=NewEntry("Close fixture","https://example.com/fixture.zip");fixture.List.Mods.Add(entry);
        var catalog=FixedData.Load();Exception failure=null;bool canceled=false,closed=false;int prompts=0;
        string selected=null;
        var thread=new Thread(()=>{
            try {
                using(var form=new GeneralModWindow(catalog,new DownloadCache(Path.Combine(root,"cache")),data,fixture)) {
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);
                    form.launch=url=>{};
                    form.FormClosed+=(s,e)=>{
                        if(form.working||form.normal.Count!=0||form.priority.Count!=0)failure=new IOException("Close completed before the download queue stopped.");
                        closed=true;
                    };
                    form.Shown+=async(s,e)=>{
                        try {
                            form.library.Save(form.state);selected=form.state.SelectedPreset;
                            form.Enqueue(new[]{form.MakeWork(form.state.List.Mods.Single())},false);
                            await Until(()=>form.NeedUnknownFile);
                            using(var timer=new System.Windows.Forms.Timer{Interval=40}) {
                                DateTime deadline=DateTime.UtcNow.AddSeconds(15);
                                timer.Tick+=(ts,te)=>{
                                    var dialog=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(item=>item.Visible);
                                    try {
                                        if(DateTime.UtcNow>=deadline)throw new IOException("Close confirmation was not shown.");
                                        if(dialog==null)return;
                                        string label=prompts==0?"Continuar no app":"Fechar";
                                        var button=AllControls(dialog).OfType<Button>().Single(item=>item.Text==label);
                                        prompts++;button.PerformClick();
                                    } catch(Exception ex) {failure=ex;timer.Stop();if(dialog!=null)dialog.Close();Application.ExitThread();}
                                };
                                timer.Start();form.Close();
                                if(failure!=null)throw failure;
                                canceled=!form.IsDisposed&&form.working&&form.NeedUnknownFile&&prompts==1;
                                if(!canceled)throw new IOException("Canceling close must preserve the active download queue.");
                                form.Close();timer.Stop();
                                if(prompts!=2)throw new IOException("Second close must ask again before stopping the queue.");
                            }
                        } catch(Exception ex) {failure=ex;Application.ExitThread();}
                    };
                    Application.Run(form);
                }
            } catch(Exception ex) {failure=ex;}
        });
        thread.IsBackground=true;thread.SetApartmentState(ApartmentState.STA);thread.Start();
        if(!thread.Join(45000))throw new IOException("Human close test exceeded its deadline.");
        if(failure!=null)throw failure;
        if(!canceled||!closed||Directory.EnumerateFiles(game,"*",SearchOption.AllDirectories).Any())throw new IOException("Closing the waiting queue changed game files or did not complete.");
        var saved=new GeneralModLibrary(Path.Combine(data,"lista.json")).Load();
        TestForm(catalog,saved,data,root,async reopened=>{
            await reopened.Scan();
            if(reopened.state.SelectedPreset!=selected||reopened.state.List.Mods.Count!=1||reopened.working||reopened.Status(reopened.MakeWork(reopened.state.List.Mods.Single()),game)==GeneralModInstaller.Installed)
                throw new IOException("Reopening must preserve the project and preset without a false installed state.");
        });
        Safe.WriteJson(Path.Combine(root,"ui-result.json"),new{Passed=true,Checks=new[]{"Cancel close keeps waiting queue active","Confirm close drains queue before FormClosed","No game files created","Reopen preserves mod and selected preset","Reopen does not claim missing mod installed"},BrowserOpened=false,RealGameTouched=false});
    }
}
