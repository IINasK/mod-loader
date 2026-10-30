using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public sealed class AutoInstallPlan {
    public List<FixedMod> ExpandedMods,MissingMods,ReadyMods;
    public Dictionary<string,ModScan> Scans;
    public Dictionary<string,bool> CachedAssets;
    public FixedMod ManualMod;
    public FixedAsset ManualAsset;
    public bool Complete { get { return MissingMods.Count==0; } }

    public static AutoInstallPlan Build(FixedCatalog catalog,IEnumerable<string> authorizedIds,string gameRoot,DownloadCache cache,bool premium) {
        if(catalog==null||authorizedIds==null||cache==null)throw new ArgumentNullException();
        var mods=FixedData.Expand(catalog,authorizedIds).Select(m=>NightCityDensity.ForInstall(m,gameRoot)).ToList();
        if(mods.Count==0)throw new IOException("Nenhum mod autorizado para instalar.");
        FixedData.RequireInstallable(mods);
        string game=Path.GetFullPath(gameRoot).TrimEnd('\\');
        if(game.Length<=3||game.StartsWith(@"\\",StringComparison.Ordinal))throw new IOException("Escolha uma pasta local do jogo.");
        FixedData.ValidateGame(game,mods,catalog.GameFileVersion);
        if(cache.Root.Equals(game,StringComparison.OrdinalIgnoreCase)||cache.Root.StartsWith(game+"\\",StringComparison.OrdinalIgnoreCase))
            throw new IOException("O cache de downloads precisa ficar fora da pasta do jogo.");
        if(mods.SelectMany(m=>m.Assets).GroupBy(a=>a.Sha256).Any(g=>g.Select(a=>a.Size).Distinct().Count()!=1))
            throw new IOException("O catálogo atribui tamanhos diferentes ao mesmo arquivo.");
        var plan=new AutoInstallPlan {
            ExpandedMods=mods,MissingMods=new List<FixedMod>(),ReadyMods=new List<FixedMod>(),
            Scans=new Dictionary<string,ModScan>(StringComparer.Ordinal),CachedAssets=new Dictionary<string,bool>(StringComparer.Ordinal)
        };
        foreach(var mod in mods) {
            var scan=FixedData.Scan(mod,game);plan.Scans.Add(mod.Id,scan);
            if(scan.IsConflict)throw new IOException(mod.Name+": "+scan.State+". Resolva o conflito; nenhum arquivo será sobrescrito.");
            if(!scan.IsCorrect||ModDefaults.Needs(catalog,mod.Id,game))plan.MissingMods.Add(mod);
        }
        var available=new Dictionary<string,bool>(StringComparer.Ordinal);
        foreach(var mod in mods) {
            bool ownReady=plan.Scans[mod.Id].IsCorrect;
            if(!ownReady) {
                ownReady=true;
                foreach(var asset in mod.Assets) {
                    bool cached;
                    if(!plan.CachedAssets.TryGetValue(asset.Sha256,out cached)) {
                        cached=cache.Contains(asset);plan.CachedAssets.Add(asset.Sha256,cached);
                    }
                    if(cached||asset.ModId==0||premium)continue;
                    ownReady=false;
                    if(plan.ManualMod==null){plan.ManualMod=mod;plan.ManualAsset=asset;}
                }
            }
            bool ready=ownReady&&mod.Dependencies.All(id=>available[id]);
            available.Add(mod.Id,ready);
            if(ready&&plan.MissingMods.Any(m=>m.Id==mod.Id))plan.ReadyMods.Add(mod);
        }
        OnlineDiagnostics.Record("plan.created",null,"Plano baseado nos arquivos reais do jogo.",new{Game=game,Mods=mods.Select(m=>m.Id).ToArray(),NeedInstall=plan.MissingMods.Select(m=>m.Id).ToArray(),Ready=plan.ReadyMods.Select(m=>m.Id).ToArray(),Manual=plan.ManualMod==null?null:plan.ManualMod.Id,Complete=plan.Complete});return plan;
    }
}
