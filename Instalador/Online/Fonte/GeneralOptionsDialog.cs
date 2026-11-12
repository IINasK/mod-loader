using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralOptionsDialog : GeneralDialog {
    readonly GeneralPicker theme = new GeneralPicker();
    readonly GeneralPicker language = new GeneralPicker();
    readonly TextBox folder = new TextBox(), destination=new TextBox();
    readonly bool projectOptions;
    readonly Label error = new Label();
    readonly SwitchPerformanceField performance;
    internal SwitchPerformance SwitchSettings {get{return performance.Settings;}}

    internal string ThemeValue { get; private set; }
    internal string LanguageValue { get; private set; }
    internal string DownloadFolder { get; private set; }
    internal string Destination { get; private set; }

    internal GeneralOptionsDialog(string theme, string language, string folder, Func<DownloadCache.CleanupPlan> previewCleanup = null,string installationFolder=null,Func<string,FixedInstaller.SaveBackupCleanup> previewSaveBackups=null,SwitchPerformance switchSettings=null)
        : base(T("Opções", "Options")) {
        projectOptions=installationFolder!=null;
        bool showSaveCleanup=previewSaveBackups!=null&&!String.IsNullOrWhiteSpace(installationFolder)&&
            Directory.Exists(Path.Combine(installationFolder,".CoopModsOnline","backups"));
        ClientSize = new Size(620, (previewCleanup == null ? 392 : 472)+(projectOptions?86:0)+(showSaveCleanup?60:0)+92);
        Font = GeneralStyle.Font(12);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9,
            Padding = new Padding(24, 16, 24, 20), Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Content.Controls.Add(layout);

        var choices = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty
        };
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        choices.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        choices.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        choices.Controls.Add(FieldLabel(T("Tema", "Theme")), 0, 0);
        choices.Controls.Add(FieldLabel(T("Idioma", "Language")), 1, 0);
        Configure(this.theme, T("Tema", "Theme"), new Padding(0, 0, 10, 0));
        this.theme.Items.AddRange(new object[] { T("Claro — violeta", "Light — violet"), T("Escuro", "Dark"), T("Claro — vermelho", "Light — red"), T("Preto e cinza", "Black and gray") });
        this.theme.SelectedIndex = theme == "dark" ? 1 : theme == "light-red" ? 2 : theme == "black" ? 3 : 0;
        Configure(this.language, T("Idioma", "Language"), new Padding(10, 0, 0, 0));
        this.language.Items.AddRange(new object[] { "Português", "English" });
        this.language.SelectedIndex = language == "en" ? 1 : 0;
        choices.Controls.Add(this.theme, 0, 1);
        choices.Controls.Add(this.language, 1, 1);
        choices.GetControlFromPosition(1, 0).Margin = new Padding(10, 0, 0, 6);
        layout.Controls.Add(choices, 0, 0);

        this.folder.Text=folder??"";
        this.folder.ReadOnly=true;
        this.folder.AccessibleName=T("Pasta de downloads","Download folder");
        layout.Controls.Add(FolderRow(T("Procurar downloads em","Look for downloads in"),this.folder,()=>Browse(this.folder,false)),0,1);
        var browser = GeneralStyle.Button(T("Conectar navegador…", "Connect browser…"), ConnectBrowser);
        browser.Margin = new Padding(0, 10, 0, 0);
        var support = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Margin = Padding.Empty };
        var report = GeneralStyle.Button(T("Reportar erro", "Report a problem"), ShowSessionReport);
        report.Name = "ReportSessionError";
        report.Margin = new Padding(10, 10, 0, 0);
        support.Controls.Add(browser);
        support.Controls.Add(report);
        layout.Controls.Add(support, 0, 2);
        if(projectOptions){
            destination.Text=installationFolder;
            destination.Name="OptionsInstallationFolder";
            destination.AccessibleName=T("Pasta de instalação","Installation folder");
            layout.Controls.Add(FolderRow(destination.AccessibleName,destination,()=>Browse(destination,true)),0,3);
        }
        if(previewCleanup!=null){
            var cachePanel=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=new Padding(0,12,0,0)};
            cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cachePanel.Controls.Add(new Label{Text=T("Cópias antigas do cache podem ser removidas. Os downloads originais e os mods instalados serão preservados.","Old cache copies can be removed. Original downloads and installed mods will be preserved."),Dock=DockStyle.Fill,AutoSize=true,MaximumSize=new Size(330,0)},0,0);
            var clean=GeneralStyle.Button(T("Limpar cópias antigas","Clear old copies"),()=>ClearOldCopies(previewCleanup));
            clean.Margin=Padding.Empty;cachePanel.Controls.Add(clean,1,0);layout.Controls.Add(cachePanel,0,4);
        }

        if(showSaveCleanup){
            Button cleanSaves=null;
            cleanSaves=GeneralStyle.Button(T("Limpar backups de saves","Clear save backups"),
                async()=>await ClearSaveBackups(cleanSaves,()=>previewSaveBackups(installationFolder)));
            cleanSaves.Name="CleanOldSaveBackups";
            cleanSaves.Margin=new Padding(0,10,0,0);
            cleanSaves.Anchor=AnchorStyles.Right;
            layout.Controls.Add(cleanSaves,0,5);
        }

        performance=new SwitchPerformanceField(switchSettings??new SwitchPerformance());
        performance.Margin=new Padding(0,8,0,0);
        layout.Controls.Add(performance,0,6);
        error.Dock = DockStyle.Fill;
        error.Margin = new Padding(0, 6, 0, 0);
        error.AutoEllipsis = true;
        error.AccessibleName = T("Aviso", "Notice");
        layout.Controls.Add(error, 0, 7);

        var commands = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, WrapContents = false,
            FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 12, 0, 0)
        };
        var save = GeneralStyle.Button(T("Salvar", "Save"), Save, true);
        save.Margin = Padding.Empty;
        var cancel = GeneralStyle.Button(T("Cancelar", "Cancel"), () => DialogResult = DialogResult.Cancel);
        cancel.Margin = new Padding(0, 0, 10, 0);
        commands.Controls.Add(save);
        commands.Controls.Add(cancel);
        layout.Controls.Add(commands, 0, 8);
        AcceptButton = save;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        this.folder.BorderStyle = BorderStyle.None;
        error.ForeColor = GeneralStyle.Danger;
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }

    void ShowSessionReport() {
        using(var dialog=CreateSessionReport())dialog.ShowDialog(this);
    }

    internal static GeneralDialog CreateSessionReport() {
        bool english = GeneralStyle.Language == "en";
        var dialog = new GeneralDialog(T("Reportar erro", "Report a problem")) { ClientSize = new Size(720, 520) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(24, 10, 24, 20) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var feedback = new Label { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8),
            Text = T("Copie o relatório e cole na conversa com quem vai te ajudar.", "Copy the report and paste it into your conversation with support.") };
        var preview = new TextBox { Name = "SessionReportPreview", Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
            AccessibleName = T("Relatório da sessão", "Session report"), Text = OnlineDiagnostics.Report(english) };
        var surface = new GeneralSurface { Dock = DockStyle.Fill, Padding = new Padding(12) };
        surface.Controls.Add(preview);
        var actions = GeneralTableDialogLayout.Commands();
        var copy = GeneralStyle.Button(T("Copiar relatório", "Copy report"), () => {
            try {
                Clipboard.SetText(preview.Text);
                feedback.Text = T("Relatório copiado.", "Report copied.");
            } catch (ExternalException) {
                feedback.Text = T("Não foi possível copiar. Tente novamente ou selecione o texto.", "Couldn't copy. Try again or select the text.");
            }
        }, true);
        copy.Name = "CopySessionReport";
        var close = GeneralStyle.Button(T("Fechar", "Close"), () => dialog.Close());
        actions.Controls.Add(copy);
        actions.Controls.Add(close);
        layout.Controls.Add(feedback, 0, 0);
        layout.Controls.Add(surface, 0, 1);
        layout.Controls.Add(actions, 0, 2);
        dialog.Content.Controls.Add(layout);
        GeneralStyle.Apply(dialog, GeneralStyle.Dark);
        return dialog;
    }

    void ConnectBrowser() {
        try {
            string folder = BrowserDownloadBridge.Prepare(Application.ExecutablePath);
            using (Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true })) { }
            string steps = T(
                "Abra brave://extensions, chrome://extensions ou edge://extensions no seu navegador. Ative o modo do desenvolvedor e escolha Carregar sem compactação. Selecione a pasta aberta no Explorador.",
                "Open brave://extensions, chrome://extensions or edge://extensions in your browser. Turn on developer mode, choose Load unpacked and select the folder opened in File Explorer.");
            GeneralDialog.Message(this, steps + "\n\n" + folder, T("Conectar navegador", "Connect browser"));
        } catch (Exception ex) { ShowNotice(ex.Message, true); }
    }

    async Task ClearSaveBackups(Button button,Func<FixedInstaller.SaveBackupCleanup> preview){
        button.Enabled=false;
        ShowNotice(T("Conferindo os backups…","Checking backups…"));
        try{
            var plan=await Task.Run(preview);
            if(IsDisposed)return;
            if(plan.Count==0){ShowNotice(T("Nenhum backup antigo identificado.","No old backups identified."));return;}
            string summary=String.Format(T(
                "Excluir {0:N0} backups antigos de saves ({1})? A cópia mais recente, os saves originais e os backups dos mods serão preservados.",
                "Delete {0:N0} old save backups ({1})? The latest copy, original saves and mod backups will be kept."),
                plan.Count,GeneralStorageSize.Format(plan.Bytes));
            if(GeneralDialog.Message(this,summary,button.Text,MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)!=DialogResult.Yes){
                ShowNotice("");return;
            }
            var result=await Task.Run(()=>{
                int skipped;
                long bytes=plan.Apply(out skipped);
                return Tuple.Create(bytes,skipped);
            });
            if(IsDisposed)return;
            string notice=T("Espaço liberado: ","Space freed: ")+GeneralStorageSize.Format(result.Item1);
            if(result.Item2>0)notice+=" · "+String.Format(T("Backups não removidos: {0:N0}","Backups skipped: {0:N0}"),result.Item2);
            ShowNotice(notice);
        }catch(Exception ex){if(!IsDisposed)ShowNotice(ex.Message,true);}
        finally{if(!IsDisposed)button.Enabled=true;}
    }

    void ClearOldCopies(Func<DownloadCache.CleanupPlan> preview){
        try{
            var plan=preview();
            if(plan.Count==0){ShowNotice(T("Nenhuma cópia antiga encontrada.","No old copies found."));return;}
            string summary=String.Format(T(
                "Cópias antigas encontradas: {0:N0} ({1}). Reinstalar poderá exigir baixar novamente. Mods instalados, saves, arquivos em Downloads e transferências P2P serão preservados.",
                "Old copies found: {0:N0} ({1}). Reinstalling may require downloading again. Installed mods, saves, files in Downloads and P2P transfers will be preserved."),
                plan.Count,GeneralStorageSize.Format(plan.Bytes));
            if(GeneralDialog.Message(this,summary,T("Limpar cópias antigas","Clear old copies"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
            int skipped;
            long bytes=plan.Apply(out skipped);
            string result=T("Espaço liberado: ","Space freed: ")+GeneralStorageSize.Format(bytes);
            if(skipped>0)result+=" · "+String.Format(T("Não removidos: {0:N0}","Skipped: {0:N0}"),skipped);
            ShowNotice(result);
        }catch(Exception ex){ShowNotice(ex.Message,true);}
    }

    void ShowNotice(string text,bool failed=false){
        if(failed)OnlineDiagnostics.Record("options.failed",null,"Falha ao aplicar uma opção.",new{Actual=text});
        error.ForeColor=failed?GeneralStyle.Danger:GeneralStyle.Foreground;
        error.Text=text;
    }

    static Label FieldLabel(string text) {
        return new Label { Text = text, AutoSize = true, Margin = new Padding(0, 0, 0, 6), UseMnemonic = false };
    }

    static void Configure(GeneralPicker picker, string label, Padding margin) {
        picker.Dock = DockStyle.Fill;
        picker.DropDownStyle = ComboBoxStyle.DropDownList;
        picker.Font = GeneralStyle.Font(12);
        picker.Margin = margin;
        picker.MinimumSize = new Size(120, 44);
        picker.AccessibleName = label;
    }

    internal static TableLayoutPanel FolderRow(string label,TextBox text,Action browseFolder){
        text.Dock=DockStyle.Fill;text.BorderStyle=BorderStyle.None;
        var row = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0, 18, 0, 0)
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var folderLabel=FieldLabel(label);
        row.Controls.Add(folderLabel, 0, 0);
        row.SetColumnSpan(folderLabel, 2);
        var field = new GeneralSurface {
            Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 0),
            Margin = new Padding(0, 0, 10, 0), MinimumSize = new Size(100, 44)
        };
        field.Controls.Add(text);
        row.Controls.Add(field,0,1);
        var browse = GeneralStyle.Button(T("Escolher pasta", "Choose folder"), browseFolder);
        browse.Margin = Padding.Empty;
        browse.Dock = DockStyle.Fill;
        browse.MinimumSize = new Size(140, 44);
        row.Controls.Add(browse, 1, 1);
        return row;
    }

    void Browse(TextBox field,bool install) {
        try {
            string selected=WindowsFolderPicker.Choose(this,
                install?T("Pasta de instalação","Installation folder"):T("Pasta de downloads","Download folder"),field.Text);
            if(selected==null)return;
            field.Text=install?WindowsFolderPicker.InstallationPath(selected):new OnlineDownloadWatch(selected).Folder;
            error.Text="";
        } catch(Exception ex) { ShowNotice(ex.Message,true); }
    }

    void Save() {
        try {
            string selectedFolder = new OnlineDownloadWatch(folder.Text).Folder;
            Destination=projectOptions?WindowsFolderPicker.InstallationPath(destination.Text):null;
            ThemeValue = theme.SelectedIndex == 1 ? "dark" : theme.SelectedIndex == 2 ? "light-red" : theme.SelectedIndex == 3 ? "black" : "light";
            LanguageValue = language.SelectedIndex == 1 ? "en" : "pt";
            DownloadFolder = selectedFolder;
            DialogResult = DialogResult.OK;
        } catch (Exception ex) { ShowNotice(ex.Message,true); }
    }
}

public sealed class SwitchPerformance {
    public int CpuPercent=50;
    public bool AskBeforeSwitch=true;

    internal void Normalize() {
        if(CpuPercent!=25&&CpuPercent!=50&&CpuPercent!=75&&CpuPercent!=100)CpuPercent=50;
    }
    internal int Workers(int logicalProcessors) {
        return Math.Max(1,logicalProcessors*CpuPercent/100);
    }
}

internal sealed class SwitchPerformanceField : TableLayoutPanel {
    readonly GeneralPicker limit=new GeneralPicker();
    readonly CheckBox remember=new CheckBox();

    internal SwitchPerformanceField(SwitchPerformance settings) {
        Dock=DockStyle.Fill;AutoSize=true;ColumnCount=1;RowCount=3;
        ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        RowStyles.Add(new RowStyle(SizeType.Absolute,42));
        RowStyles.Add(new RowStyle(SizeType.AutoSize));
        string label=GeneralStyle.T("CPU na conferência da troca","CPU for switch verification");
        Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,0,0,5)},0,0);
        limit.Name="SwitchCpuLimit";limit.AccessibleName=label;
        limit.Dock=DockStyle.Fill;limit.Margin=Padding.Empty;
        limit.Items.AddRange(new object[]{"25%","50%","75%","100%"});
        settings.Normalize();
        limit.SelectedIndex=settings.CpuPercent/25-1;
        Controls.Add(limit,0,1);
        remember.Name="RememberSwitchPerformance";
        remember.Text=GeneralStyle.T("Não perguntar novamente","Don't ask again");
        remember.Checked=!settings.AskBeforeSwitch;remember.AutoSize=true;
        remember.Margin=new Padding(0,7,0,0);
        Controls.Add(remember,0,2);
    }

    internal SwitchPerformance Settings {
        get{return new SwitchPerformance{CpuPercent=25*(limit.SelectedIndex+1),AskBeforeSwitch=!remember.Checked};}
    }
}

internal sealed class SwitchPerformanceDialog : GeneralDialog {
    readonly SwitchPerformanceField performance;
    internal SwitchPerformance Settings {get{return performance.Settings;}}

    internal SwitchPerformanceDialog(SwitchPerformance settings) : base(GeneralStyle.T("Desempenho da troca","Switch performance")) {
        ClientSize=new Size(500,280);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(24,12,24,18)};
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{AutoSize=true,MaximumSize=new Size(440,0),Margin=new Padding(0,0,0,12),
            Text=GeneralStyle.T("Esta troca pode demorar. Escolha a parcela dos núcleos lógicos que a conferência pode usar.",
                "This switch may take a while. Choose the share of logical processors that verification can use.")},0,0);
        performance=new SwitchPerformanceField(settings){Margin=Padding.Empty,Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right};
        layout.Controls.Add(performance,0,1);
        var commands=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};
        var apply=GeneralStyle.Button(GeneralStyle.T("Aplicar","Apply"),()=>Complete(DialogResult.OK),true);
        var cancel=GeneralStyle.Button(GeneralStyle.T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));
        commands.Controls.Add(apply);commands.Controls.Add(cancel);
        layout.Controls.Add(commands,0,2);Content.Controls.Add(layout);
        AcceptButton=apply;CancelButton=cancel;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
}

internal static class WindowsFolderPicker {
    const uint PickFolders=0x20, ForceFileSystem=0x40, PathMustExist=0x800;
    const uint FileSystemPath=0x80058000;
    const int Cancelled=unchecked((int)0x800704C7);

    internal static string InstallationPath(string input) {
        string path=(input??"").Trim();
        if(path.Length==0)return "";
        if(path.Length>=2&&path[0]=='"'&&path[path.Length-1]=='"')path=path.Substring(1,path.Length-2);
        path=path.Replace('/','\\');
        bool absoluteDrive=path.Length>=3&&Char.IsLetter(path[0])&&path[1]==':'&&path[2]=='\\';
        bool networkShare=path.StartsWith(@"\\",StringComparison.Ordinal)
            &&!path.StartsWith(@"\\?\",StringComparison.Ordinal)&&!path.StartsWith(@"\\.\",StringComparison.Ordinal);
        if((!absoluteDrive&&!networkShare)||!Directory.Exists(path))throw new IOException(GeneralStyle.T(
            "Informe o caminho completo de uma pasta existente.","Enter the full path of an existing folder."));
        path=Path.GetFullPath(path);
        Safe.NoLinks(path);
        return path;
    }

    internal static string Choose(IWin32Window owner,string title,string initial=null) {
        var dialog=(IFileDialog)new FileOpenDialog();
        IShellItem start=null,selected=null;
        try {
            uint options;
            dialog.GetOptions(out options);
            dialog.SetOptions(options|PickFolders|ForceFileSystem|PathMustExist);
            dialog.SetTitle(title);
            if(Directory.Exists(initial)){
                Guid type=typeof(IShellItem).GUID;
                if(SHCreateItemFromParsingName(initial,IntPtr.Zero,ref type,out start)>=0)
                    dialog.SetFolder(start);
            }
            int result=dialog.Show(owner.Handle);
            if(result==Cancelled)return null;
            Marshal.ThrowExceptionForHR(result);
            dialog.GetResult(out selected);
            string path;
            selected.GetDisplayName(FileSystemPath,out path);
            return path;
        } finally {
            if(selected!=null)Marshal.ReleaseComObject(selected);
            if(start!=null)Marshal.ReleaseComObject(start);
            Marshal.ReleaseComObject(dialog);
        }
    }

    [DllImport("shell32.dll",CharSet=CharSet.Unicode,ExactSpelling=true)]
    static extern int SHCreateItemFromParsingName(string path,IntPtr bindContext,ref Guid type,out IShellItem item);

    [ComImport,Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")]
    class FileOpenDialog {}

    [ComImport,Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IFileDialog {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count,IntPtr filters);
        void SetFileTypeIndex(uint index);
        void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events,out uint cookie);
        void Unadvise(uint cookie);
        void SetOptions(uint options);
        void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder);
        void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder);
        void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem item);
    }

    [ComImport,Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItem {
        void BindToHandler(IntPtr bindContext,ref Guid handler,ref Guid type,out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint format,[MarshalAs(UnmanagedType.LPWStr)] out string name);
    }
}
