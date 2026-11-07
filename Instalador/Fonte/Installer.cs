using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

public sealed class Item { public string Path; public string Sha256; public long Size; public string Component; }
public sealed class Manifest {
    public int Schema; public string Profile; public string GameVersion;
    public bool RequirePhantomLiberty; public bool RequireRedmod; public List<Item> Files;
}
public sealed class Change { public string Path; public string Before; public string After; }
public sealed class Receipt {
    public string Id; public string Profile; public string Status; public string CreatedUtc;
    public List<Change> Files = new List<Change>();
}
public static class Safe {
    [ThreadStatic] static JavaScriptSerializer json;
    public static JavaScriptSerializer Json {get{return json??(json=new JavaScriptSerializer{MaxJsonLength=16000000});}}
    public static string Hash(string file) { using (var s=File.OpenRead(file)) using (var h=SHA256.Create()) return Hex(h.ComputeHash(s)); }
    public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    public static string In(string root, string relative) {
        if (String.IsNullOrWhiteSpace(relative) || relative.IndexOf('\\')>=0 || relative.StartsWith("/") || relative.Any(c=>c<32) || relative.IndexOfAny(new[]{':','<','>','"','|','?','*'})>=0) throw new IOException("Caminho inválido no pacote.");
        foreach (string part in relative.Split('/'))
            if (part.Length==0 || part=="." || part==".." || part.EndsWith(".") || part.EndsWith(" ") || Regex.IsMatch(part,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)",RegexOptions.IgnoreCase)) throw new IOException("Caminho inseguro no pacote.");
        string parent=System.IO.Path.GetFullPath(root).TrimEnd('\\')+"\\";
        string full=System.IO.Path.GetFullPath(System.IO.Path.Combine(parent,relative.Replace('/','\\')));
        if (!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)) throw new IOException("Caminho fora da pasta autorizada.");
        NoLinks(full); return full;
    }
    public static void NoLinks(string full) {
        string p=System.IO.Path.GetFullPath(full);
        while (!String.IsNullOrEmpty(p)) {
            try { if ((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0) throw new IOException("Há um atalho de pasta ou link no destino. Escolha uma pasta local comum."); }
            catch(FileNotFoundException) { } catch(DirectoryNotFoundException) { }
            p=System.IO.Path.GetDirectoryName(p);
        }
    }
    public static void WriteJson(string file, object value) {
        NoLinks(file); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));
        string tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        using (var s=new FileStream(tmp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
        using (var w=new StreamWriter(s,new UTF8Encoding(false))) { w.Write(Json.Serialize(value)); w.Flush(); s.Flush(true); }
        if(File.Exists(file)) File.Replace(tmp,file,null); else File.Move(tmp,file);
    }
    public static void CopyAtomic(string source,string destination,string expectedHash) {
        NoLinks(source); NoLinks(destination); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
        string tmp=destination+".coopmods-"+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.Copy(source,tmp,false);
            if(Hash(tmp)!=expectedHash) throw new IOException("A cópia não passou na verificação. Tente novamente.");
            NoLinks(destination);
            if(File.Exists(destination)) File.Replace(tmp,destination,null); else File.Move(tmp,destination);
        } finally { if(File.Exists(tmp)) File.Delete(tmp); }
    }
    public static bool Allowed(string p) { return Regex.IsMatch(p,@"^(bin|engine|r6|archive|red4ext)/",RegexOptions.IgnoreCase); }
}
public sealed class Package {
    public string Root; public Manifest Manifest; public string PayloadHash;
    public static Package Load(string root,string manifestHash,string payloadHash) {
        string path=Safe.In(root,"Dados/manifesto.json");
        if(!File.Exists(path)) throw new IOException("Extraia todos os arquivos do ZIP antes de abrir o instalador. Mantenha a pasta Dados ao lado do EXE.");
        if(Safe.Hash(path)!=manifestHash) throw new IOException("O manifesto está diferente do original. Extraia uma nova cópia do ZIP recebido.");
        var p=new Package { Root=root, PayloadHash=payloadHash, Manifest=Safe.Json.Deserialize<Manifest>(File.ReadAllText(path)) };
        if(p.Manifest==null || p.Manifest.Schema!=1 || p.Manifest.Profile==null || p.Manifest.Files==null || p.Manifest.Files.Count==0 || p.Manifest.Files.Count>10000) throw new IOException("Manifesto inválido.");
        var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase); long total=0;
        foreach(var f in p.Manifest.Files) {
            if(f==null || !Safe.Allowed(f.Path)) throw new IOException("Arquivo inesperado no pacote.");
            Safe.In(root,f.Path);
            if(!paths.Add(f.Path) || f.Size<0 || f.Size>6L*1024*1024*1024 || !Regex.IsMatch(f.Sha256??"", "^[a-f0-9]{64}$")) throw new IOException("Manifesto inconsistente.");
            total=checked(total+f.Size);
        }
        if(total>8L*1024*1024*1024) throw new IOException("Pacote maior que o limite desta edição.");
        return p;
    }
    // The same verification routine is used for the full payload audit and actual installation.
    public void Extract(string stage,Action<string> report) {
        string archive=Safe.In(Root,"Dados/conteudo.zip");
        report("Conferindo o pacote recebido…");
        if(!File.Exists(archive) || Safe.Hash(archive)!=PayloadHash) throw new IOException("O pacote está incompleto ou diferente do original. Extraia novamente o ZIP recebido.");
        var expected=Manifest.Files.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
        using(var zip=ZipFile.OpenRead(archive)) {
            if(zip.Entries.Count!=expected.Count) throw new IOException("Número de arquivos inesperado no pacote.");
            var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase); int count=0;
            foreach(var e in zip.Entries) {
                Item f;
                if(!expected.TryGetValue(e.FullName,out f) || !seen.Add(e.FullName) || e.Length!=f.Size || ((e.ExternalAttributes>>16)&0xF000)==0xA000) throw new IOException("Conteúdo inesperado no ZIP.");
                string dest=stage==null ? null : Safe.In(stage,e.FullName);
                report("Conferindo arquivos: "+(++count)+" de "+expected.Count);
                if(dest!=null) Directory.CreateDirectory(Path.GetDirectoryName(dest));
                using(var input=e.Open()) using(var h=SHA256.Create()) {
                    Stream output=dest==null ? Stream.Null : new FileStream(dest,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                    using(output) {
                        byte[] buffer=new byte[524288]; long bytes=0; int read;
                        while((read=input.Read(buffer,0,buffer.Length))>0) {
                            bytes+=read; if(bytes>f.Size) throw new IOException("Arquivo maior que o declarado.");
                            h.TransformBlock(buffer,0,read,buffer,0); output.Write(buffer,0,read);
                        }
                        h.TransformFinalBlock(new byte[0],0,0);
                        if(bytes!=f.Size || Safe.Hex(h.Hash)!=f.Sha256) throw new IOException("Arquivo danificado: "+f.Path);
                    }
                }
            }
        }
    }
}
public sealed class Installer {
    public readonly string Game; public readonly string Data; readonly string saves; readonly Package pkg; readonly bool fixture;
    string StatePath { get { return Safe.In(Data,"estado.json"); } }
    public Installer(Package p,string game,string saveRoot,bool isFixture=false) {
        pkg=p; Game=Path.GetFullPath(game).TrimEnd('\\'); Data=Path.Combine(Game,".CoopMods"); saves=saveRoot; fixture=isFixture;
        if(Game.StartsWith(@"\\") || Game.Length<=3) throw new IOException("Escolha uma pasta local que contenha o jogo.");
        if(fixture && !File.Exists(Path.Combine(Game,".fixture-only"))) throw new IOException("Ambiente de teste inválido.");
        Safe.NoLinks(Game);
    }
    public static bool VersionSupported(string version) { return Regex.IsMatch(version??"", @"^2\.(5|50)(\.0){0,2}$"); }
    public void ValidateGame() {
        if(!Environment.Is64BitOperatingSystem) throw new IOException("Este pacote precisa de Windows de 64 bits.");
        if(fixture) return;
        if(Environment.OSVersion.Version.Major<10) throw new IOException("Este pacote precisa de Windows 10 ou 11.");
        string exe=Safe.In(Game,"bin/x64/Cyberpunk2077.exe");
        if(!File.Exists(exe)) throw new IOException("Não encontrei Cyberpunk2077.exe. Escolha a pasta do jogo que contém a pasta bin.");
        string version=FileVersionInfo.GetVersionInfo(exe).ProductVersion;
        if(!VersionSupported(version)) throw new IOException("Este pacote foi preparado para o Cyberpunk 2077 2.5. Versão encontrada: "+(version??"não identificada")+". Nada foi instalado.");
        if(pkg.Manifest.RequirePhantomLiberty && !File.Exists(Safe.In(Game,"archive/pc/ep1/ep1.addcont_keystone"))) throw new IOException("Este conjunto precisa da expansão Phantom Liberty instalada. Nada foi instalado.");
        if(pkg.Manifest.RequireRedmod && !File.Exists(Safe.In(Game,"tools/redmod/bin/redMod.exe"))) throw new IOException("Esta variante precisa do REDmod gratuito, instalado separadamente pela loja do jogo.");
        if(File.Exists(Safe.In(Game,"mods/NIGHT CITY ALIVE/info.json"))) throw new IOException("Há uma versão REDMOD antiga de Night City Alive nesta pasta. Remova essa variante com o instalador que a colocou antes de continuar.");
    }
    void Closed() {
        if(fixture) return;
        foreach(var p in Process.GetProcessesByName("Cyberpunk2077")) { p.Dispose(); throw new IOException("Feche o Cyberpunk 2077 e tente novamente."); }
    }
    public void ProbeWrite() {
        ValidateGame(); ProbeDirectory();
    }
    public void ProbeDirectory() {
        Closed(); Safe.NoLinks(Game);
        string file=Path.Combine(Game,".coopmods-acesso-"+Guid.NewGuid().ToString("N"));
        using(var s=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)) { }
    }
    Receipt ReadState() {
        if(!File.Exists(StatePath)) return null;
        if(new FileInfo(StatePath).Length>8000000) throw new IOException("Registro local inválido.");
        Receipt state=Safe.Json.Deserialize<Receipt>(File.ReadAllText(StatePath));
        if(state==null || !Regex.IsMatch(state.Id??"", "^[a-f0-9]{32}$") || state.Profile!=pkg.Manifest.Profile || state.Files==null || state.Files.Count>pkg.Manifest.Files.Count) throw new IOException("Há um registro de outro pacote ou danificado. Não vou sobrescrevê-lo.");
        var known=pkg.Manifest.Files.ToDictionary(f=>f.Path,StringComparer.OrdinalIgnoreCase);
        var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var c in state.Files) {
            Item f;
            if(c==null || !known.TryGetValue(c.Path,out f) || c.After!=f.Sha256 || !seen.Add(c.Path) || (c.Before!=null&&!Regex.IsMatch(c.Before,"^[a-f0-9]{64}$"))) throw new IOException("Registro de backup inconsistente.");
            Safe.In(Game,c.Path);
        }
        return state;
    }
    string Transaction(Receipt r) { return Safe.In(Data,"instalacoes/"+r.Id); }
    void Save(Receipt r) { Safe.WriteJson(StatePath,r); Safe.WriteJson(Safe.In(Transaction(r),"registro.json"),r); }
    void CopySaves(string destination) {
        if(String.IsNullOrEmpty(saves)||!Directory.Exists(saves)) return;
        Safe.NoLinks(saves); Directory.CreateDirectory(destination);
        var todo=new Stack<string>(); todo.Push(saves);
        while(todo.Count>0) {
            string dir=todo.Pop(); Safe.NoLinks(dir);
            foreach(string sub in Directory.GetDirectories(dir)) { Safe.NoLinks(sub); todo.Push(sub); }
            foreach(string source in Directory.GetFiles(dir)) {
                Safe.NoLinks(source);
                string relative=source.Substring(saves.TrimEnd('\\').Length+1).Replace('\\','/');
                string dest=Safe.In(destination,relative); Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(source,dest,false); if(Safe.Hash(source)!=Safe.Hash(dest)) throw new IOException("Não foi possível conferir o backup dos saves. Nada foi instalado.");
            }
        }
    }
    void CleanStage(string stage) {
        string parent=Path.GetFullPath(Path.Combine(Data,"preparacao")).TrimEnd('\\')+"\\";
        if(Directory.Exists(stage)&&Path.GetFullPath(stage).StartsWith(parent,StringComparison.OrdinalIgnoreCase)) { Safe.NoLinks(stage); Directory.Delete(stage,true); }
    }
    public string Install(Action<string> report,int failAfter=-1) {
        ValidateGame(); Closed(); Safe.NoLinks(Data); Directory.CreateDirectory(Data);
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            Receipt previous=ReadState();
            if(previous!=null && previous.Status!="desfeito") {
                if(previous.Status=="instalado" && pkg.Manifest.Files.All(f=>File.Exists(Safe.In(Game,f.Path))&&Safe.Hash(Safe.In(Game,f.Path))==f.Sha256)) return "Este pacote já está instalado e foi conferido. Pode abrir o jogo.";
                throw new IOException("Há uma instalação anterior ou arquivos alterados. Use Desfazer instalação antes de reinstalar. Seus arquivos não foram sobrescritos.");
            }
            var state=new Receipt { Id=Guid.NewGuid().ToString("N"), Profile=pkg.Manifest.Profile, Status="preparando", CreatedUtc=DateTime.UtcNow.ToString("o") };
            string transaction=Transaction(state), stage=Safe.In(Data,"preparacao/"+state.Id);
            long oldBytes=pkg.Manifest.Files.Sum(f=>File.Exists(Safe.In(Game,f.Path))?new FileInfo(Safe.In(Game,f.Path)).Length:0L);
            long saveBytes=0;
            if(!String.IsNullOrEmpty(saves)&&Directory.Exists(saves)) {
                var pending=new Stack<string>(); pending.Push(saves);
                while(pending.Count>0) { string dir=pending.Pop(); Safe.NoLinks(dir); foreach(string sub in Directory.GetDirectories(dir)) { Safe.NoLinks(sub); pending.Push(sub); } foreach(string file in Directory.GetFiles(dir)) { Safe.NoLinks(file); saveBytes=checked(saveBytes+new FileInfo(file).Length); } }
            }
            long needed=checked(pkg.Manifest.Files.Sum(f=>f.Size)*2+oldBytes+saveBytes+67108864);
            if(new DriveInfo(Path.GetPathRoot(Game)).AvailableFreeSpace<needed) throw new IOException("Libere pelo menos "+Math.Ceiling(needed/1073741824.0)+" GB no disco do jogo e tente novamente.");
            Directory.CreateDirectory(transaction); Directory.CreateDirectory(stage);
            try {
                pkg.Extract(stage,report); Closed();
                foreach(var f in pkg.Manifest.Files) {
                    string dest=Safe.In(Game,f.Path); if(Directory.Exists(dest)) throw new IOException("Existe uma pasta no lugar de um arquivo do pacote: "+f.Path);
                    string before=File.Exists(dest)?Safe.Hash(dest):null;
                    if(before!=f.Sha256) state.Files.Add(new Change { Path=f.Path, Before=before, After=f.Sha256 });
                }
                Save(state); report("Guardando uma cópia dos seus saves…"); CopySaves(Safe.In(transaction,"saves-antes"));
                report("Fazendo backup dos arquivos existentes…");
                foreach(var c in state.Files.Where(c=>c.Before!=null)) {
                    string backup=Safe.In(transaction,"originais/"+c.Path); Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Copy(Safe.In(Game,c.Path),backup,false); if(Safe.Hash(backup)!=c.Before) throw new IOException("Backup não conferido. Instalação interrompida.");
                }
                state.Status="instalando"; Save(state); int n=0;
                foreach(var c in state.Files) {
                    Closed(); report("Instalando arquivos: "+(++n)+" de "+state.Files.Count);
                    string dest=Safe.In(Game,c.Path); string current=File.Exists(dest)?Safe.Hash(dest):null;
                    if(current!=c.Before) throw new IOException("Um arquivo foi alterado durante a instalação. A operação foi interrompida para preservá-lo.");
                    Safe.CopyAtomic(Safe.In(stage,c.Path),dest,c.After);
                    if(Safe.Hash(dest)!=c.After) throw new IOException("A cópia de um arquivo falhou.");
                    if(failAfter==n) throw new IOException("Falha simulada para testar a recuperação.");
                }
                state.Status="instalado"; Save(state);
                try { CleanStage(stage); } catch { report("Instalado. A preparação foi mantida para limpeza posterior."); }
                return "Pronto! Mods instalados. Você já pode abrir o jogo. A primeira abertura pode demorar enquanto os scripts são preparados.";
            } catch(Exception original) {
                if(File.Exists(StatePath)) {
                    try { report("Interrompido. Restaurando os arquivos anteriores…"); UndoCore(state,report); }
                    catch(Exception rollback) { throw new IOException(original.Message+"\r\nA restauração precisa ser retomada pelo botão Desfazer instalação. Detalhe: "+rollback.Message); }
                }
                try { CleanStage(stage); } catch { }
                throw new IOException(original.Message+"\r\nOs arquivos aplicados foram revertidos quando permaneceram iguais aos instalados. O backup foi preservado.",original);
            }
        }
    }
    string UndoCore(Receipt state,Action<string> report) {
        var kept=new List<Change>(); string transaction=Transaction(state); int done=0;
        foreach(var c in state.Files.AsEnumerable().Reverse()) {
            Closed(); string dest=Safe.In(Game,c.Path); string current=File.Exists(dest)?Safe.Hash(dest):null;
            if(current==c.Before) continue;
            if(current!=c.After) { kept.Add(c); continue; }
            report("Restaurando arquivos: "+(++done)+" de "+state.Files.Count);
            if(c.Before!=null) {
                string backup=Safe.In(transaction,"originais/"+c.Path);
                if(!File.Exists(backup)||Safe.Hash(backup)!=c.Before) throw new IOException("O backup de "+c.Path+" está ausente ou diferente. O arquivo atual foi preservado.");
                Safe.CopyAtomic(backup,dest,c.Before);
            } else { Safe.NoLinks(dest); File.Delete(dest); }
        }
        state.Files=kept; state.Status=kept.Count==0?"desfeito":"desfeito-parcial"; Save(state);
        return kept.Count==0 ? "Instalação desfeita. Seus saves atuais e os backups foram preservados." : "Instalação desfeita, com "+kept.Count+" arquivo(s) alterado(s) depois da instalação preservado(s). Os detalhes estão no registro local.";
    }
    public string Undo(Action<string> report) {
        Closed(); Safe.NoLinks(Data);
        if(!Directory.Exists(Data)) return "Não há instalação deste pacote para desfazer nesta pasta.";
        using(var mutex=new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            Receipt state=ReadState(); if(state==null||state.Status=="desfeito") return "Não há instalação ativa deste pacote para desfazer.";
            return UndoCore(state,report);
        }
    }
}
public static class Discovery {
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath(ref Guid id,uint flags,IntPtr token,out IntPtr path);
    public static string Saves() {
        Guid id=new Guid("4C5C32FF-BB9D-43B0-BF6D-474324C42412"); IntPtr p;
        if(SHGetKnownFolderPath(ref id,0,IntPtr.Zero,out p)==0) { try { string folder=Marshal.PtrToStringUni(p);if(!String.IsNullOrWhiteSpace(folder)&&Path.IsPathRooted(folder))return Path.Combine(folder,@"CD Projekt Red\Cyberpunk 2077"); } finally { Marshal.FreeCoTaskMem(p); } }
        string profile=Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if(String.IsNullOrWhiteSpace(profile)||!Path.IsPathRooted(profile))profile=Environment.GetEnvironmentVariable("USERPROFILE");
        if(String.IsNullOrWhiteSpace(profile)||!Path.IsPathRooted(profile))throw new IOException("Não foi possível localizar a pasta de saves do Windows. Nenhuma instalação foi iniciada.");
        return Path.Combine(profile,@"Saved Games\CD Projekt Red\Cyberpunk 2077");
    }
    public static List<string> Find() {
        var roots=new HashSet<string>(StringComparer.OrdinalIgnoreCase); var steam=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Action<string> add=p=>{try{if(String.IsNullOrWhiteSpace(p)||p.StartsWith(@"\\")||!Path.IsPathRooted(p)) return; Safe.NoLinks(p); if(File.Exists(Path.Combine(p,@"bin\x64\Cyberpunk2077.exe"))) roots.Add(Path.GetFullPath(p).TrimEnd('\\'));}catch{}};
        foreach(var hive in new[]{RegistryHive.CurrentUser,RegistryHive.LocalMachine}) foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32}) {
            try { using(var reg=RegistryKey.OpenBaseKey(hive,view)) {
                using(var key=reg.OpenSubKey(@"Software\Valve\Steam")) if(key!=null) { string s=(key.GetValue("SteamPath")??key.GetValue("InstallPath")) as string; if(s!=null) steam.Add(s); }
                using(var list=reg.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall")) if(list!=null) foreach(string name in list.GetSubKeyNames()) {
                    using(var key=list.OpenSubKey(name)) if(key!=null && ((key.GetValue("DisplayName") as string)??"").StartsWith("Cyberpunk 2077",StringComparison.OrdinalIgnoreCase)) add(key.GetValue("InstallLocation") as string);
                }
                using(var gog=reg.OpenSubKey(@"Software\GOG.com\Games\1423049311")) if(gog!=null) add(gog.GetValue("path") as string);
            }} catch { }
        }
        steam.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        foreach(string s in steam.ToArray()) {
            try { if(s.StartsWith(@"\\")||!Path.IsPathRooted(s)) continue; Safe.NoLinks(s); string vdf=Path.Combine(s,@"steamapps\libraryfolders.vdf"); if(File.Exists(vdf)) foreach(Match m in Regex.Matches(File.ReadAllText(vdf),"\"path\"\\s*\"([^\"]+)\"")) steam.Add(m.Groups[1].Value.Replace(@"\\",@"\")); } catch { }
        }
        foreach(string s in steam) {
            if(s.StartsWith(@"\\")||!Path.IsPathRooted(s)) continue;
            add(Path.Combine(s,@"steamapps\common\Cyberpunk 2077"));
            try { Safe.NoLinks(s); string acf=Path.Combine(s,@"steamapps\appmanifest_1091500.acf"); if(File.Exists(acf)) { Match m=Regex.Match(File.ReadAllText(acf),"\"installdir\"\\s*\"([^\"]+)\""); if(m.Success) add(Path.Combine(s,@"steamapps\common",m.Groups[1].Value)); } } catch { }
        }
        try { string epic=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),@"Epic\EpicGamesLauncher\Data\Manifests"); Safe.NoLinks(epic); if(Directory.Exists(epic)) foreach(string file in Directory.GetFiles(epic,"*.item")) {
            var map=Safe.Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(file)); object value; if(map.TryGetValue("InstallLocation",out value)) add(value as string);
        }} catch { }
        foreach(var drive in DriveInfo.GetDrives()) {
            if(drive.DriveType!=DriveType.Fixed||!drive.IsReady) continue;
            foreach(string parent in new[]{"","Games","Jogos","GOG Games","Epic Games",@"SteamLibrary\steamapps\common",@"Program Files (x86)\Steam\steamapps\common"}) {
                string dir=Path.Combine(drive.RootDirectory.FullName,parent);
                foreach(string name in new[]{"Cyberpunk 2077","Cyberpunk2077","Cyberpunk"}) add(Path.Combine(dir,name));
                try { Safe.NoLinks(dir); if(parent.Length>0 && Directory.Exists(dir)) foreach(string sub in Directory.GetDirectories(dir).Where(p=>Path.GetFileName(p).IndexOf("Cyberpunk",StringComparison.OrdinalIgnoreCase)>=0)) add(sub); } catch { }
            }
        }
        return roots.OrderBy(p=>p).ToList();
    }
}
public sealed class InstallerWindow : Form {
    readonly ComboBox paths=new ComboBox(); readonly Button choose=new Button(), install=new Button(), undo=new Button(), play=new Button();
    readonly Label status=new Label(); readonly ProgressBar progress=new ProgressBar(); Package package; bool busy; bool installedSuccessfully; readonly bool previewMode;
    protected override bool ShowWithoutActivation { get { return previewMode; } }
    public InstallerWindow(bool preview=false) {
        previewMode=preview;
        Text="Cyberpunk 2077 · Instalar mods"; Font=new Font("Segoe UI",10.5f); BackColor=Color.White; AutoScaleMode=AutoScaleMode.Font;
        ClientSize=new Size(700,555); MinimumSize=new Size(570,510); StartPosition=FormStartPosition.CenterScreen;
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,Padding=new Padding(25),ColumnCount=1,RowCount=10,AutoScroll=true };
        for(int i=0;i<10;i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text="Pronto para jogar juntos",Font=new Font("Segoe UI",22,FontStyle.Bold),AutoSize=true,ForeColor=Color.FromArgb(25,42,68),Margin=new Padding(0,0,0,8) });
        layout.Controls.Add(new Label { Text="NightCoop + 10 mods\nOs arquivos já estão neste pacote. Não é preciso baixar nada.",AutoSize=true,Margin=new Padding(0,0,0,20) });
        layout.Controls.Add(new Label { Text="1. Confira a pasta do Cyberpunk 2077",AutoSize=true,Font=new Font(Font,FontStyle.Bold) });
        var folderRow=new TableLayoutPanel { ColumnCount=2,Dock=DockStyle.Top,AutoSize=true,Margin=new Padding(0,7,0,16) };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100)); folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,155));
        paths.DropDownStyle=ComboBoxStyle.DropDownList; paths.Dock=DockStyle.Fill; choose.Text="Escolher pasta…"; choose.Dock=DockStyle.Fill; choose.Height=34;
        folderRow.Controls.Add(paths,0,0); folderRow.Controls.Add(choose,1,0); layout.Controls.Add(folderRow);
        layout.Controls.Add(new Label { Text="2. Feche o jogo e clique em Instalar mods",AutoSize=true,Font=new Font(Font,FontStyle.Bold) });
        layout.Controls.Add(new Label { Text="O instalador confere os arquivos e guarda um backup antes de alterar o jogo.\nCyberpunk 2077 2.5 + Phantom Liberty · Sem REDmod",AutoSize=true,Margin=new Padding(0,7,0,17) });
        var buttons=new FlowLayoutPanel { AutoSize=true,Dock=DockStyle.Top,WrapContents=true,Margin=new Padding(0,0,0,14) };
        install.Text="Instalar mods"; undo.Text="Desfazer instalação"; play.Text="Abrir jogo";
        foreach(var b in new[]{install,undo,play}) { b.Size=new Size(182,39); b.Margin=new Padding(0,0,9,6); buttons.Controls.Add(b); }
        install.BackColor=Color.FromArgb(30,76,135); install.ForeColor=Color.White; install.FlatStyle=FlatStyle.Flat;
        layout.Controls.Add(buttons); progress.Dock=DockStyle.Top; progress.Height=13; progress.Visible=false; layout.Controls.Add(progress);
        status.Text="Procurando o jogo…"; status.AutoSize=false; status.Height=92; status.Dock=DockStyle.Top; status.Padding=new Padding(0,12,0,0); layout.Controls.Add(status);
        layout.Controls.Add(new Label { Text="Pacote para teste. A sincronização entre os jogadores ainda precisa ser conferida.\nUse o mesmo pacote nos dois computadores.",AutoSize=true,ForeColor=Color.FromArgb(75,75,75),Margin=new Padding(0,8,0,0) });
        choose.Click+=(s,e)=>{using(var d=new FolderBrowserDialog { Description="Escolha a pasta do Cyberpunk 2077 que contém a pasta bin.",ShowNewFolderButton=false }) if(d.ShowDialog()==DialogResult.OK) { if(!paths.Items.Contains(d.SelectedPath)) paths.Items.Add(d.SelectedPath); paths.SelectedItem=d.SelectedPath; RefreshButtons(); }};
        paths.SelectedIndexChanged+=(s,e)=>RefreshButtons(); install.Click+=async(s,e)=>await Run(false); undo.Click+=async(s,e)=>await Run(true);
        play.Click+=(s,e)=>{try { string game=Selected(); new Installer(package,game,Discovery.Saves()).ValidateGame(); Process.Start(new ProcessStartInfo(Path.Combine(game,@"bin\x64\Cyberpunk2077.exe")){WorkingDirectory=Path.Combine(game,@"bin\x64"),UseShellExecute=false}); }catch(Exception x){status.Text=x.Message;}};
        FormClosing+=(s,e)=>{if(busy) { e.Cancel=true; status.Text="Aguarde a operação terminar antes de fechar. Os arquivos estão sendo conferidos."; }};
        install.Enabled=undo.Enabled=play.Enabled=false;
    }
    string Selected() { if(paths.SelectedItem==null) throw new IOException("Escolha a pasta do jogo primeiro."); return paths.SelectedItem.ToString(); }
    void RefreshButtons() { bool ready=!busy&&package!=null&&paths.SelectedItem!=null; install.Enabled=undo.Enabled=ready; play.Enabled=ready&&installedSuccessfully; choose.Enabled=!busy; paths.Enabled=!busy; }
    protected override async void OnShown(EventArgs e) {
        base.OnShown(e);
        if(previewMode) return;
        try {
            package=Package.Load(AppDomain.CurrentDomain.BaseDirectory,PackageIdentity.ManifestHash,PackageIdentity.PayloadHash);
            var found=await Task.Run(()=>Discovery.Find()); foreach(string p in found) paths.Items.Add(p);
            string[] args=Environment.GetCommandLineArgs(); int index=Array.IndexOf(args,"--game");
            if(index>=0&&index+1<args.Length) { if(!paths.Items.Contains(args[index+1])) paths.Items.Add(args[index+1]); paths.SelectedItem=args[index+1]; }
            else if(found.Count>0) paths.SelectedIndex=0;
            status.Text=found.Count==0 ? "Não encontrei o jogo automaticamente. Clique em Escolher pasta para indicar onde ele está." : "Pasta encontrada. Confira acima e clique em Instalar mods.";
            RefreshButtons(); if(args.Contains("--start-install")&&paths.SelectedItem!=null) await Run(false);
            else if(args.Contains("--start-undo")&&paths.SelectedItem!=null) await Run(true);
        } catch(Exception x) { status.Text=x.Message; RefreshButtons(); }
    }
    async Task Run(bool removing) {
        if(busy) return;
        try {
            string game=Selected(); var engine=new Installer(package,game,Discovery.Saves());
            if(!removing || File.Exists(Path.Combine(engine.Data,"estado.json"))) {
                try { if(removing) engine.ProbeDirectory(); else engine.ProbeWrite(); }
                catch(UnauthorizedAccessException) {
                    try {
                        Process.Start(new ProcessStartInfo(Application.ExecutablePath,"--game \""+game+"\" "+(removing?"--start-undo":"--start-install")) { UseShellExecute=true,Verb="runas" }); Close();
                    } catch(System.ComponentModel.Win32Exception) { status.Text="O Windows não concedeu permissão. Nenhum arquivo foi alterado."; }
                    return;
                }
            }
            busy=true; progress.Visible=true; progress.Style=ProgressBarStyle.Marquee; RefreshButtons();
            DateTime last=DateTime.MinValue;
            Action<string> report=text=>{if((DateTime.UtcNow-last).TotalMilliseconds<90) return;last=DateTime.UtcNow; BeginInvoke(new Action(()=>status.Text=text));};
            string result=await Task.Run(()=>removing?engine.Undo(report):engine.Install(report)); installedSuccessfully=!removing; status.Text=result;
        } catch(Exception x) { status.Text=x is UnauthorizedAccessException ? "Sem permissão para acessar essa pasta. Abra este instalador como administrador pelo menu do Windows." : x.Message; }
        finally { busy=false; progress.Visible=false; RefreshButtons(); }
    }
    public void Preview(string file) {
        paths.Items.Add(@"D:\Jogos\Cyberpunk 2077"); paths.SelectedIndex=0; status.Text="Pasta encontrada. Confira acima e clique em Instalar mods.";
        install.Enabled=undo.Enabled=true; play.Enabled=false; ShowInTaskbar=false; StartPosition=FormStartPosition.Manual; Location=new Point(-30000,-30000); Show(); Application.DoEvents(); PerformLayout();
        using(var bmp=new Bitmap(Width,Height)) { DrawToBitmap(bmp,new Rectangle(0,0,Width,Height)); bmp.Save(file,System.Drawing.Imaging.ImageFormat.Png); }
        Close();
    }
}
static class Program {
    [STAThread] static int Main(string[] args) {
        try {
            if(args.Length>0&&args[0]=="--self-test") { SelfTests.Run(args.Length>1?args[1]:Path.GetTempPath()); return 0; }
            if(args.Length>0&&args[0]=="--verify-package") {
                var p=Package.Load(AppDomain.CurrentDomain.BaseDirectory,PackageIdentity.ManifestHash,PackageIdentity.PayloadHash); p.Extract(null,s=>{});
                File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"verificacao-pacote.txt"),"OK: "+p.Manifest.Files.Count+" arquivos conferidos por SHA-256. "+DateTime.UtcNow.ToString("o")); return 0;
            }
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            if(args.Length>1&&args[0]=="--preview") { using(var f=new InstallerWindow(true)) f.Preview(args[1]); return 0; }
            Application.Run(new InstallerWindow()); return 0;
        } catch(Exception ex) {
            if(args.Length>0) { File.WriteAllText(Path.Combine(Path.GetTempPath(),"CoopMods-erro-testes.txt"),ex.ToString()); return 1; }
            MessageBox.Show(ex.Message,"Não foi possível iniciar",MessageBoxButtons.OK,MessageBoxIcon.Error); return 1;
        }
    }
}
