using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralProjectHome {
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
            var session=NexusUpdateSession.Session;
            // Home remains alive while a project is open, so the same in-flight request is reused.
            await Task.WhenAll(links.Select(link=>session.Check(link,()=>!IsDisposed)));
        }catch(Exception ex){OnlineDiagnostics.Record("mods.update.home",null,ex.GetType().Name);}
    }
}
