using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

public static class GeneralModInstallerTests {
    public static int Run(string root) {
        string folder = Path.Combine(root, "geral-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        string target = Path.Combine(folder, "jogo");
        string secondTarget = Path.Combine(folder, "outro-jogo");
        string state = Path.Combine(folder, "estado");
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(secondTarget);
        string original = Path.Combine(target, "preservar.txt");
        File.WriteAllText(original, "existente");
        string first = MakeZip(folder, "mod-1.zip", new Dictionary<string, string> {
            { "Data/Mods/um.txt", "primeiro" }, { "Data/Mods/compartilhado.txt", "comum" }, { "preservar.txt", "existente" }
        });
        string second = MakeZip(folder, "mod-2.zip", new Dictionary<string, string> {
            { "Data/Mods/dois.txt", "segundo" }, { "Data/Mods/compartilhado.txt", "comum" }
        });
        string conflict = MakeZip(folder, "mod-3.zip", new Dictionary<string, string> {
            { "nova-pasta/inexistente.txt", "novo" }, { "preservar.txt", "outro" }
        });
        string traversal = MakeZip(folder, "mod-4.zip", new Dictionary<string, string> {
            { "../fora.txt", "fora" }
        });
        var installer = new GeneralModInstaller(target, state);
        Check(installer.Status("mod-1") == GeneralModInstaller.Missing, "Um mod sem recibo não aparece como instalado.");
        installer.Install(first, "mod-1", null);
        installer = new GeneralModInstaller(target, state);
        string firstFile = Path.Combine(target, @"Data\Mods\um.txt");
        Check(installer.Status("mod-1") == GeneralModInstaller.Installed && File.ReadAllText(firstFile) == "primeiro", "Estrutura e recibo sobrevivem à reabertura.");
        Check(new GeneralModInstaller(secondTarget, state).Status("mod-1") == GeneralModInstaller.Missing, "Recibos pertencem apenas ao destino correspondente.");
        File.Delete(firstFile);
        Check(installer.Status("mod-1") == GeneralModInstaller.Partial, "Recibo detecta arquivo ausente.");
        installer.Install(first, "mod-1", null);
        Check(installer.Status("mod-1") == GeneralModInstaller.Installed, "Repetição do mesmo pacote repara arquivo ausente.");
        Throws(() => installer.Install(conflict, "mod-3", null));
        Check(File.ReadAllText(original) == "existente" && !Directory.Exists(Path.Combine(target, "nova-pasta")), "Conflito posterior bloqueia todas as alterações do pacote.");
        Throws(() => installer.Install(traversal, "mod-4", null));
        Check(!File.Exists(Path.Combine(folder, "fora.txt")), "Travessia de diretório recusada.");
        installer.Install(second, "mod-2", null);
        string shared = Path.Combine(target, @"Data\Mods\compartilhado.txt");
        File.WriteAllText(firstFile, "alterado");
        Check(installer.Status("mod-1") == GeneralModInstaller.Modified, "Detecta alterações por conteúdo.");
        installer.Remove("mod-1");
        Check(File.Exists(shared) && File.Exists(original) && File.Exists(firstFile), "Remoção preserva arquivos compartilhados, preexistentes e alterados.");
        installer.Remove("mod-2");
        Check(!File.Exists(shared) && !File.Exists(Path.Combine(target, @"Data\Mods\dois.txt")), "Última remoção limpa arquivos criados pelo gerenciador.");

        string rollbackState = Path.Combine(folder, "reversao");
        var rollback = new GeneralModInstaller(secondTarget, rollbackState);
        rollback.Install(first, "mod-1", null);
        string stateFile = Directory.GetFiles(rollbackState, "*.json", SearchOption.AllDirectories)[0];
        rollback.Remove("mod-1");
        Throws(() => rollback.Install(first, "mod-1", message => Directory.CreateDirectory(stateFile)));
        Check(Directory.GetFiles(secondTarget, "*", SearchOption.AllDirectories).Length == 0 &&
            Directory.GetDirectories(secondTarget, ".mod-loader-*").Length == 0, "Falha ao gravar o recibo reverte os arquivos copiados e limpa o estágio.");
        string package=MakeZip(folder,"routes.zip",new Dictionary<string,string>{
            {"src/one.txt","one"},{"src/sub/two.txt","two"}
        });
        var routes=GeneralInstallRoutes.Parse("src = %GAME%/chosen | src/sub/two.txt = %GAME%/alternate",target);
        string firstPath=Path.Combine(target,@"src\one.txt"), secondPath=Path.Combine(target,@"src\sub\two.txt");
        string movedFirst=Path.Combine(target,@"chosen\one.txt"), movedSecond=Path.Combine(target,@"alternate\two.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(movedFirst));
        Directory.CreateDirectory(Path.GetDirectoryName(movedSecond));
        installer.Install(package,"relocate",null);
        var defaults=new List<GeneralInstallRoute>();
        File.WriteAllText(movedSecond,"pre-existing");
        Throws(()=>installer.Relocate("relocate",defaults,routes,null));
        Check(File.Exists(firstPath)&&File.Exists(secondPath)&&!File.Exists(movedFirst)&&File.ReadAllText(movedSecond)=="pre-existing",
            "A conflicting destination preserves the installed package and the existing file.");
        File.Delete(movedSecond);
        Check(installer.Relocate("relocate",defaults,routes,null)&&File.ReadAllText(movedFirst)=="one"&&File.ReadAllText(movedSecond)=="two"&&
            !File.Exists(firstPath)&&!File.Exists(secondPath)&&installer.Status("relocate",package,Safe.Hash,routes)==GeneralModInstaller.Installed,
            "The installed files and their receipt follow folder and individual file rules.");
        Check(installer.Relocate("relocate",routes,defaults,null)&&File.Exists(firstPath)&&File.Exists(secondPath)&&!File.Exists(movedFirst)&&!File.Exists(movedSecond),
            "Reset restores the original archive paths.");
        installer.Remove("relocate");
        Check(!File.Exists(firstPath)&&!File.Exists(secondPath),"Uninstall uses the updated record.");
        string pinnedSource=Path.Combine(folder,"pinned.bin");
        File.WriteAllText(pinnedSource,"pinned");
        var asset=new FixedAsset { Id="pinned", Name="pinned.bin", Format="raw", Size=new FileInfo(pinnedSource).Length, Sha256=Safe.Hash(pinnedSource) };
        var pinned=new FixedMod { Id="pinned", Name="Pinned mod", Assets=new List<FixedAsset>{asset}, Files=new List<FixedFile>{
            new FixedFile { Path="src/pinned.txt", Entry="pinned.txt", Asset=asset.Id, Size=asset.Size, Sha256=asset.Sha256 }
        } };
        var download=new DownloadCache(Path.Combine(folder,"fixed-cache"));
        download.Import(pinnedSource,asset);
        var pinnedPaths=new Dictionary<string,string>{{"src/pinned.txt","chosen/pinned.txt"}};
        installer.InstallRoutedFixed("pinned",pinned,pinnedPaths,routes,download,message=>{});
        string installedPinned=Path.Combine(target,@"chosen\pinned.txt");
        Check(installer.Status("pinned")==GeneralModInstaller.Installed&&File.ReadAllText(installedPinned)=="pinned"&&
            !File.Exists(Path.Combine(target,@"src\pinned.txt")),"An audited archive installs directly to the chosen location.");
        installer.Remove("pinned");
        Check(!File.Exists(installedPinned),"The pinned package is removed through its routed receipt.");
        return 17;
    }

    static string MakeZip(string root, string name, Dictionary<string, string> entries) {
        string path = Path.Combine(root, name);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            foreach (var entry in entries)
                using (var writer = new StreamWriter(archive.CreateEntry(entry.Key).Open())) writer.Write(entry.Value);
        return path;
    }

    static void Throws(Action operation) {
        try { operation(); }
        catch (IOException) { return; }
        throw new Exception("O pacote inseguro passou pela validação.");
    }

    static void Check(bool valid, string message) {
        if (!valid) throw new Exception(message);
    }
}
