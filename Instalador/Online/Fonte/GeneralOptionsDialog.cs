using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralOptionsDialog : GeneralDialog {
    readonly GeneralPicker theme = new GeneralPicker();
    readonly GeneralPicker language = new GeneralPicker();
    readonly TextBox folder = new TextBox();
    readonly Label error = new Label();

    internal string ThemeValue { get; private set; }
    internal string LanguageValue { get; private set; }
    internal string DownloadFolder { get; private set; }

    internal GeneralOptionsDialog(string theme, string language, string folder, Func<long> clearDownloads = null)
        : base(T("Opções", "Options")) {
        ClientSize = new Size(620, clearDownloads == null ? 340 : 420);
        Font = GeneralStyle.Font(12);
        AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(24, 16, 24, 20), Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
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

        var downloads = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 2,
            Margin = new Padding(0, 18, 0, 0)
        };
        downloads.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        downloads.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        downloads.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        downloads.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        var folderLabel = FieldLabel(T("Procurar downloads em", "Look for downloads in"));
        downloads.Controls.Add(folderLabel, 0, 0);
        downloads.SetColumnSpan(folderLabel, 2);
        var field = new GeneralSurface {
            Dock = DockStyle.Fill, Padding = new Padding(14, 10, 14, 0),
            Margin = new Padding(0, 0, 10, 0), MinimumSize = new Size(100, 44)
        };
        this.folder.Text = folder ?? "";
        this.folder.ReadOnly = true;
        this.folder.Dock = DockStyle.Fill;
        this.folder.AccessibleName = T("Pasta de downloads", "Download folder");
        field.Controls.Add(this.folder);
        downloads.Controls.Add(field, 0, 1);
        var browse = GeneralStyle.Button(T("Escolher pasta", "Choose folder"), Browse);
        browse.Margin = Padding.Empty;
        browse.Dock = DockStyle.Fill;
        browse.MinimumSize = new Size(140, 44);
        downloads.Controls.Add(browse, 1, 1);
        layout.Controls.Add(downloads, 0, 1);
        if(clearDownloads!=null){
            var cachePanel=new TableLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,ColumnCount=2,Margin=new Padding(0,12,0,0)};
            cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));cachePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            cachePanel.Controls.Add(new Label{Text=T("Cópias antigas do cache podem ser removidas. Os downloads originais e os mods instalados serão preservados.","Old cache copies can be removed. Original downloads and installed mods will be preserved."),Dock=DockStyle.Fill,AutoSize=true,MaximumSize=new Size(330,0)},0,0);
            var clean=GeneralStyle.Button(T("Limpar cópias antigas","Clear old copies"),()=>{
                if(GeneralDialog.Message(this,T("Remover os downloads guardados pelo app? Reinstalar poderá exigir baixar novamente. Seus mods instalados, saves e arquivos em Downloads serão preservados.","Remove the app's saved downloads? Reinstalling may require downloading again. Installed mods, saves and files in Downloads will be preserved."),T("Limpar cópias antigas","Clear old copies"),MessageBoxButtons.OKCancel)!=DialogResult.OK)return;
                try{long bytes=clearDownloads();error.Text=T("Liberado: ","Freed: ")+GeneralStorageSize.Format(bytes);}catch(Exception ex){error.Text=ex.Message;}
            });clean.Margin=Padding.Empty;cachePanel.Controls.Add(clean,1,0);layout.Controls.Add(cachePanel,0,2);
        }

        error.Dock = DockStyle.Fill;
        error.Margin = new Padding(0, 6, 0, 0);
        error.AutoEllipsis = true;
        error.AccessibleName = T("Aviso", "Notice");
        layout.Controls.Add(error, 0, 3);

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
        layout.Controls.Add(commands, 0, 4);
        AcceptButton = save;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        this.folder.BorderStyle = BorderStyle.None;
        error.ForeColor = GeneralStyle.Danger;
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }

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

    void Browse() {
        using (var dialog = new FolderBrowserDialog {
            Description = T("Escolha onde o navegador salva os downloads.", "Choose where your browser saves downloads."),
            SelectedPath = folder.Text
        }) {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            try {
                folder.Text = new OnlineDownloadWatch(dialog.SelectedPath).Folder;
                error.Text = "";
            } catch (Exception ex) { error.Text = ex.Message; }
        }
    }

    void Save() {
        try {
            string selectedFolder = new OnlineDownloadWatch(folder.Text).Folder;
            ThemeValue = theme.SelectedIndex == 1 ? "dark" : theme.SelectedIndex == 2 ? "light-red" : theme.SelectedIndex == 3 ? "black" : "light";
            LanguageValue = language.SelectedIndex == 1 ? "en" : "pt";
            DownloadFolder = selectedFolder;
            DialogResult = DialogResult.OK;
        } catch (Exception ex) { error.Text = ex.Message; }
    }
}

