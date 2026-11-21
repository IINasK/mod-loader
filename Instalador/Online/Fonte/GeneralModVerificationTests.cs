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
            List=GeneralModLibrary.Seed(catalog,new ModImportItem[0]),DownloadFolder=downloads,Language="pt",Theme="dark"
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
            form.startupLoading=true;
            form.BuildInterface();
            if(!form.startupSkeleton.Visible||form.mainLayout.Visible||form.modTableScroll.Visible||
                form.startupSkeleton.Bounds!=form.ClientRectangle||form.startupSkeleton.TabStop)
                throw new IOException("O esqueleto deve preencher o projeto e deixar os controles fora da navegação.");
            CaptureUi(form,Safe.In(root,"startup-skeleton.png"));
            var skeleton=form.startupSkeleton;
            using(var frame=new System.Drawing.Bitmap(skeleton.Width,skeleton.Height))
            using(var graphics=System.Drawing.Graphics.FromImage(frame))
            using(var paint=new PaintEventArgs(graphics,skeleton.ClientRectangle)){
                int dpi=skeleton.DeviceDpi;
                int mark=132*dpi/96,top=45*dpi/96;
                long atMark=(long)(mark+skeleton.Width/6)*1450/(skeleton.Width+skeleton.Width/3);
                skeleton.PaintFrame(paint,null);
                var resting=frame.GetPixel(mark,top);
                skeleton.PaintFrame(paint,atMark);
                if(frame.GetPixel(mark,top)==resting)
                    throw new IOException("O brilho deve avançar pelo cabeçalho quando há animação.");
                skeleton.PaintFrame(paint,atMark+600);
                if(frame.GetPixel(mark,top)!=resting)
                    throw new IOException("O brilho deve devolver a cor original depois de passar.");
                frame.Save(Safe.In(root,"startup-skeleton-next.png"));
            }
            bool sawLocalScan=false;
            form.testStage=(key,phase)=>{
                if(phase!="scan.begin")return;
                sawLocalScan=true;
                if(form.verifyAction.Visible)throw new IOException("Recarregar deve sumir no scan automático.");
            };
            await form.Scan();
            if(form.startupSkeleton.Visible||!form.mainLayout.Visible||!form.modTableScroll.Visible||form.startupLoading)
                throw new IOException("O fim da conferência local deve exibir o projeto e ocultar o esqueleto.");
            CaptureUi(form,Safe.In(root,"startup-project-ready.png"));
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
                    var confirmation=Application.OpenForms.OfType<GeneralInstallConfirmation>().FirstOrDefault(item=>item.Visible);
                    try{
                        if(DateTime.UtcNow>=deadline)throw new IOException("A revisão não avançou até a etapa "+step+".");
                        if(step==0&&review!=null){
                            if(review.Modal||!form.Enabled||!GeneralDialog.FocusOpen<GeneralDependencyReview>(form,"verify"))
                                throw new IOException("O relatório fica aberto sem bloquear o projeto e a mesma janela recebe o foco.");
                            form.ShowProjectDetails();form.ShowProjectDetails();
                            var other=form.OwnedForms.OfType<GeneralDependencyReview>().Where(item=>item.Visible&&item!=review).ToArray();
                            if(other.Length!=1||other[0].Modal||!form.Enabled)
                                throw new IOException("Dois relatórios diferentes podem ficar abertos, uma janela por função.");
                            other[0].Close();
                            var rows=AllControls(review).OfType<DataGridView>().Single(grid=>grid.Name=="DependencyResults");
                            var row=rows.Rows.Cast<DataGridViewRow>().Single(item=>item.Tag is GeneralDependencyIssue);
                            if(!Equals(row.Cells[1].Value,owner.Name)||!Equals(row.Cells[2].Value,dependency.Name)||
                                rows.CellBorderStyle!=DataGridViewCellBorderStyle.None||!Equals(row.Cells[0].Value,true))
                                throw new IOException("A revisão deve mostrar a dependência ausente e o mod que precisa dela, com seleção à esquerda.");
                            rows.CurrentCell=null;
                            rows.CurrentCell=row.Cells[1];
                            AllControls(review).OfType<Button>().Single(button=>button.Name=="OpenDependencyPage").PerformClick();
                            if(!opened.SequenceEqual(new[]{"https://www.nexusmods.com/cyberpunk2077/mods/400?tab=files&file_id=502"}))
                                throw new IOException("A revisão deve abrir somente a página oficial do arquivo fixado.");
                            if(AllControls(review).Any(control=>control.Name=="ShowUnlistedInventory"))
                                throw new IOException("Itens fora do catálogo pertencem à tabela principal, fora do relatório de pendências.");
                            var installButton=AllControls(review).OfType<Button>().Single(button=>button.Name=="InstallCatalogDependencies");
                            var toggle=typeof(DataGridView).GetMethod("OnCellContentClick",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
                            toggle.Invoke(rows,new object[]{new DataGridViewCellEventArgs(0,row.Index)});
                            if(installButton.Enabled||!Equals(row.Cells[0].Value,false))
                                throw new IOException("Desmarcar a dependência desativa a ação de instalar.");
                            toggle.Invoke(rows,new object[]{new DataGridViewCellEventArgs(0,row.Index)});
                            step=2;
                            installButton.PerformClick();
                        }else if(step==2&&confirmation!=null){
                            var rows=AllControls(confirmation).OfType<DataGridView>().Single(grid=>grid.Name=="InstallChoices");
                            if(rows.Rows.Count!=1||!Equals(rows.Rows[0].Cells[1].Value,dependency.Name)||!Equals(rows.Rows[0].Cells[0].Value,true))
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
                        var repair=rows.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is GeneralDependencyIssue&&((GeneralDependencyIssue)row.Tag).CanRepair);
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
            var detectedRows=form.grid.Rows.Cast<DataGridViewRow>().Where(row=>row.Visible&&row.Tag is P2PSyncDifference).ToArray();
            var selectedRow=detectedRows.SingleOrDefault(row=>InventoryPaths.Equals(((P2PSyncDifference)row.Tag).Path,extraPath));
            if(detectedRows.Length!=2||selectedRow==null||form.state.List.Mods.Any(mod=>mod.Name=="Mod detectado")||
                AllControls(form).Any(control=>control.Name=="ReviewDetectedMods"))
                throw new IOException("O scan deve exibir o item na tabela, sem cadastrar nem repetir o atalho no resumo. Linhas: "+detectedRows.Length+"; inventário: "+String.Join(", ",form.gameInventory.Select(item=>item.Kind+" "+item.Path)));
            using(var timer=new Timer{Interval=40}){
                timer.Tick+=(s,e)=>{
                    var editor=Application.OpenForms.OfType<GeneralModEditor>().FirstOrDefault(item=>item.Visible);
                    if(editor==null)return;
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
                form.OpenRowEditor(selectedRow);
                await Until(()=>!timer.Enabled&&(dialogFailure!=null||!form.scanning&&form.state.List.Mods.Any(mod=>mod.Name=="Mod detectado")));
            }
            if(dialogFailure!=null)throw dialogFailure;
            var adopted=form.library.Load().List.Mods.Single(mod=>mod.Name=="Mod detectado");
            var adoptedWork=form.MakeWork(form.state.List.Mods.Single(mod=>mod.Id==adopted.Id));
            if(!new HashSet<string>(adopted.DetectedPaths).SetEquals(new[]{extraPath})||
                form.Status(adoptedWork,game)!=GeneralStatusPresentation.DetectedFiles||
                !form.VisibleGameInventory().Select(file=>file.Path).SequenceEqual(new[]{related})||form.state.SnapshotReceipts.ContainsKey(adoptedWork.Key)||
                File.ReadAllText(manualPath)!="manual"||File.ReadAllText(relatedPath)!="manual script")
                throw new IOException("Incluir o item deve registrar apenas os arquivos do grupo escolhido sem assumir instalação ou mexer nos arquivos.");

            string viewed=null;
            using(var review=new GeneralDependencyReview(new[]{VerificationLine("Teste","Verificação")},new[]{
                new GeneralDependencyIssue{Mod="Mod",Requirement="Arquivo",Url="file:///C:/test.zip",InstalledModId=owner.Id}
            },opened.Add,viewInstalled:id=>viewed=id)){
                review.Show(form);
                var rows=AllControls(review).OfType<DataGridView>().Single(grid=>grid.Name=="DependencyResults");
                rows.CurrentCell=null;
                rows.CurrentCell=rows.Rows[0].Cells[1];
                AllControls(review).OfType<Button>().Single(button=>button.Name=="ViewInstalledDependency").PerformClick();
                if(viewed!=owner.Id)throw new IOException("A revisão deve abrir os detalhes do mod instalado conhecido.");
                var browser=AllControls(review).OfType<Button>().Single(button=>button.Name=="OpenDependencyPage");
                browser.PerformClick();
                if(browser.Enabled||opened.Count!=1)
                    throw new IOException("O relatório não pode abrir uma referência de arquivo local como página.");
            }

            var updated=state.List.Mods.Single(mod=>mod.Id==dependency.Id);
            var variant=updated.Variants.Single();
            variant.CyberpunkId=null;
            variant.Sources=new List<string>{"https://github.com/example/fixture/releases/download/v2.0/fixture.zip"};
            string updatedZip=TestZip(root,"dependency-v2.zip",dependency.Files.Single().Path,"new verified version");
            var current=form.MakeWork(updated);
            form.Generic(game).Install(updatedZip,current.Receipt(0),null);
            var audit=new ProjectVerification(false);
            await form.Scan(audit);
            var offered=new Dictionary<string,Work>();
            if(form.ReviewDependencies(audit,offered).Any(issue=>issue.Requirement==dependency.Name)||offered.Count!=0)
                throw new IOException("A verificação precisa aceitar a versão ligada a um recibo atualizado, mesmo quando o hash do catálogo antigo for diferente.");
            var candidatePage=new GeneralNexusLink{Game="cyberpunk2077",Mod=9000};
            var request=new GeneralNexusRequirement{Name=updated.Name,Source=candidatePage,MinFile=34,MaxFile=0};
            var proposed=form.CreateDependency(request,form.ActivePreset);
            proposed.Tags.Add("API tag");
            var proposedConfig=GeneralPresetProfiles.Copy(proposed);
            form.ActivePreset.Configurations[proposed.Id]=proposedConfig;
            var metadata=new GeneralSourceInfo{Name=updated.Name,Tags=new[]{"API tag"},NexusChecked=true};
            Func<HashSet<string>,bool> reconcile=required=>form.ReconcileDependencySuggestions(
                form.state.List.Mods.Select(form.MakeWork).ToList(),page=>metadata,required);
            var liveRequirement=new HashSet<string>{candidatePage.Url};
            if(reconcile(null))throw new IOException("Um nome semelhante sem exigência online para revisar precisa ficar intacto.");
            proposedConfig.Description="My manual note";
            if(reconcile(liveRequirement))throw new IOException("Uma personalização de preset deve preservar a sugestão.");
            proposedConfig.Description=null;
            proposed.Variants[0].RequiredMods.Add(owner.Id);
            if(reconcile(liveRequirement))throw new IOException("Uma dependência própria deve preservar o cadastro.");
            proposed.Variants[0].RequiredMods.Clear();
            proposed.Tags.Add("Handpicked");
            if(reconcile(liveRequirement))throw new IOException("Tags não atribuíveis à API devem ser preservadas.");
            proposed.Tags.Remove("Handpicked");
            if(!reconcile(liveRequirement)||state.List.Mods.Any(mod=>mod.Id==proposed.Id)||
                form.ActivePreset.ModIds.Contains(proposed.Id)||!form.ActivePreset.ModIds.Contains(updated.Id)||
                File.ReadAllText(dependencyPath)!="new verified version")
                throw new IOException("Uma sugestão automática repetida deve sair da fila sem substituir o mod instalado.");

            File.WriteAllText(dependencyPath,"changed after installation");
            audit=new ProjectVerification(false);
            await form.Scan(audit);
            var altered=form.ReviewDependencies(audit,offered).SingleOrDefault(issue=>issue.Requirement==dependency.Name);
            if(altered==null||altered.Status==T("Falta instalar","Installation missing")||offered.Count!=0)
                throw new IOException("Dependência alterada precisa aparecer para revisão, sem reinstalação em fila.");
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
