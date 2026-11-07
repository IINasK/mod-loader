using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunGeneralTests(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        string stateDir=Path.Combine(root,"app-data"),download=Path.Combine(root,"downloads");
        Directory.CreateDirectory(download);
        var library=new GeneralModLibrary(Path.Combine(stateDir,"lista.json"));
        var real=FixedData.Load();
        var realSeed=GeneralModLibrary.Seed(real,new ModImportItem[0]);
        var nca=realSeed.Mods.Single(m=>m.Id==NightCityDensity.ModId);
        check(realSeed.Mods.Count==141&&nca.DefaultVariant=="low"&&nca.Variants.Count==3,"Catálogo preservado com três densidades e Baixa principal.");
        string local=TestZip(root,"custom.zip","CETExample/init.lua","return {}");
        var imported=NewEntry("Nome original",local);
        imported.Name="Personalizado renomeado";imported.Category="Minha categoria";
        imported.Variants.Add(new GeneralModVariant{Id="alt",Name="Alternativa",Sources=new List<string>{"https://example.com/mod.zip"}});
        imported.DefaultVariant="alt";
        realSeed.Categories.Add(imported.Category);realSeed.Mods.Add(imported);
        var saved=new GeneralModState{List=realSeed,Destination=Path.Combine(root,"private-destination"),DownloadFolder=download};
        saved.ChosenVariants[nca.Id]="high";saved.ResolvedDownloads["exemplo"]=local;
        library.Save(saved);
        var loaded=library.Load();
        var reloaded=loaded.List.Mods.Single(m=>m.Id==imported.Id);
        check(reloaded.Name==imported.Name&&reloaded.Category==imported.Category&&reloaded.DefaultVariant=="alt"&&loaded.ChosenVariants[nca.Id]=="high","Nome, categoria, principal e escolha atual sobrevivem à reabertura.");
        string export=Path.Combine(root,"export.json");
        library.Export(export,loaded.List);
        var roundTrip=library.Import(export);
        string text=File.ReadAllText(export);
        check(!text.Contains("CyberpunkId")&&!text.Contains("Destination")&&!text.Contains("ResolvedDownloads")&&!text.Contains("ChosenVariants")&&!text.Contains("DownloadFolder"),"Exportação exclui pastas de destino, escolhas locais e receitas internas.");
        check(roundTrip.Categories.Contains(imported.Category)&&roundTrip.Mods.Single(m=>m.Id==imported.Id).Variants[0].Sources[0]==local,"Exportação leva referências locais e organização.");
        var ncaImported=roundTrip.Mods.Single(m=>m.Id==nca.Id);
        ncaImported.Variants.Single(v=>v.Id=="high").Sources[0]="https://example.com/other.zip";
        GeneralModLibrary.RestoreKnownMappings(roundTrip,GeneralModLibrary.Seed(real,new ModImportItem[0]));
        check(ncaImported.Variants.Single(v=>v.Id=="low").CyberpunkId==nca.Id&&ncaImported.Variants.Single(v=>v.Id=="high").CyberpunkId==null,"Só fontes exatamente iguais ao catálogo recuperam a receita fixa.");

        string game=OnlineTests.Fixture(root,"fake-game");
        var fake=new FixedCatalog{Schema=1,Id="general-test",GameVersion="2.5",GameFileVersion="2.5.0.0",Packages=new List<FixedMod>()};
        var originals=new Dictionary<string,string>();
        for(int i=1;i<=3;i++){
            string id="fixture"+i,name=id+".zip",target="archive/pc/mod/"+id+".archive";
            string zip=TestZip(root,name,target,id);
            var asset=new FixedAsset{Id="a",Name=name,ModId=400,FileId=500+i,Format="zip",Size=new FileInfo(zip).Length,Sha256=Safe.Hash(zip)};
            fake.Packages.Add(new FixedMod{Id=id,Name=id,Version="1",GameVersion="2.5",Dependencies=new string[0],Assets=new List<FixedAsset>{asset},
                Files=new List<FixedFile>{new FixedFile{Asset="a",Path=target,Entry=target,Size=Encoding.UTF8.GetByteCount(id),Sha256=TestHash(id)}}});
            originals.Add(id,zip);
        }
        var queueState=new GeneralModState{List=GeneralModLibrary.Seed(fake,new ModImportItem[0]),Destination=game,DownloadFolder=download};
        var urgent=NewEntry("Custom prioritário",local);queueState.List.Mods.Add(urgent);
        File.Copy(originals["fixture2"],Path.Combine(download,"fixture2.zip"));
        var opened=new List<string>();
        TestForm(fake,queueState,stateDir,root,async form=>{
            form.launch=url=>opened.Add(url);
            var ordered=queueState.List.Mods.Take(3).Select(form.MakeWork).ToArray();
            form.Enqueue(ordered,false);
            await Until(()=>opened.Count==1);
            check(opened[0].EndsWith("501"),"A fila abre somente a primeira página.");
            string first=Path.Combine(game,"archive","pc","mod","fixture1.archive");
            string partial=Path.Combine(download,"fixture1.zip.crdownload");
            File.Copy(originals["fixture1"],partial);
            form.Enqueue(new[]{form.MakeWork(urgent)},true);
            string urgentPath=Path.Combine(game,"bin","x64","plugins","cyber_engine_tweaks","mods","CETExample","init.lua");
            await Until(()=>File.Exists(urgentPath)&&form.priorityCurrent==null);
            check(!File.Exists(first)&&opened.Count==1,"Importado CET entra na frente sem aceitar parcial nem abrir outras páginas.");
            File.Move(partial,Path.Combine(download,"fixture1.zip"));
            await Until(()=>opened.Count==2);
            check(opened[1].EndsWith("503")&&File.Exists(first)&&File.Exists(Path.Combine(game,"archive","pc","mod","fixture2.archive")),"O ZIP completo instala, reaproveita o próximo já em Downloads e então avança.");
            form.RequestSkip();await Until(()=>!form.working);
            check(form.statuses[ordered[2].Key]=="Pulado"&&!File.Exists(Path.Combine(game,"archive","pc","mod","fixture3.archive")),"Pular não grava o mod que está aguardando.");
            check(form.Status(form.MakeWork(urgent),game)=="Instalado","Instalação CET reconhecida pelo recibo legado.");
            await Task.Run(()=>form.Cyberpunk(game).RemoveImported(ImportItem(local).Id,s=>{}));
            check(!File.Exists(urgentPath)&&File.Exists(first),"Remoção direcionada de importado preserva os outros mods.");
        });
        string general=Path.Combine(root,"outro-jogo");
        Directory.CreateDirectory(general);
        var genericState=new GeneralModState{Destination=general,DownloadFolder=download,List=new GeneralModList()};
        var custom=NewEntry("Outro jogo","https://example.com/mod.zip");
        genericState.List.Mods.Add(custom);
        TestForm(real,genericState,stateDir,root,async form=>{
            int launches=0;
            form.launch=url=>launches++;
            var work=form.MakeWork(custom);form.Enqueue(new[]{work},false);
            await Until(()=>form.NeedUnknownFile&&launches==1);
            check(!File.Exists(Path.Combine(general,"CETExample","init.lua")),"Link livre aguarda a associação explícita.");
            form.association=local;
            await Until(()=>!form.working);
            check(form.Status(work,general)=="Instalado"&&File.Exists(Path.Combine(general,"CETExample","init.lua")),"Arquivo associado instala na pasta genérica preservando a hierarquia.");
            form.Enqueue(new[]{work},false);await Until(()=>!form.working);
            check(launches==1,"Instalação existente é reconhecida sem reabrir o navegador.");
        });
        int archiveChecks=GeneralModInstallerTests.Run(root);
        int libraryChecks=GeneralModLibraryV2Tests.Run(root);
        RunProgressTests(root);HistoricalReceiptTests.Run(root);
        RunUiTests(root);
        GeneralButtonPaintTests.Run(root);
        GeneralP2PTests.Run(root);
        GeneralP2PUiTests.Run(root);
        InstalledSnapshotTests.Run(root);InstalledSnapshotInstallerTests.Run(root);
        GeneralStatusPresentationTests.Run(root);
        RunFloatingTests(root);GeneralCategoryDeleteTests.Run(root);GeneralTableDialogTests.Run(root);RunRedesignTests(root);
        GeneralUpdatesTests.Run(root);GeneralAutomaticUpdateTests.Run(root);ModVersionChoiceTests.Run(root);RunPresetIsolationTests(root);
        GeneralBrandTests.Run(root);
        GeneralProjectStoreTests.Run(root);RunProjectHomeTests(root);RunProjectDeletionTests(root);RunProjectHubTests(root);GeneralAppPathsTests.Run(root);RunStorageTests(root);RunNoCopyTests(root);RunProjectCloneTests(root);
        GeneralMenuHostTests.Run(root);RunPresetDraftTests(root);
        GeneralPopupStabilityTests.Run(root);RunProjectSearchTests(root);
        Safe.WriteJson(Path.Combine(root,"resultado-loader-geral.json"),new{Passed=true,Tests=checks,ArchiveChecks=archiveChecks,LibraryChecks=libraryChecks,RealGameTouched=false,NetworkUsed=false});
    }
    static string TestZip(string root,string name,string entry,string contents){
        string path=Path.Combine(root,name);
        using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
            using(var writer=new StreamWriter(zip.CreateEntry(entry).Open(),new UTF8Encoding(false)))writer.Write(contents);
        return path;
    }
    static string TestHash(string text){
        using(var sha=System.Security.Cryptography.SHA256.Create())return Safe.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }
    static async Task Until(Func<bool> condition){
        var limit=DateTime.UtcNow.AddSeconds(25);
        while(!condition()){if(DateTime.UtcNow>=limit)throw new IOException("A fila de teste não chegou ao estado esperado.");await Task.Delay(40);}
    }
    static void TestForm(FixedCatalog catalog,GeneralModState state,string data,string root,Func<GeneralModWindow,Task> test){
        Exception failure=null;bool completed=false;
        var ui=new System.Threading.Thread(()=>{
            try{
                using(var form=new GeneralModWindow(catalog,new DownloadCache(Path.Combine(root,"cache")),data,state)){
                    form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-30000,-30000);
                    form.Shown+=async(s,e)=>{try{await test(form);completed=true;}catch(Exception ex){failure=new IOException(form.detail.Text,ex);}finally{Application.ExitThread();}};
                    Application.Run(form);
                }
            }catch(Exception ex){failure=ex;}
        });
        ui.IsBackground=true;ui.SetApartmentState(System.Threading.ApartmentState.STA);ui.Start();
        if(!ui.Join(60000))throw new IOException("A janela de teste excedeu o tempo limite.");
        if(failure!=null)throw failure;
        if(!completed)throw new IOException("A janela de teste fechou sem completar o fluxo.");
    }
}
