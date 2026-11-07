using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

internal static class OnlineRemovedPackageTests {
    static GeneralModEntry Entry(string id,string source,string mapped=null){return new GeneralModEntry{Id=id,Name=id,Category="Principal",DefaultVariant="main",Variants=new List<GeneralModVariant>{new GeneralModVariant{Id="main",Name="Principal",Sources=new List<string>{source},CyberpunkId=mapped}}};}
    internal static void Run(string parent){
        string root=Path.Combine(parent,"retirada-pacote-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        var catalog=FixedData.Load();
        check(catalog.Packages.Count==156&&!catalog.Packages.Any(mod=>mod.Id==RetiredPackEntries.DroneId),"Receita retirada; demais156 componentes permanecem.");
        check(!catalog.Packages.Any(mod=>mod.Id=="mod-12572")&&catalog.Packages.Single(mod=>mod.Id=="mod-34403").Dependencies.SequenceEqual(new[]{"CET"}),"Stockings collection retired and reporter uses the existing CET without replacing the base.");
        check(!catalog.Packages.Any(m=>m.Id==RetiredPackEntries.RadioId)&&!catalog.Packages.SelectMany(m=>m.Dependencies).Contains(RetiredPackEntries.RadioId),"Spot On Radio retirado sem dependencias pendentes.");
        var radioList=new GeneralModList{Mods=new List<GeneralModEntry>{Entry(RetiredPackEntries.RadioId,"https://www.nexusmods.com/cyberpunk2077/mods/34305"),Entry("mapped-radio","https://www.nexusmods.com/cyberpunk2077/mods/34305",RetiredPackEntries.RadioId),Entry("custom-radio","https://www.nexusmods.com/cyberpunk2077/mods/34305")}};
        RetiredPackEntries.Remove(radioList);check(radioList.Mods.Count==1&&radioList.Mods[0].Id=="custom-radio"&&RetiredPackEntries.PreparedArchive(new ModImportItem{Kind="Arquivo",Sha256=RetiredPackEntries.RadioArchiveHash}),"Receita e mapeamento antigos sao retirados, ZIP conhecido nao reaparece automaticamente e uma escolha custom explicita permanece editavel.");
        var accessories=catalog.Packages.Single(mod=>mod.Id=="mod-12637");check(accessories.Files.Count==2&&accessories.Files.Any(file=>file.Path.EndsWith("voidwear-accessories.reds"))&&!accessories.Files.Any(file=>file.Path.EndsWith("voidwear-casual.reds")||file.Path.EndsWith("voidwear-tacticool.reds")),"Only the two requested VOIDWEAR stores are omitted; accessories remain available.");
        check(catalog.Defaults.Count==8&&!catalog.Defaults.Any(value=>value.ModId==RetiredPackEntries.DroneId||value.Content.Contains("profiles/NASK.json")),"Perfil, índice e atalhos do drone não são embutidos.");
        check(catalog.Packages.All(mod=>mod.Files.All(file=>(file.Patch==null||VehicleCompatibility.ApprovedPatch(mod,file))&&!file.Path.Contains("FPVDrone"))),"Nenhum script, patch ou arquivo do drone permanece no catálogo ativo.");
        check(new[]{"NightCoop","CET","redscript","Codeware","TweakXL","Audioware","NativeSettings","ArchiveXL"}.All(id=>catalog.Packages.Any(mod=>mod.Id==id)),"Dependências compartilhadas preservadas.");
        var prepared=new ModImportItem{Id="file-"+RetiredPackEntries.DroneArchiveHash,Kind="Arquivo",Name="legacy.zip",Source=Path.Combine(root,"legacy.zip"),Sha256=RetiredPackEntries.DroneArchiveHash,Size=1,State="Pendente de auditoria"};
        var other=new ModImportItem{Id="file-"+new string('a',64),Kind="Arquivo",Name="other.zip",Source=Path.Combine(root,"other.zip"),Sha256=new string('a',64),Size=1,State="Pendente de auditoria"};
        var seed=GeneralModLibrary.Seed(catalog,new[]{prepared,other});
        check(!seed.Mods.Any(mod=>mod.Id==prepared.Id||mod.Id==RetiredPackEntries.DroneId)&&seed.Mods.Any(mod=>mod.Id==other.Id),"Preparação antiga não reaparece na lista; outros imports são preservados.");
        var oldStockings=new GeneralModList{Mods=new List<GeneralModEntry>{Entry("mod-12572","https://www.nexusmods.com/cyberpunk2077/mods/12572"),Entry("custom-stockings","https://www.nexusmods.com/cyberpunk2077/mods/12572")}};
        RetiredPackEntries.Remove(oldStockings);check(oldStockings.Mods.Count==1&&oldStockings.Mods[0].Id=="custom-stockings","Opening an old list retires only the built-in stockings identity; explicit custom choices remain editable.");
        var list=new GeneralModList{Categories=new List<string>{"Principal"}};
        string page="https://www.nexusmods.com/cyberpunk2077/mods/31642?tab=files&file_id=168826";
        list.Mods.Add(Entry(RetiredPackEntries.DroneId,page));
        list.Mods.Add(Entry(prepared.Id,prepared.Source));
        list.Mods.Add(Entry("old-mapped",page,RetiredPackEntries.DroneId));
        list.Mods.Add(Entry("custom-explicit",page));
        list.Mods.Add(Entry("other",other.Source));
        list.Presets.Add(new GeneralModPreset{Id="all",Name="Todos",ModIds=list.Mods.Select(mod=>mod.Id).ToList()});
        var state=new GeneralModState{List=list,SelectedPreset="all",ChosenVariants=list.Mods.ToDictionary(mod=>mod.Id,mod=>"main")};
        state.ResolvedDownloads[RetiredPackEntries.DroneId+"/main/0/"+page]=prepared.Source;
        state.CyberpunkImports[RetiredPackEntries.DroneId+"/main/0/"+page]=prepared.Id;
        state.ResolvedDownloads["other/main/0/source"]=other.Source;
        string path=Path.Combine(root,"state.json");Safe.WriteJson(path,state);
        string before=Safe.Hash(path);var library=new GeneralModLibrary(path);var loaded=library.Load();
        check(loaded.List.Mods.Select(mod=>mod.Id).SequenceEqual(new[]{"custom-explicit","other"}),"Carga retira apenas IDs conhecidos da lista legada.");
        check(loaded.List.Presets.Single().ModIds.SequenceEqual(new[]{"custom-explicit","other"}),"Presets não guardam referências ao item retirado.");
        check(loaded.ChosenVariants.Keys.OrderBy(id=>id).SequenceEqual(new[]{"custom-explicit","other"}),"Escolhas antigas do drone são retiradas do estado.");
        check(loaded.ResolvedDownloads.Count==1&&loaded.CyberpunkImports.Count==0,"Associações automáticas antigas não retomam o item retirado.");
        check(Safe.Hash(path)==before,"Migração em memória não apaga nem reescreve a lista original ao ler.");
        check(loaded.List.Mods[0].Variants[0].Sources[0]==page,"Uma nova entrada custom explícita pode continuar usando o mesmo site.");
        string exported=Path.Combine(root,"legacy-export.json");library.Export(exported,list);
        var imported=library.Import(exported);
        check(imported.Mods.Select(mod=>mod.Id).SequenceEqual(new[]{"old-mapped","custom-explicit","other"}),"Importação retira IDs legados mesmo sem metadados de receita no JSON.");
        // Exports deliberately remove trusted mappings; an arbitrary renamed custom entry is not banned.
        library.Save(loaded);check(library.Load().List.Mods.Count==2,"Estado migrado permanece correto depois de salvar e reabrir.");
        var mappings=GeneralModLibrary.Seed(catalog,new ModImportItem[0]);
        var legacy=new GeneralModList{Categories=new List<string>{"Principal"},Mods=new List<GeneralModEntry>{Entry(RetiredPackEntries.DroneId,page)}};
        GeneralModLibrary.RestoreKnownMappings(legacy,mappings);check(legacy.Mods.Count==0,"Reconciliação não converte o antigo pacote fixo em download livre.");
        string queue=Path.Combine(root,"pending.json");Safe.WriteJson(queue,new[]{prepared,other});string queueBefore=Safe.Hash(queue);
        var importer=new ModImporter(catalog,new DownloadCache(Path.Combine(root,"cache")),queue);
        check(importer.ReadPending().Select(item=>item.Id).SequenceEqual(new[]{other.Id})&&Safe.Hash(queue)==queueBefore,"Monitor ignora preparação aposentada sem apagar o registro antigo.");
        string custom=Path.Combine(root,"FPVDrone-custom-explicito.zip");using(var zip=ZipFile.Open(custom,ZipArchiveMode.Create))using(var stream=new StreamWriter(zip.CreateEntry("mods/custom.txt").Open()))stream.Write("fixture");
        var accepted=importer.Import(custom);
        check(accepted.Pending&&accepted.PendingItems.Length==1,"Importação explícita futura continua permitida, sem proibição por nome do arquivo.");
        Safe.WriteJson(Path.Combine(root,"resultado-pacote-retirado.json"),new{Passed=checks.Count,Tests=checks,RealGameTouched=false,PayloadExecuted=false});
    }
}

internal static class HistoricalReceiptTests {
    internal static int Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"history-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        var catalog=FixedData.Load();var engine=new FixedInstaller(catalog,root);
        var reader=typeof(FixedInstaller).GetMethod("ReadReceiptCore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
        Action<OnlineReceipt> read=saved=>{
            string path=Safe.In(root,".CoopModsOnline/"+saved.Id+".json");
            Safe.WriteJson(path,new ReceiptEnvelope{Protected=Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(Safe.Json.Serialize(saved)),null,System.Security.Cryptography.DataProtectionScope.CurrentUser))});
            try{reader.Invoke(engine,new object[]{path,false});}catch(System.Reflection.TargetInvocationException ex){throw ex.InnerException;}
        };
        string retired="mod-31642";var recipe=HistoricalReceipts.Expand(catalog,new[]{retired}).Single(m=>m.Id==retired);var file=recipe.Files.First(f=>f.Patch==null);
        var receipt=new OnlineReceipt{Id=Guid.NewGuid().ToString("N"),CatalogId=catalog.Id,Operation="install",Status="instalado",Mods=new List<string>{retired},Files=new List<OwnedChange>{new OwnedChange{Path=file.Path,After=file.Sha256,Applied=true}}};
        read(receipt);check(true,"Recibo histórico reconhecido sem reintroduzir o drone no catálogo ativo.");
        check(!catalog.Packages.Any(m=>m.Id==retired),"Validação histórica não disponibiliza mods retirados para instalação.");
        receipt.Files[0].After=new string('0',64);try{read(receipt);throw new Exception("Hash adulterado aceito.");}catch(IOException){checks.Add("Hash desconhecido permanece bloqueado, mesmo em recibo DPAPI válido.");}
        receipt.Files[0].After=file.Sha256;receipt.Files[0].Path="r6/scripts/unknown.reds";try{read(receipt);throw new Exception("Caminho adulterado aceito.");}catch(IOException){checks.Add("Caminho fora da receita histórica permanece bloqueado.");}
        receipt.Files[0].Path=file.Path;receipt.Mods[0]="unknown-mod";try{read(receipt);throw new Exception("Mod desconhecido aceito.");}catch(IOException){checks.Add("Mod ausente do histórico não é ignorado.");}
        receipt.Mods[0]="NightCoop";receipt.Operation="deactivate";try{read(receipt);throw new Exception("Base desativável.");}catch(IOException){checks.Add("Base imutável continua protegida.");}
        check(HistoricalReceipts.Expand(new FixedCatalog{Id="other-game",Packages=new List<FixedMod>()},new string[0]).Count==0,"Histórico não injeta receitas em outros projetos.");
        Safe.WriteJson(Path.Combine(root,"resultado-historico.json"),new{Passed=true,Checks=checks,RealGameTouched=false});return checks.Count;
    }
}
