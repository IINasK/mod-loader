using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed partial class FixedInstaller {
    const string ExtraResetOperation = "reset-extras";
    const string RedmodRoot = "mods";
    const int ExtraResetLimit = 2000;
    static readonly string[] ExtraFileRoots = {"archive/pc/mod", "r6/scripts", "r6/tweaks"};
    static readonly string[] ExtraBundleRoots = {"red4ext/plugins", "bin/x64/plugins/cyber_engine_tweaks/mods", RedmodRoot};

    static bool InFolder(string path, string folder) {
        return path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase);
    }

    static bool ExtraFilePath(string path) {
        var extension = Path.GetExtension(path).ToLowerInvariant();
        if (InFolder(path, ExtraFileRoots[0])) return extension == ".archive" || extension == ".xl";
        if (InFolder(path, ExtraFileRoots[1])) return extension == ".reds";
        if (InFolder(path, ExtraFileRoots[2])) return extension == ".yaml" || extension == ".yml" || extension == ".tweak";
        if (InFolder(path, ExtraBundleRoots[0]) && path.Substring(ExtraBundleRoots[0].Length+1).IndexOf('/') < 0)
            return extension == ".dll";
        return ExtraBundleRoots.Any(root => InFolder(path, root) && path.Substring(root.Length+1).Contains("/"));
    }

    string RelativeGamePath(string path) {
        return path.Substring(game.Length+1).Replace('\\','/');
    }

    IEnumerable<string> ExtraTreeFiles(string directory) {
        var pending = new Stack<string>();
        pending.Push(directory);
        int visited = 0;
        while (pending.Count > 0) {
            string current = pending.Pop();
            Safe.NoLinks(current);
            foreach (string path in Directory.EnumerateFileSystemEntries(current)) {
                if (++visited > 20000) throw new IOException("Há arquivos demais para conferir nesta pasta.");
                Safe.NoLinks(path);
                if (Directory.Exists(path)) pending.Push(path);
                else yield return RelativeGamePath(path);
            }
        }
    }

    List<string> FindExtraPaths() {
        string version = FixedData.GameVersion(game);
        if (!Installer.VersionSupported(version) || FixedData.GameBuild(game) != catalog.GameFileVersion)
            throw new IOException("A limpeza precisa da mesma versão do jogo indicada no catálogo.");
        var known = new HashSet<string>(catalog.Packages.SelectMany(NightCityDensity.KnownFiles).Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();
        foreach (string root in ExtraFileRoots) {
            string directory = Safe.In(game, root);
            if (Directory.Exists(directory)) paths.AddRange(ExtraTreeFiles(directory).Where(ExtraFilePath));
        }
        foreach (string root in ExtraBundleRoots) {
            string directory = Safe.In(game, root);
            if (!Directory.Exists(directory)) continue;
            if (root == ExtraBundleRoots[0])
                paths.AddRange(Directory.EnumerateFiles(directory, "*.dll").Select(RelativeGamePath));
            foreach (string bundle in Directory.EnumerateDirectories(directory)) {
                string relative = RelativeGamePath(bundle);
                Safe.NoLinks(bundle);
                if (known.Any(path => InFolder(path, relative))) continue;
                if (root == RedmodRoot && !File.Exists(Safe.In(game, relative + "/info.json"))) continue;
                paths.AddRange(ExtraTreeFiles(bundle));
            }
        }
        paths = paths.Where(path => !known.Contains(path) && ExtraFilePath(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
        if (paths.Count > ExtraResetLimit) throw new IOException("A lista de arquivos fora do catálogo excede o limite de limpeza.");
        return paths;
    }

    internal List<OwnedChange> PlanExtraReset() {
        return FindExtraPaths().Select(path => new OwnedChange { Path=path, Before=Safe.Hash(Safe.In(game,path)) }).ToList();
    }

    internal string ApplyExtraReset(List<OwnedChange> approved, Action<string> report) {
        if (approved == null || approved.Count == 0) return "Nenhum mod fora da lista encontrado.";
        Closed();
        Directory.CreateDirectory(Data);
        using (var mutex = new FileStream(Safe.In(Data,"operacao.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)) {
            EnsureNoIncomplete();
            var current = PlanExtraReset();
            if (!current.Select(f => f.Path+"|"+f.Before).SequenceEqual(approved.Select(f => f.Path+"|"+f.Before), StringComparer.OrdinalIgnoreCase))
                throw new IOException("Os arquivos mudaram após a conferência. Abra a limpeza novamente para revisar a lista atual.");
            var receipt = new OnlineReceipt {
                Id=Guid.NewGuid().ToString("N"), CatalogId=catalog.Id, CreatedUtc=DateTime.UtcNow.ToString("o"),
                Status="desativando", Operation=ExtraResetOperation, BackupLayout="flat-v1"
            };
            string ledger = Safe.In(Data, "limpezas/" + receipt.Id + ".json");
            Save(ledger, receipt);
            try {
                foreach (var change in current) {
                    Closed();
                    string source = Safe.In(game, change.Path);
                    if (Safe.Hash(source) != change.Before) throw new IOException("O arquivo mudou durante a limpeza: " + change.Path);
                    string backup = DisabledPath(receipt, change.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    receipt.Files.Add(change);
                    Save(ledger, receipt);
                    File.Move(source, backup);
                    change.Applied = true;
                    Save(ledger, receipt);
                    report("Guardado: " + change.Path);
                }
                receipt.Status="desativado";
                Save(ledger, receipt);
                return receipt.Files.Count + " arquivo(s) fora da lista guardado(s). Use Desfazer última operação para restaurar.";
            } catch {
                try { UndoDeactivation(receipt,ledger,report); } catch { }
                throw;
            }
        }
    }

    IEnumerable<string> AllReceiptPaths() {
        foreach (var path in Directory.GetFiles(Data, "*.json")) yield return path;
        string imports = Safe.In(Data, "importados");
        Safe.NoLinks(imports);
        if (Directory.Exists(imports))
            foreach (var path in Directory.GetFiles(imports, "*.json")) yield return path;
        string folder = Safe.In(Data, "limpezas");
        if (Directory.Exists(folder))
            foreach (var path in Directory.GetFiles(folder, "*.json")) yield return path;
    }

    void ValidateExtraResetReceipt(OnlineReceipt receipt, string path) {
        string folder = Safe.In(Data, "limpezas");
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!String.Equals(Path.GetDirectoryName(path), folder, StringComparison.OrdinalIgnoreCase)
            || receipt.BackupLayout != "flat-v1" || receipt.Mods.Count != 0 || receipt.Files.Count > ExtraResetLimit)
            throw new IOException("Registro de limpeza inválido.");
        foreach (var file in receipt.Files) {
            if (file == null || String.IsNullOrEmpty(file.Path) || !unique.Add(file.Path) || !ExtraFilePath(file.Path)
                || file.After != null || !FixedData.HashValid(file.Before))
                throw new IOException("Arquivo fora do escopo da limpeza.");
            Safe.In(game, file.Path);
        }
    }
}

public sealed partial class OnlineWindow {
    async Task CleanExtras() {
        if (busy || automaticFlow || pollingDownload || pollingFacial || densityDialogOpen) return;
        if (automaticQueue.Count > 0 || pendingDensityId != null ||
            (downloadWatch != null && downloadWatch.IsArmed) || (facialQueue != null && facialQueue.IsArmed)) {
            status.Text="Conclua ou cancele a fila atual antes de limpar mods.";
            return;
        }
        try {
            string root=GameRoot();
            List<OwnedChange> plan=null;
            if (!await Run(() => {
                plan=createRemovalInstaller(root).PlanExtraReset();
                return plan.Count==0 ? "Nenhum arquivo de mod fora da lista encontrado." : plan.Count+" arquivo(s) para revisar.";
            }) || plan.Count==0) return;
            if (!ConfirmExtraPlan(root,plan)) return;
            if (MessageBox.Show(this,
                "Confirmar a retirada de "+plan.Count+" arquivo(s) do jogo? Eles serão guardados para restauração.",
                "Confirmar limpeza de mods",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes) return;
            if (!String.Equals(GameRoot(),root,StringComparison.OrdinalIgnoreCase)) return;
            await Run(() => {
                var result=createRemovalInstaller(root).ApplyExtraReset(plan,Report);
                RefreshMods(catalog.Packages,root);
                return result;
            });
        } catch (Exception ex) { status.Text=Friendly(ex); }
    }

    bool ConfirmExtraPlan(string root,List<OwnedChange> plan) {
        using(var dialog=new Form { Text="Revisar mods fora da lista", Width=820, Height=490, StartPosition=FormStartPosition.CenterParent, MinimizeBox=false, MaximizeBox=false }) {
            var content=new TableLayoutPanel { Dock=DockStyle.Fill, Padding=new Padding(14), ColumnCount=1, RowCount=3 };
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var intro=new Label { AutoSize=true, MaximumSize=new Size(760,0), Text=root+"\r\n\r\nRevise os arquivos que vão sair do jogo. A limpeza abrange archives, scripts, tweaks e pastas de mods do CET, RED4ext e REDmod." };
            var paths=new TextBox { Dock=DockStyle.Fill, ReadOnly=true, Multiline=true, WordWrap=false, ScrollBars=ScrollBars.Both, Text=String.Join("\r\n",plan.Select(f=>f.Path)), AccessibleName="Arquivos que serão guardados" };
            var buttons=new FlowLayoutPanel { AutoSize=true, Dock=DockStyle.Fill, FlowDirection=FlowDirection.RightToLeft };
            var next=new Button { Text="Continuar", AutoSize=true, DialogResult=DialogResult.OK };
            var cancel=new Button { Text="Cancelar", AutoSize=true, DialogResult=DialogResult.Cancel };
            buttons.Controls.Add(next);
            buttons.Controls.Add(cancel);
            content.Controls.Add(intro,0,0);
            content.Controls.Add(paths,0,1);
            content.Controls.Add(buttons,0,2);
            dialog.Controls.Add(content);
            dialog.CancelButton=cancel;
            dialog.AcceptButton=cancel;
            return dialog.ShowDialog(this)==DialogResult.OK;
        }
    }
}
