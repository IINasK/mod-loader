using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class GeneralOptionsDialog : GeneralDialog {
    readonly GeneralPicker theme = new GeneralPicker();
    readonly GeneralPicker language = new GeneralPicker();
    readonly TextBox folder = new TextBox(), destination=new TextBox();
    readonly bool projectOptions;
    readonly Label error = new Label();

    internal string ThemeValue { get; private set; }
    internal string LanguageValue { get; private set; }
    internal string DownloadFolder { get; private set; }
    internal string Destination { get; private set; }

    internal GeneralOptionsDialog(string theme, string language, string folder, Func<DownloadCache.CleanupPlan> previewCleanup = null,string installationFolder=null)
        : base(T("Opções", "Options")) {
        projectOptions=installationFolder!=null;
        ClientSize = new Size(620, (previewCleanup == null ? 340 : 420)+(projectOptions?86:0));
        Font = GeneralStyle.Font(12);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(24, 16, 24, 20), Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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
        if(projectOptions){
            destination.Text=installationFolder;
            destination.Name="OptionsInstallationFolder";
            destination.AccessibleName=T("Pasta de instalação","Installation folder");
            layout.Controls.Add(FolderRow(destination.AccessibleName,destination,()=>Browse(destination,true)),0,2);
        }
        if(previewCleanup!=null){
            var cachePanel=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=new Padding(0,12,0,0)};
            cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cachePanel.Controls.Add(new Label{Text=T("Cópias antigas do cache podem ser removidas. Os downloads originais e os mods instalados serão preservados.","Old cache copies can be removed. Original downloads and installed mods will be preserved."),Dock=DockStyle.Fill,AutoSize=true,MaximumSize=new Size(330,0)},0,0);
            var clean=GeneralStyle.Button(T("Limpar cópias antigas","Clear old copies"),()=>ClearOldCopies(previewCleanup));
            clean.Margin=Padding.Empty;cachePanel.Controls.Add(clean,1,0);layout.Controls.Add(cachePanel,0,3);
        }

        error.Dock = DockStyle.Fill;
        error.Margin = new Padding(0, 6, 0, 0);
        error.AutoEllipsis = true;
        error.AccessibleName = T("Aviso", "Notice");
        layout.Controls.Add(error, 0, 4);

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
        layout.Controls.Add(commands, 0, 5);
        AcceptButton = save;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        this.folder.BorderStyle = BorderStyle.None;
        error.ForeColor = GeneralStyle.Danger;
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }

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

    static Control FolderRow(string label,TextBox text,Action browseFolder){
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
