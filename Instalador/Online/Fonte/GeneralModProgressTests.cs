using System;
using System.Collections.Generic;
using System.IO;

internal sealed partial class GeneralModWindow {
    internal static void RunProgressTests(string parent) {
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-progress-"+Guid.NewGuid().ToString("N"));
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
            check(form.operationTotal==2&&form.operationFinished==2&&form.totalProgress.Value==100,"Progresso geral contabiliza os dois itens processados, inclusive o pulado.");
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
            check(form.operationFinished==2&&form.operationTotal==2&&form.totalProgress.Value==100,"A fila com prioridade termina com todos os trabalhos contabilizados.");
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
        Safe.WriteJson(Path.Combine(root,"resultado-progresso-geral.json"),new{Passed=true,Count=checks.Count,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
