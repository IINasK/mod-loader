using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

internal static class GeneralAutomaticUpdateTests {
    internal static int Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"automatic-update-");Directory.CreateDirectory(root);var checks=new List<string>();
        Action<bool,string> check=(ok,text)=>{if(!ok)throw new IOException(text);checks.Add(text);};
        Action<Action,string> refuse=(action,text)=>{try{action();}catch(IOException){checks.Add(text);return;}throw new IOException("Não bloqueou: "+text);};
        var running=new Version(GeneralUpdateChecker.CurrentVersion);string current=running.ToString(3),next=new Version(running.Major,running.Minor,running.Build+1).ToString(3);
        string old=MakeExe(root,"old.exe",current,"Mod Loader"),setup=MakeExe(root,"setup.exe",next,"Mod Loader Setup");byte[] bytes=File.ReadAllBytes(setup);
        var release=new GeneralUpdateRelease{Version=next,Url="https://github.com/IINasK/mod-loader/releases/download/v"+next+"/Mod-Loader-"+next+".exe",Sha256=Safe.Hash(setup),Size=bytes.Length};
        string installed=Path.Combine(root,"installed"),cache=Path.Combine(root,"cache");Directory.CreateDirectory(installed);string destination=Path.Combine(installed,"ModLoader.exe");File.Copy(old,destination);string original=Safe.Hash(destination);
        int downloads=0,launches=0;ProcessStartInfo command=null;
        Action<GeneralUpdateRelease,Stream> download=(offer,output)=>{downloads++;output.Write(bytes,0,bytes.Length);};
        Func<ProcessStartInfo,bool> launch=info=>{launches++;command=info;return true;};
        check(!GeneralAutomaticUpdate.StageAndLaunch(release,installed,cache,current,()=>true,download,launch)&&downloads==0&&!Directory.Exists(cache),"Outra instância ativa impede download, substituição e execução.");
        var bad=Copy(release);bad.Url="https://evil.example/setup.exe";refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(bad,installed,cache,current,()=>false,download,launch),"URL de outro domínio é recusada.");
        bad=Copy(release);bad.Sha256=null;refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(bad,installed,cache,current,()=>false,download,launch),"Sem hash confiável não baixa nem executa.");
        bad=Copy(release);bad.Size=GeneralAutomaticUpdate.MaxBytes+1;refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(bad,installed,cache,current,()=>false,download,launch),"Limite de tamanho rejeita metadado excessivo.");
        refuse(()=>GeneralAutomaticUpdate.ValidateRelease(release,next),"Versão igual não atualiza nem faz downgrade.");
        foreach(string url in new[]{"http://github.com/a","https://evil.example/a","https://github.com:444/a","https://user:pass@github.com/a"})check(!GeneralAutomaticUpdate.AllowedDownload(new Uri(url)),"Redirecionamento inseguro é recusado: "+new Uri(url).Host);
        check(GeneralAutomaticUpdate.AllowedDownload(new Uri("https://release-assets.githubusercontent.com/a")),"CDN oficial do GitHub é reconhecida.");
        string failed=Path.Combine(root,"failed");bad=Copy(release);bad.Sha256=new string('0',64);
        refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(bad,installed,failed,current,()=>false,download,launch),"Hash divergente impede execução.");
        check(launches==0&&Safe.Hash(destination)==original&&Directory.GetFiles(failed).All(f=>Path.GetFileName(f)=="update.lock"),"Hash divergente limpa só seus temporários e preserva a versão instalada.");
        string interrupted=Path.Combine(root,"interrupted");
        refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(release,installed,interrupted,current,()=>false,(offer,output)=>{output.WriteByte(1);throw new IOException("interrupted");},launch),"Queda durante download mantém versão atual.");
        check(Directory.GetFiles(interrupted).All(f=>Path.GetFileName(f)=="update.lock")&&Safe.Hash(destination)==original,"Download interrompido não deixa instalador parcial ou apaga o programa.");
        string wrong=MakeExe(root,"wrong.exe",current,"Mod Loader Setup");byte[] wrongBytes=File.ReadAllBytes(wrong);bad=Copy(release);bad.Size=wrongBytes.Length;bad.Sha256=Safe.Hash(wrong);
        refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(bad,installed,Path.Combine(root,"wrong-version"),current,()=>false,(offer,output)=>output.Write(wrongBytes,0,wrongBytes.Length),launch),"PE de versão diferente é recusado mesmo com o hash anunciado.");
        string portable=MakeExe(root,"portable.exe",next,"Mod Loader"),portableHash=Safe.Hash(portable);var notSetup=Copy(release);notSetup.Size=new FileInfo(portable).Length;notSetup.Sha256=portableHash;
        refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(notSetup,installed,Path.Combine(root,"not-setup"),current,()=>false,(offer,output)=>{byte[] data=File.ReadAllBytes(portable);output.Write(data,0,data.Length);},launch),"Executável que não é Setup é recusado.");
        check(GeneralAutomaticUpdate.RegisteredDirectory(destination)==null,"Cópia portátil de teste não assume a instalação real do usuário.");
        Directory.CreateDirectory(cache);string note=Path.Combine(cache,"user-note.txt");File.WriteAllText(note,"preserve");
        check(GeneralAutomaticUpdate.StageAndLaunch(release,installed,cache,current,()=>false,download,launch),"Setup íntegro é preparado e lançado automaticamente.");
        check(command!=null&&!command.UseShellExecute&&command.CreateNoWindow&&command.WindowStyle==ProcessWindowStyle.Hidden&&command.Arguments.Contains("/VERYSILENT")&&command.Arguments.Contains("/NORESTART ")&&command.Arguments.Contains("/MODLOADERUPDATE=1")&&command.Arguments.Contains("/NOCLOSEAPPLICATIONS")&&!command.Arguments.Contains(" /FORCECLOSEAPPLICATIONS"),"Comando oculto não pede reboot, elevação ou fechamento forçado de aplicativos.");
        check(command.Arguments.Contains("/DIR=\""+installed+"\"")&&Safe.Hash(destination)==original,"Updater aponta só à instalação existente e não sobrescreve o EXE em uso por conta própria.");
        int before=launches;check(!GeneralAutomaticUpdate.StageAndLaunch(release,installed,cache,current,()=>false,download,launch)&&launches==before,"Tentativa recente não entra em ciclo se o Setup não concluir.");
        using(var handle=new FileStream(command.FileName,FileMode.Open,FileAccess.Read,FileShare.Read))check(!GeneralAutomaticUpdate.Cleanup(cache,next)&&File.Exists(command.FileName),"Arquivo em uso pelo instalador é preservado e a limpeza pode aguardar.");
        check(GeneralAutomaticUpdate.Cleanup(cache,next)&&!File.Exists(command.FileName)&&!File.Exists(Path.Combine(cache,"pending.json"))&&File.ReadAllText(note)=="preserve","Após atualizar, limpeza remove somente o Setup/registro próprios e preserva arquivos desconhecidos.");
        string unowned=Path.Combine(root,"unowned");Directory.CreateDirectory(unowned);string collision=Path.Combine(unowned,"Mod-Loader-"+next+".exe");File.WriteAllText(collision,"do not replace");
        refuse(()=>GeneralAutomaticUpdate.StageAndLaunch(release,installed,unowned,current,()=>false,download,launch),"Arquivo sem registro de propriedade no cache é preservado.");
        check(File.ReadAllText(collision)=="do not replace"&&Safe.Hash(destination)==original,"Colisão no cache não remove dados desconhecidos nem a versão atual.");
        Safe.WriteJson(Path.Combine(root,"resultado-automatic-update.json"),new{Passed=true,Tests=checks,RealInstallationTouched=false,NetworkUsed=false});return checks.Count;
    }
    internal static bool LaunchFixture(string fixtureRoot,string setup,string installed,string cache,string current){
        string root=Path.GetFullPath(fixtureRoot).TrimEnd('\\');
        if(!System.Text.RegularExpressions.Regex.IsMatch(root,@"(?i)[\\/]online-testes-[a-f0-9]{32}$")||!File.Exists(Path.Combine(root,".fixture-only")))throw new IOException("Atualização real de teste exige fixture identificada.");
        foreach(string path in new[]{setup,installed,cache}){Safe.NoLinks(path);if(!Path.GetFullPath(path).StartsWith(root+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Destino fora da fixture.");}
        var version=Version.Parse(FileVersionInfo.GetVersionInfo(setup).ProductVersion.Trim()).ToString(3);
        var release=new GeneralUpdateRelease{Version=version,Url="https://github.com/IINasK/mod-loader/releases/download/v"+version+"/Mod-Loader-"+version+".exe",Sha256=Safe.Hash(setup),Size=new FileInfo(setup).Length};
        return GeneralAutomaticUpdate.StageAndLaunch(release,installed,cache,current,()=>false,(offer,output)=>{using(var input=File.OpenRead(setup))input.CopyTo(output);},info=>{using(var process=Process.Start(info)){if(process==null)return false;File.WriteAllText(Path.Combine(root,"setup-pid.txt"),process.Id.ToString());return true;}});
    }
    static GeneralUpdateRelease Copy(GeneralUpdateRelease value){return new GeneralUpdateRelease{Version=value.Version,Page=value.Page,Url=value.Url,Sha256=value.Sha256,Size=value.Size};}
    internal static string MakeExe(string root,string name,string version,string description){
        string file=Path.Combine(root,name),source=file+".cs";
        File.WriteAllText(source,"using System.Reflection; [assembly:AssemblyVersion(\""+version+".0\")] [assembly:AssemblyFileVersion(\""+version+".0\")] [assembly:AssemblyTitle(\""+description+"\")] class Fixture { static void Main(){} }");
        string compiler=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),@"Microsoft.NET\Framework64\v4.0.30319\csc.exe");
        using(var process=Process.Start(new ProcessStartInfo(compiler,"/nologo /target:winexe /out:\""+file+"\" \""+source+"\""){UseShellExecute=false,CreateNoWindow=true,RedirectStandardError=true,RedirectStandardOutput=true})){string output=process.StandardOutput.ReadToEnd()+process.StandardError.ReadToEnd();process.WaitForExit();if(process.ExitCode!=0)throw new IOException(output);}return file;
    }
}
