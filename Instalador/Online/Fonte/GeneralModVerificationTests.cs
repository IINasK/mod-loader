using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunVerificationFlowTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"verify-project-");
        Directory.CreateDirectory(root);
        string game=OnlineTests.Fixture(root,"game");
        string downloads=Path.Combine(root,"downloads");
        Directory.CreateDirectory(downloads);

        var owner=VerificationFixtureMod(root,"verify-owner","Mod de teste",501);
        var dependency=VerificationFixtureMod(root,"verify-dependency","Dependência de teste",502);
        owner.Dependencies=new[]{dependency.Id};
        var catalog=new FixedCatalog{
            Schema=1,Id="verify-project-test",GameVersion="2.5",GameFileVersion="2.5.0.0",
            Packages=new List<FixedMod>{owner,dependency}
        };
        var state=new GeneralModState{
            List=GeneralModLibrary.Seed(catalog,new ModImportItem[0]),DownloadFolder=downloads,Language="pt"
        };
        string installedPath=Safe.In(game,owner.Files.Single().Path);
        string dependencyPath=Safe.In(game,dependency.Files.Single().Path);
        const string extraPath="archive/pc/mod/manual-tattoo.archive";
        string manualPath=Safe.In(game,extraPath);
        Directory.CreateDirectory(Path.GetDirectoryName(installedPath));
        File.WriteAllText(installedPath,owner.Id,new UTF8Encoding(false));
        File.WriteAllText(manualPath,"manual");
        var opened=new List<string>();

        TestForm(catalog,state,Path.Combine(root,"app-data"),root,async form=>{
            form.launch=opened.Add;
            await form.VerifyProject();
            if(form.detail.Text!="Selecione a pasta de instalação para verificar."||form.statuses.Count!=0)
                throw new IOException("Verificar sem pasta precisa informar o pré-requisito antes de varrer.");
            form.SetDestination(game);

            Exception dialogFailure=null;
            int step=0;
            var deadline=DateTime.UtcNow.AddSeconds(15);
            using(var timer=new Timer{Interval=40}){
                timer.Tick+=(s,e)=>{
                    var review=Application.OpenForms.OfType<GeneralDependencyReview>().FirstOrDefault(item=>item.Visible);
                    var inventory=Application.OpenForms.OfType<GeneralSyncDifferencesDialog>().FirstOrDefault(item=>item.Visible);
                    var confirmation=Application.OpenForms.OfType<GeneralInstallConfirmation>().FirstOrDefault(item=>item.Visible);
                    try{
                        if(DateTime.UtcNow>=deadline)throw new IOException("A revisão não avançou até a etapa "+step+".");
                        if(step==0&&review!=null){
                            var rows=AllControls(review).OfType<DataGridView>().Single(grid=>grid.Name=="DependencyResults");
                            if(rows.Rows.Count!=1||!Equals(rows.Rows[0].Cells[0].Value,owner.Name)||
                                !Equals(rows.Rows[0].Cells[1].Value,dependency.Name))
                                throw new IOException("A revisão deve mostrar a dependência ausente e o mod que precisa dela.");
                            rows.CurrentCell=null;
                            rows.CurrentCell=rows.Rows[0].Cells[0];
                            AllControls(review).OfType<Button>().Single(button=>button.Name=="OpenDependencyPage").PerformClick();
                            if(!opened.SequenceEqual(new[]{"https://www.nexusmods.com/cyberpunk2077/mods/400?tab=files&file_id=502"}))
                                throw new IOException("A revisão deve abrir somente a página oficial do arquivo fixado.");
                            step=1;
                            AllControls(review).OfType<Button>().Single(button=>button.Name=="ShowUnlistedInventory").PerformClick();
                            if(dialogFailure!=null)return;
                            if(step!=2)throw new IOException("O relatório de arquivos fora da lista não abriu.");
                            AllControls(review).OfType<Button>().Single(button=>button.Name=="InstallCatalogDependencies").PerformClick();
                        }else if(step==1&&inventory!=null){
                            var rows=AllControls(inventory).OfType<DataGridView>().Single(grid=>grid.Name=="SyncDifferenceFiles");
                            if(rows.Rows.Count!=1||!Equals(rows.Rows[0].Cells[2].Value,extraPath))
                                throw new IOException("O relatório precisa mostrar o arquivo externo pelo caminho encontrado no jogo.");
                            step=2;
                            ((Button)inventory.CancelButton).PerformClick();
                        }else if(step==2&&confirmation!=null){
                            var rows=AllControls(confirmation).OfType<DataGridView>().Single(grid=>grid.Name=="PendingDownloads");
                            if(rows.Rows.Count!=1||!Convert.ToString(rows.Rows[0].Cells[0].Value).Contains(dependency.Name))
                                throw new IOException("A confirmação deve incluir somente a dependência oferecida.");
                            step=3;
                            timer.Stop();
                            ((Button)confirmation.CancelButton).PerformClick();
                        }
                    }catch(Exception ex){
                        dialogFailure=ex;
                        timer.Stop();
                        foreach(var dialog in Application.OpenForms.OfType<GeneralDialog>().Reverse().ToArray())dialog.Close();
                    }
                };
                timer.Start();
                await form.VerifyProject();
            }
            if(dialogFailure!=null)throw dialogFailure;
            if(step!=3||form.working||form.detail.Text!="Instalação cancelada."||
                File.Exists(dependencyPath)||File.ReadAllText(installedPath)!=owner.Id||File.ReadAllText(manualPath)!="manual")
                throw new IOException("Cancelar na confirmação deve preservar os arquivos locais e deixar a fila vazia.");

            using(var review=new GeneralDependencyReview("Teste",new[]{
                new GeneralDependencyIssue{Mod="Mod",Requirement="Arquivo",Url="file:///C:/test.zip"}
            },0,opened.Add)){
                review.Show(form);
                var rows=AllControls(review).OfType<DataGridView>().Single(grid=>grid.Name=="DependencyResults");
                rows.CurrentCell=rows.Rows[0].Cells[0];
                var browser=AllControls(review).OfType<Button>().Single(button=>button.Name=="OpenDependencyPage");
                browser.PerformClick();
                if(browser.Enabled||opened.Count!=1)
                    throw new IOException("O relatório não pode abrir uma referência de arquivo local como página.");
            }
        });
    }

    static FixedMod VerificationFixtureMod(string root,string id,string name,int fileId){
        string target="archive/pc/mod/"+id+".archive";
        string zip=TestZip(root,id+".zip",target,id);
        var asset=new FixedAsset{
            Id="a",Name=Path.GetFileName(zip),ModId=400,FileId=fileId,Format="zip",
            Size=new FileInfo(zip).Length,Sha256=Safe.Hash(zip)
        };
        return new FixedMod{
            Id=id,Name=name,Version="1",GameVersion="2.5",Dependencies=new string[0],
            Assets=new List<FixedAsset>{asset},
            Files=new List<FixedFile>{new FixedFile{
                Asset="a",Path=target,Entry=target,Size=Encoding.UTF8.GetByteCount(id),Sha256=TestHash(id)
            }}
        };
    }
}
