using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class OnlineFileApprovalWindowTests {
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static object Get(object o,string name){return o.GetType().GetField(name,Private).GetValue(o);}
    static object Call(object o,string name,params object[] args){try{return o.GetType().GetMethod(name,Private).Invoke(o,args);}catch(TargetInvocationException ex){throw ex.InnerException??ex;}}
    static bool Wait(Task<bool> task){var timer=Stopwatch.StartNew();while(!task.IsCompleted){if(timer.Elapsed.TotalSeconds>30)throw new Exception("Teste de aceite excedeu o limite.");Application.DoEvents();Thread.Sleep(5);}Application.DoEvents();return task.GetAwaiter().GetResult();}
    public static void Run(string parent){
        string root=Path.Combine(Path.GetFullPath(parent),"online-testes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        FixedAsset asset;var catalog=OnlineTests.MakeCatalog(root,out asset);var mod=catalog.Packages[1];mod.Files[0].Size--;
        string game=OnlineTests.Fixture(root,"game");foreach(var file in catalog.Packages[0].Files){string p=Safe.In(game,file.Path);Directory.CreateDirectory(Path.GetDirectoryName(p));File.WriteAllText(p,"test "+file.Path,new UTF8Encoding(false));}
        var cache=new DownloadCache(Path.Combine(root,"cache"));cache.Import(Path.Combine(root,"fixture.zip"),asset);
        var passed=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new Exception(name);passed.Add(name);};
        int consentCalls=0,attempts=0;
        using(var window=new OnlineWindow(catalog,cache,true)){
            window.ShowInTaskbar=false;window.StartPosition=FormStartPosition.Manual;window.Location=new System.Drawing.Point(-30000,-30000);window.Show();Application.DoEvents();
            ((TextBox)Get(window,"game")).Text=game;
            Func<string> work=()=>{attempts++;return new FixedInstaller(catalog,cache,game,null,true).Install(new[]{mod.Id},true,s=>{});};
            typeof(OnlineWindow).GetField("confirmFileMismatch",Private).SetValue(window,new Func<FileApprovalRequest,bool>(r=>{consentCalls++;return false;}));
            bool declined=Wait((Task<bool>)Call(window,"Run",work));
            check(!declined&&attempts==1&&consentCalls==1,"Recusar a diferença não repete a instalação");
            check(!File.Exists(Safe.In(game,mod.Files[0].Path))&&!File.Exists(Safe.In(game,".CoopModsOnline/local-approvals/identidades.dat")),"Recusa não grava aprovação nem o arquivo divergente no jogo");
            var grid=(DataGridView)Get(window,"grid");var row=grid.Rows.Cast<DataGridViewRow>().Single(r=>Convert.ToString(r.Tag)==mod.Id);
            check(Convert.ToString(row.Cells["State"].Value)=="Falhou"&&row.Cells["State"].ToolTipText.Contains(asset.Name),"Erro identifica o pacote na linha correta");
            attempts=0;consentCalls=0;
            typeof(OnlineWindow).GetField("confirmFileMismatch",Private).SetValue(window,new Func<FileApprovalRequest,bool>(r=>{consentCalls++;return true;}));
            bool accepted=Wait((Task<bool>)Call(window,"Run",work));
            check(accepted&&attempts==2&&consentCalls==1,"Aceite explícito registra a identidade e repete a instalação automaticamente uma vez");
            check(FixedData.Scan(mod,game).IsCorrect,"Instalação aceita é reconhecida após nova conferência");
            Call(window,"RefreshMods",new List<FixedMod>{mod},game);Application.DoEvents();
            check(!String.IsNullOrWhiteSpace(row.Cells["Installed"].ErrorText)&&row.Cells["Installed"].ErrorText.Contains("autorização manual"),"Ícone de aviso na célula identifica instalação com diferença aceita");
            check(!((bool)Get(window,"busy"))&&((Label)Get(window,"summary")).Text.Contains("Falhas: 0"),"Interface volta a responder e limpa a falha após concluir");
            check(!String.IsNullOrEmpty(ModDetailsInfo.Read(mod,game,"",catalog).Notes)&&ModDetailsInfo.Read(mod,game,"",catalog).Notes.Contains("autorização manual"),"Detalhes do mod também explicam a diferença aceita");
            window.Close();
        }
        Safe.WriteJson(Path.Combine(root,"resultado-aceite-interface.json"),new{Passed=passed.Count,Tests=passed,FixtureOnly=true,RealGameTouched=false});File.WriteAllText(Path.Combine(parent,"ultimo-teste-aceite-interface.txt"),root);
    }
}
