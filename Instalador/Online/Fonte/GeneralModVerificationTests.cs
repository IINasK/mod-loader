using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            if(form.statuses.Count!=0)
                throw new IOException("Verificar sem pasta precisa informar o pré-requisito antes de varrer.");
            form.SetDestination(game);
            if(!form.verifyAction.Visible||!form.verifyAction.Enabled)
                throw new IOException("Recarregar deve estar disponível antes do scan.");
            var title=form.Controls.Find("ProjectBrandTitle",true).Single();
            if(form.verifyAction.Parent!=title.Parent||form.verifyAction.Left<=title.Right||
                form.verifyAction.Size!=new System.Drawing.Size(44,44)||
                Math.Abs((form.verifyAction.Top+form.verifyAction.Height/2)-(title.Top+title.Height/2))>8)
                throw new IOException("Recarregar deve estar à direita do título Mod Loader, alinhado ao centro.");
            Func<bool> pressF5=()=>{
                var message=Message.Create(form.Handle,0x0100,(IntPtr)Keys.F5,IntPtr.Zero);
                return form.ProcessCmdKey(ref message,Keys.F5);
            };
            bool sawLocalScan=false;
            form.testStage=(key,phase)=>{
                if(phase!="scan.begin")return;
                sawLocalScan=true;
                if(form.verifyAction.Visible)throw new IOException("Recarregar deve sumir no scan automático.");
            };
            await form.Scan();
            if(!sawLocalScan||!form.verifyAction.Visible||!form.verifyAction.Enabled)
                throw new IOException("Recarregar deve voltar ao fim do scan automático.");
            var checkedMods=new HashSet<string>();
            bool hiddenDuringScan=true;
            bool acceptedDuringScan=true;
            bool repeatedScan=false;
            form.testStage=(key,phase)=>{
                if(phase!="scan.begin")return;
                hiddenDuringScan&=!form.verifyAction.Visible;
                if(checkedMods.Count==0)acceptedDuringScan=pressF5();
                repeatedScan|=!checkedMods.Add(key);
            };

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
                if(!pressF5())throw new IOException("F5 deve iniciar a verificação do projeto.");
                while(form.verifyingProject&&DateTime.UtcNow<deadline.AddSeconds(2))await Task.Delay(25);
            }
            form.testStage=null;
            if(dialogFailure!=null)throw dialogFailure;
            if(form.verifyingProject||!hiddenDuringScan||!acceptedDuringScan||repeatedScan||checkedMods.Count!=state.List.Mods.Count||
                !form.verifyAction.Visible||!form.verifyAction.Enabled)
                throw new IOException("Recarregar deve sumir durante o scan, ignorar F5 repetido e reaparecer ao terminar.");
            if(step!=3||form.working||
                File.Exists(dependencyPath)||File.ReadAllText(installedPath)!=owner.Id||File.ReadAllText(manualPath)!="manual")
                throw new IOException("Cancelar na confirmação deve preservar os arquivos locais e deixar a fila vazia.");

            File.WriteAllText(installedPath,"local version");
            form.testVersionChoice=difference=>{
                if(!difference.AllowSkip||!difference.CanReplace||!difference.Detail.Contains(owner.Files.Single().Path))
                    throw new IOException("A revisão precisa listar o arquivo divergente e oferecer as duas escolhas.");
                difference.SkipSelected=true;
                return false;
            };
            using(var timer=new Timer{Interval=40}){
                timer.Tick+=(s,e)=>{
                    var review=Application.OpenForms.OfType<GeneralDependencyReview>().FirstOrDefault(item=>item.Visible);
                    if(review==null)return;
                    timer.Stop();
                    try{
                        var rows=AllControls(review).OfType<DataGridView>().Single(grid=>grid.Name=="DependencyResults");
                        var repair=rows.Rows.Cast<DataGridViewRow>().Single(row=>((GeneralDependencyIssue)row.Tag).CanRepair);
                        rows.CurrentCell=repair.Cells[0];
                        AllControls(review).OfType<Button>().Single(button=>button.Name=="ReviewModReplacement").PerformClick();
                    }catch(Exception ex){dialogFailure=ex;review.Close();}
                };
                timer.Start();
                await form.VerifyProject();
            }
            var stop=DateTime.UtcNow.AddSeconds(5);
            while(form.working&&DateTime.UtcNow<stop)await System.Threading.Tasks.Task.Delay(20);
            if(dialogFailure!=null)throw dialogFailure;
            if(form.working||form.statuses[form.MakeWork(form.state.List.Mods.Single(entry=>entry.Id==owner.Id)).Key]!="Pulado"||
                File.ReadAllText(installedPath)!="local version"||File.ReadAllText(manualPath)!="manual")
                throw new IOException("Pular a substituição na verificação deve preservar a versão e os arquivos locais.");
            File.WriteAllText(installedPath,owner.Id);
            form.testVersionChoice=null;

            string movedByUser=Path.Combine(root,"moved-by-user.archive");
            File.Move(installedPath,movedByUser);
            await form.Scan();
            var movedWork=form.MakeWork(form.state.List.Mods.Single(entry=>entry.Id==owner.Id));
            if(form.Status(movedWork,game)==GeneralModInstaller.Installed||!File.Exists(movedByUser)||File.Exists(installedPath))
                throw new IOException("Mover um arquivo manualmente deve invalidar o estado instalado sem desfazer a movimentação do usuário.");
            File.Move(movedByUser,installedPath);
            await form.Scan();
            if(form.Status(movedWork,game)!=GeneralModInstaller.Installed)
                throw new IOException("Devolver o arquivo original deve restaurar o reconhecimento por conteúdo.");

            const string related="r6/scripts/manual-tattoo.reds";
            string relatedPath=Safe.In(game,related);
            Directory.CreateDirectory(Path.GetDirectoryName(relatedPath));
            File.WriteAllText(relatedPath,"manual script");
            await form.Scan();
            if(!form.detectedModsReview.Visible||form.DetectedModGroups().Length!=1||form.state.List.Mods.Any(mod=>mod.Name=="Mod detectado"))
                throw new IOException("O scan deve oferecer a revisão sem alterar a lista.");
            using(var timer=new Timer{Interval=40}){
                timer.Tick+=(s,e)=>{
                    var editor=Application.OpenForms.OfType<GeneralModEditor>().FirstOrDefault(item=>item.Visible);
                    if(editor==null){
                        var review=Application.OpenForms.OfType<GeneralDialog>().FirstOrDefault(item=>item.Visible&&
                            AllControls(item).Any(control=>control.Name=="DetectedMods"));
                        if(review==null)return;
                        var table=AllControls(review).OfType<DataGridView>().Single();
                        table.CurrentCell=table.Rows[0].Cells[0];
                        AllControls(review).OfType<Button>().Single(button=>button.Name=="RegisterDetectedMod").PerformClick();
                        return;
                    }
                    timer.Stop();
                    try{
                        var save=(Button)editor.AcceptButton;
                        var source=EditorField<TextBox>(editor,"sources");
                        if(save.Enabled||source.ReadOnly||source.Text!="")
                            throw new IOException("O item externo deve abrir sem vínculo e permitir informar uma fonte.");
                        EditorField<TextBox>(editor,"modName").Text="Mod detectado";
                        if(!save.Enabled||save.Text!="Incluir na lista e salvar")
                            throw new IOException("A edição de metadados deve liberar Incluir na lista e salvar.");
                        save.PerformClick();
                    }catch(Exception ex){dialogFailure=ex;editor.Complete(DialogResult.Cancel);}
                };
                timer.Start();
                form.ReviewDetectedMods();
                await Until(()=>!form.scanning&&!timer.Enabled);
            }
            if(dialogFailure!=null)throw dialogFailure;
            var adopted=form.library.Load().List.Mods.Single(mod=>mod.Name=="Mod detectado");
            var adoptedWork=form.MakeWork(form.state.List.Mods.Single(mod=>mod.Id==adopted.Id));
            if(!new HashSet<string>(adopted.DetectedPaths).SetEquals(new[]{extraPath,related})||
                form.Status(adoptedWork,game)!=GeneralStatusPresentation.DetectedFiles||
                form.VisibleGameInventory().Any()||form.detectedModsReview.Visible||form.state.SnapshotReceipts.ContainsKey(adoptedWork.Key)||
                File.ReadAllText(manualPath)!="manual"||File.ReadAllText(relatedPath)!="manual script")
                throw new IOException("Incluir o item deve registrar pistas de múltiplas pastas sem assumir instalação ou mexer nos arquivos.");

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

    internal static void RunOutputRecoveryTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"outputs-");
        string game=Path.Combine(root,"g");
        Directory.CreateDirectory(game);
        File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
        const string code="mods/clothes/entry.bin",older="mods/clothes/preexisting.json",later="mods/clothes/run/48317.log";
        string archive=TestZip(root,"clothes.zip",code,"installed data");
        var entry=NewEntry("Roupas de teste",archive);
        var state=new GeneralModState{DownloadFolder=root,Language="pt"};
        state.List.Mods.Add(entry);
        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};
        TestForm(catalog,state,Path.Combine(root,"d"),root,async form=>{
            form.SetDestination(game);
            var work=form.MakeWork(entry);
            form.Generic(game).Install(archive,work.Receipt(0),null);
            string existing=Safe.In(game,older);
            File.WriteAllText(existing,"{\"quality\":1}");
            await form.RememberModOutputBaseline(work,game);
            string generated=Safe.In(game,later);
            Directory.CreateDirectory(Path.GetDirectoryName(generated));
            File.WriteAllText(generated,"runtime only");
            state.List.Mods.RemoveAll(mod=>mod.Id==entry.Id);
            foreach(var preset in state.List.Presets){
                preset.ModIds.Remove(entry.Id);
                preset.Configurations.Remove(entry.Id);
            }
            await form.Scan();
            var installed=state.List.Mods.Single(mod=>mod.Id==entry.Id);
            if(installed.Name!=entry.Name||state.List.Presets.Any(preset=>preset.ModIds.Contains(entry.Id))||
                !state.RecoveredModIds.Contains(entry.Id)||!form.ModRows().Any(row=>Equals(row.Tag,entry.Id)))
                throw new IOException("O cadastro deve recuperar o nome e aparecer sem entrar no preset.");
            var files=form.DisplayedInventory().Where(file=>file.Kind=="associated").ToDictionary(file=>file.Path,InventoryPaths);
            if(!files[older].PreexistingOutput||!files[later].GeneratedOutput||files[later].ModId!=entry.Id)
                throw new IOException("O scan em nova sessão deve separar o arquivo anterior da saída posterior.");
            File.WriteAllText(Safe.In(game,code),"modified data");
            await form.Scan();
            if(!form.DisplayedInventory().Any(file=>file.Path==code&&file.RecordedChange)||
                form.DisplayedInventory().Any(file=>file.Path==later&&file.Kind=="associated"))
                throw new IOException("Alterar a referência deve invalidar a associação sem transformar o arquivo editado em saída nova.");
            File.WriteAllText(Safe.In(game,code),"installed data");
            state.List.Mods.RemoveAll(mod=>mod.Id==entry.Id);
            state.SuppressedModRecovery.Add(entry.Id);
            await form.Scan();
            if(state.List.Mods.Any(mod=>mod.Id==entry.Id)||!File.Exists(generated))
                throw new IOException("Um cadastro removido intencionalmente deve continuar removido.");
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
