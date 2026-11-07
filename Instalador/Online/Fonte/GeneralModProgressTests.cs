using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal sealed partial class GeneralModWindow {
    internal static bool DensityCountsAsInstalled(FixedCatalog catalog,GeneralModState state,string data){
        using(var form=new GeneralModWindow(catalog,new DownloadCache(Path.Combine(data,"cache")),data,state)){
            string status=form.Status(form.MakeWork(state.List.Mods.Single()),state.Destination);
            var pending=new List<Work>();
            var downloads=form.PlanSelected(new List<Work>{form.MakeWork(state.List.Mods.Single())},state.Destination,pending);
            return GeneralStatusPresentation.Kind(status)=="installed"&&pending.Count==0&&downloads.Count==0;
        }
    }

    internal static void RunProgressTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"online-testes-progress-");
        Directory.CreateDirectory(root);
        string downloads=Path.Combine(root,"downloads");
        Directory.CreateDirectory(downloads);
        var checks=new List<string>();
        Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        var catalog=FixedData.Load();

        string firstZip=TestZip(root,"first.zip","mods/first.txt","first transaction");
        string secondZip=TestZip(root,"second.zip","mods/second.txt","second transaction");
        string lastZip=TestZip(root,"last.zip","mods/last.txt","last transaction");

        string scanRoot=Path.Combine(root,"scan-target");
        Directory.CreateDirectory(scanRoot);
        var scanState=new GeneralModState{Destination=scanRoot,DownloadFolder=downloads,List=new GeneralModList()};
        var scanFirst=NewEntry("Verificação pulada",firstZip);
        var scanSecond=NewEntry("Verificação seguinte",secondZip);
        scanState.List.Mods.AddRange(new[]{scanFirst,scanSecond});
        TestForm(catalog,scanState,Path.Combine(root,"scan-data"),root,async form=>{
            check(!form.next.Visible,"Pular fica oculto enquanto a janela está ociosa.");
            string skippedKey=form.MakeWork(scanFirst).Key;
            bool requested=false,following=false;
            form.testStage=(key,stage)=>{
                if(stage=="scan.begin"&&key==skippedKey){
                    check(form.next.Visible&&form.currentProgress.Value<100,"Verificação começa com Pular visível e sem declarar 100%.");
                    check(form.next.Parent.ClientRectangle.Contains(form.next.Bounds),
                        "O botão Pular cabe inteiro na linha de progresso: "+form.next.Bounds+" em "+form.next.Parent.ClientRectangle);
                    form.RequestSkip();requested=true;
                }
                if(stage=="scan.begin"&&key==form.MakeWork(scanSecond).Key)following=true;
            };
            await form.Scan();
            string status;
            int percent;
            check(requested&&following,"Pular a verificação atual permite conferir o próximo mod.");
            check(!form.statuses.TryGetValue(skippedKey,out status)||status!="Instalado","Verificação pulada não declara o mod instalado.");
            check(!form.progress.TryGetValue(skippedKey,out percent)||percent<100,"Verificação pulada não grava 100% na linha.");
            check(!Directory.Exists(Path.Combine(scanRoot,"mods")),"Varredura com Pular não altera o destino.");
            check(!form.next.Visible&&!form.scanning,"Pular volta a ficar oculto ao terminar a varredura.");
        });

        string installRoot=Path.Combine(root,"install-target");
        Directory.CreateDirectory(installRoot);
        var installState=new GeneralModState{Destination=installRoot,DownloadFolder=downloads,List=new GeneralModList()};
        var multiple=NewEntry("Duas fontes",firstZip);
        multiple.Variants[0].Sources.Add(secondZip);
        var followingEntry=NewEntry("Próximo mod",lastZip);
        installState.List.Mods.AddRange(new[]{multiple,followingEntry});
        TestForm(catalog,installState,Path.Combine(root,"install-data"),root,async form=>{
            var first=form.MakeWork(multiple);
            var following=form.MakeWork(followingEntry);
            bool requested=false,checkedTransaction=false;
            form.testStage=(key,stage)=>{
                if(stage=="install.begin"){
                    check(form.currentProgress.Value<100,"Instalação ainda não verificada conserva progresso abaixo de 100%.");
                    if(key==first.Key&&!requested){form.RequestSkip();requested=true;}
                }
                if(stage=="install.end"&&key==first.Key){
                    check(File.Exists(Path.Combine(installRoot,"mods","first.txt")),"Pular durante a instalação deixa a transação atual terminar.");
                    checkedTransaction=true;
                }
            };
            form.Enqueue(new[]{first,following},false);
            await Until(()=>!form.working);
            check(requested&&checkedTransaction,"O teste alcança a fronteira transacional antes de pular.");
            check(!File.Exists(Path.Combine(installRoot,"mods","second.txt")),"Pular impede a próxima fonte do mesmo mod.");
            check(form.Generic(installRoot).Status(first.Receipt(0))==GeneralModInstaller.Installed,"A primeira fonte concluída conserva recibo íntegro.");
            check(form.Status(first,installRoot)!="Instalado"&&form.statuses[first.Key]!="Instalado","Um mod parcialmente aplicado não recebe estado de instalação completa.");
            check(form.Status(following,installRoot)=="Instalado"&&form.progress[following.Key]==100,"A fila continua e só marca 100% após confirmar o próximo mod.");
            check(form.operationTotal==2&&form.operationFinished==2&&form.totalProgress.Value==50,"Completed jobs do not hide missing mods: the partially skipped mod keeps coverage at 50%.");
            check(!form.next.Visible,"Pular fica oculto após concluir a fila.");
        });

        string priorityRoot=Path.Combine(root,"priority-target");
        Directory.CreateDirectory(priorityRoot);
        File.Copy(lastZip,Path.Combine(downloads,"unrelated.zip"));
        var priorityState=new GeneralModState{Destination=priorityRoot,DownloadFolder=downloads,List=new GeneralModList()};
        var normalEntry=NewEntry("Download sem identidade","https://example.com/normal.zip");
        var urgentEntry=NewEntry("Prioritário",firstZip);
        urgentEntry.Variants[0].Sources.Add(secondZip);
        priorityState.List.Mods.AddRange(new[]{normalEntry,urgentEntry});
        TestForm(catalog,priorityState,Path.Combine(root,"priority-data"),root,async form=>{
            var normal=form.MakeWork(normalEntry);
            var urgent=form.MakeWork(urgentEntry);
            int pages=0;bool requested=false,ended=false;
            form.launch=url=>pages++;
            form.testStage=(key,stage)=>{
                if(key==urgent.Key&&stage=="install.begin"&&!requested){form.RequestSkip();requested=true;}
                if(key==urgent.Key&&stage=="work.end")ended=true;
            };
            form.Enqueue(new[]{normal},false);
            await Until(()=>form.NeedUnknownFile&&pages==1);
            check(!File.Exists(Path.Combine(priorityRoot,"mods","last.txt")),"Um link desconhecido não associa automaticamente outros arquivos disponíveis.");
            form.Enqueue(new[]{urgent},true);
            await Until(()=>ended&&form.priorityCurrent==null&&form.NeedUnknownFile);
            check(requested&&form.current!=null&&form.current.Key==normal.Key&&form.statuses[normal.Key]!="Pulado","Pular a prioridade mantém o mod normal aguardando download.");
            check(File.Exists(Path.Combine(priorityRoot,"mods","first.txt"))&&!File.Exists(Path.Combine(priorityRoot,"mods","second.txt")),"A prioridade interrompe apenas as próprias fontes após concluir a transação.");
            check(pages==1&&form.operationFinished==1&&form.operationTotal==2&&form.totalProgress.Value<100,"A espera original conserva uma página e progresso geral incompleto.");
            form.association=lastZip;
            await Until(()=>!form.working);
            check(form.Status(normal,priorityRoot)=="Instalado"&&pages==1,"Associação explícita conclui o mod original sem abrir outra página.");
            check(form.operationFinished==2&&form.operationTotal==2&&form.totalProgress.Value==50,"Priority queue finishes while the partially skipped entry remains visible in pack coverage.");
        });
        string editRoot=Path.Combine(root,"edit-target");Directory.CreateDirectory(editRoot);
        var editState=new GeneralModState{Destination=editRoot,DownloadFolder=downloads,List=new GeneralModList()};
        var hold=NewEntry("Espera","https://example.com/hold.zip");
        var queuedEntry=NewEntry("Enfileirado",firstZip);editState.List.Mods.AddRange(new[]{hold,queuedEntry});
        TestForm(catalog,editState,Path.Combine(root,"edit-data"),root,async form=>{
            form.launch=url=>{};
            form.Enqueue(new[]{form.MakeWork(hold),form.MakeWork(queuedEntry)},false);
            await Until(()=>form.NeedUnknownFile);
            var renamed=Safe.Json.Deserialize<GeneralModEntry>(Safe.Json.Serialize(queuedEntry));renamed.Name="Renomeado";
            form.ApplyEdited(queuedEntry,renamed,renamed.DefaultVariant,true);
            check(form.normal.Count==1&&form.normal[0].Entry.Name=="Renomeado","Editar metadados atualiza a referência do item pendente.");
            var changed=Safe.Json.Deserialize<GeneralModEntry>(Safe.Json.Serialize(renamed));changed.Variants[0].Sources[0]=secondZip;
            form.ApplyEdited(renamed,changed,changed.DefaultVariant,true);
            check(form.normal.Count==0&&form.operationTotal==1,"Alterar a fonte retira a autorização antiga da fila.");
            form.RequestSkip();await Until(()=>!form.working);
            check(!Directory.Exists(Path.Combine(editRoot,"mods")),"Fonte antiga não é instalada depois da edição.");
            form.Enqueue(new[]{form.MakeWork(changed)},true);await Until(()=>!form.working);
            check(form.Status(form.MakeWork(changed),editRoot)=="Instalado"&&File.Exists(Path.Combine(editRoot,"mods","second.txt"))&&!File.Exists(Path.Combine(editRoot,"mods","first.txt")),"Nova instalação explícita usa somente a fonte editada.");
        });
        string errorRoot=Path.Combine(root,"error-target");Directory.CreateDirectory(errorRoot);
        var errorState=new GeneralModState{Destination=errorRoot,DownloadFolder=downloads,List=new GeneralModList()};
        var badFirst=NewEntry("Falha A",Path.Combine(root,"missing-a.zip"));var badSecond=NewEntry("Falha B",Path.Combine(root,"missing-b.zip"));
        errorState.List.Mods.AddRange(new[]{badFirst,badSecond});
        TestForm(catalog,errorState,Path.Combine(root,"error-data"),root,async form=>{
            int prompts=0;form.testFailurePrompt=message=>{prompts++;return true;};
            form.Enqueue(new[]{form.MakeWork(badFirst),form.MakeWork(badSecond)},false);await Until(()=>!form.working);
            check(prompts==1&&form.operationFinished==2,"Uma confirmação silencia somente os próximos avisos da mesma fila.");
            check(form.statuses[form.MakeWork(badFirst).Key]=="Falhou"&&form.statuses[form.MakeWork(badSecond).Key]=="Falhou","Suprimir avisos não transforma falhas em instalações.");
            string firstKey=form.MakeWork(badFirst).Key;string reason=form.StatusDetail(firstKey,form.statuses[firstKey]);
            form.DrawRows();
            var failedRow=form.ModRows().Single(row=>Equals(row.Tag,badFirst.Id));
            check(reason.Contains("missing-a.zip")&&failedRow.Cells["Status"].ToolTipText==reason,"Mensagem completa do erro permanece após redesenhar a lista.");
            string diagnostic=form.FailureReport();
            check(diagnostic.Contains(GeneralUpdateChecker.CurrentVersion)&&diagnostic.Contains("missing-a.zip")&&diagnostic.Contains("missing-b.zip")&&diagnostic.Contains(badFirst.Id),"Diagnóstico copiável inclui versão, IDs e todas as falhas sem esconder a causa.");
            var copy=form.grid.ContextMenuStrip.Items.OfType<System.Windows.Forms.ToolStripItem>().Single(item=>item.Name=="CopyInstallFailures");copy.PerformClick();
            check(System.Windows.Forms.Clipboard.GetText()==diagnostic,"Menu copia o diagnóstico completo em vez da etiqueta Falhou.");
            form.Enqueue(new[]{form.MakeWork(badFirst)},false);await Until(()=>!form.working);
            check(prompts==2,"Nova instalação volta a mostrar o aviso; a preferência não é permanente.");
        });
        try{FixedData.Expand(catalog,new[]{"unknown-test-mod"});throw new Exception("Unknown mod accepted");}catch(IOException ex){check(ex.Message.Contains("unknown-test-mod"),"Erro de catálogo identifica a referência ausente sem relaxar a validação.");}
        string oldArchive=TestZip(downloads,"old-version.zip","mods/version.txt","old version");string newArchive=TestZip(downloads,"new-version.zip","mods/version.txt","new version");
        var exactAsset=new FixedAsset{Id="version-fixture",Name="new-version.zip",Format="zip",Sha256=Safe.Hash(newArchive),Size=new FileInfo(newArchive).Length};
        var sourceCache=new DownloadCache(Path.Combine(root,"version-cache"),()=>downloads);
        bool oldRefused=false;try{sourceCache.Import(oldArchive,exactAsset);}catch(IOException){oldRefused=true;}
        check(oldRefused&&File.Exists(oldArchive),"Download antigo não é usado como versão nova e permanece preservado.");
        sourceCache.Import(newArchive,exactAsset);check(sourceCache.Contains(exactAsset)&&sourceCache.PathFor(exactAsset)==newArchive&&File.Exists(oldArchive),"Download correto é reaproveitado sem apagar as versões antigas.");
        bool remembered=false;var previousAnimation=GeneralStyle.AnimationsEnabled;GeneralStyle.AnimationsEnabled=false;
        using(var timer=new System.Windows.Forms.Timer{Interval=100}){
            timer.Tick+=(sender,args)=>{
                var dialog=System.Windows.Forms.Application.OpenForms.Cast<System.Windows.Forms.Form>().OfType<GeneralDialog>().FirstOrDefault(f=>f.Text=="Queue failure fixture");if(dialog==null)return;timer.Stop();
                var box=dialog.Content.Controls.Find("queue-errors-suppress",true).OfType<System.Windows.Forms.CheckBox>().Single();
                check(!box.Checked&&box.Visible,"Aviso abre com supressão desmarcada e opção visível.");box.Checked=true;
                using(var image=new System.Drawing.Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(image,new System.Drawing.Rectangle(System.Drawing.Point.Empty,image.Size));image.Save(Path.Combine(root,"queue-failure.png"));}
                dialog.Complete(System.Windows.Forms.DialogResult.OK);
            };
            timer.Start();GeneralDialog.Message(null,"Fixture: missing archive. Failures remain in the list.","Queue failure fixture",System.Windows.Forms.MessageBoxButtons.OK,System.Windows.Forms.MessageBoxIcon.Warning,System.Windows.Forms.MessageBoxDefaultButton.Button1,value=>remembered=value,"Hide further warnings for this installation");
        }
        GeneralStyle.AnimationsEnabled=previousAnimation;check(remembered,"Confirmar aviso respeita a escolha marcada no diálogo real.");
        Safe.WriteJson(Path.Combine(root,"resultado-progresso-geral.json"),new{Passed=true,Count=checks.Count,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
