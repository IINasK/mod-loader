using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralProjectHome {
    LinkLabel homeModVersions;
    string homeModVersionText;
    void AddHomeModVersionNotice(Control parent){
        homeModVersions=new LinkLabel{Name="HomeModVersions",AutoSize=true,Font=GeneralStyle.Font(10.5f),
            LinkColor=GeneralStyle.Accent,ActiveLinkColor=GeneralStyle.Foreground,Margin=new Padding(0,0,0,8),
            Text=homeModVersionText??T("Versões dos mods: consulta ao abrir","Mod versions: check on opening")};
        homeModVersions.LinkClicked+=(s,e)=>GeneralDialog.Message(this,T(
            "Abra um projeto e clique em Versões para ver cada mod, conectar sua conta Nexus ou revisar uma alteração local. A consulta não instala atualizações automaticamente.",
            "Open a project and click Versions to view each mod, connect your Nexus account, or review a local change. This check does not install updates automatically."),T("Versões dos mods","Mod versions"));
        parent.Controls.Add(homeModVersions);
    }
    async Task CheckHomeModVersions(){
        if(fixture)return;
        try{
            var links=await Task.Run(()=>{
                var all=new Dictionary<string,GeneralNexusLink>(StringComparer.Ordinal);
                foreach(var project in store.Read(true).Projects){
                    try{
                        var state=new GeneralModLibrary(Path.Combine(store.DataPath(project),"lista.json")).Load();
                        if(state==null)continue;
                        foreach(var entry in state.List.Mods){
                            string selected;state.ChosenVariants.TryGetValue(entry.Id,out selected);
                            var variant=entry.Variants.FirstOrDefault(v=>v.Id==selected)??entry.Variants.FirstOrDefault(v=>v.Id==entry.DefaultVariant)??entry.Variants.FirstOrDefault();
                            if(variant==null)continue;
                            foreach(var link in variant.Sources.Select(GeneralNexusLink.Parse).Where(link=>link!=null))if(all.Count<1024)all[link.Url]=link;
                        }
                    }catch(IOException){}catch(UnauthorizedAccessException){}
                }
                return all.Values.ToArray();
            });
            if(IsDisposed||links.Length==0)return;
            homeModVersionText=T("Consultando versões dos mods…","Checking mod versions…");homeModVersions.Text=homeModVersionText;
            var session=NexusUpdateSession.Session;
            // Home remains alive while a project is open, so the same in-flight request is reused.
            var results=await Task.WhenAll(links.Select(link=>session.Check(link,()=>!IsDisposed)));
            if(IsDisposed)return;
            int updates=results.Count(r=>r.State=="update");
            int unknown=results.Count(r=>r.State!="update"&&r.State!="current");
            homeModVersionText=results.Any(r=>r.State=="connect")?T("Nexus: conecte sua conta em um projeto","Nexus: connect your account in a project"):
                String.Format(T("Versões dos mods: {0} atualização(ões) · {1} não consultado(s)","Mod versions: {0} update(s) · {1} unchecked"),updates,unknown);
            homeModVersions.Text=homeModVersionText;
        }catch(Exception ex){if(!IsDisposed){homeModVersionText=T("Versões dos mods: consulta indisponível","Mod versions: check unavailable");homeModVersions.Text=homeModVersionText;}OnlineDiagnostics.Record("mods.update.home",null,ex.GetType().Name);}
    }
}
