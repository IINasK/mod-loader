using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModEditor : GeneralDialog {
    readonly TextBox modName = new TextBox { Dock = DockStyle.Fill, MaxLength = 300 };
    readonly GeneralPicker category = new GeneralPicker { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 120 };
    readonly TextBox tags = new TextBox { Name = "TagInput", Dock = DockStyle.Fill, MaxLength = 60 };
    readonly FlowLayoutPanel tagChips = new FlowLayoutPanel {
        Name = "TagChips", Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = new Padding(3, 0, 3, 3), Visible = false
    };
    readonly List<string> selectedTags = new List<string>();
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    readonly CheckBox nsfw = new CheckBox { AutoSize = true };
    readonly CheckBox usePending = new CheckBox { AutoSize = true, Visible = false, Dock = DockStyle.Fill, UseMnemonic = false };
    readonly GeneralPicker variants = new GeneralPicker { Name = "DownloadVersion", Dock = DockStyle.Fill };
    readonly TextBox variantName = new TextBox { Dock = DockStyle.Fill, MaxLength = 300 };
    readonly TextBox sources = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 131104 };
    readonly Label validation = new Label { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false };
    readonly Label installNote;
    readonly Label sourceInfo=new Label{Name="SourceMetadata",AutoSize=true,Dock=DockStyle.Fill,UseMnemonic=false};
    readonly Timer sourceDelay=new Timer{Interval=650};
    readonly Func<string,Task<GeneralSourceInfo>> lookup;
    string lastAutoName;
    int sourceGeneration;
    readonly Button alternative, unlink;
    readonly Button save, install, removeVariant, setDefault, browse;
    readonly ToolTip editorTips = new ToolTip { UseAnimation = false, UseFading = false };
    internal GeneralModEntry LinkedAlternative { get; private set; }
    readonly FlowLayoutPanel variantActions;
    readonly List<GeneralModVariant> model;
    GeneralModVariant current;
    string principal;
    bool changing, attachment, returningFromList;
    protected override bool AnimateAppearance { get { return !returningFromList; } }
    internal GeneralModEntry Result { get; private set; }
    internal string SelectedVariant { get; private set; }
    internal bool InstallRequested { get; private set; }
    internal bool AttachRequested { get; private set; }

    internal GeneralModEditor(GeneralModEntry entry, IEnumerable<string> categories, string selected, bool allowInstall, string installInfo,Func<string,Task<GeneralSourceInfo>> lookup=null, IEnumerable<GeneralModEntry> alternatives=null) {
        this.lookup=lookup;
        model = entry.Variants.Select(v => new GeneralModVariant {
            Id = v.Id, Name = v.Name, Sources = v.Sources.ToList(), SourceInfo=v.SourceInfo==null?new Dictionary<string,GeneralSourceInfo>():v.SourceInfo.ToDictionary(pair=>pair.Key,pair=>pair.Value.SavedCopy()), CyberpunkId = v.CyberpunkId, CyberpunkVariant = v.CyberpunkVariant
        }).ToList();
        if (model.Count == 0) model.Add(new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Principal", "Default") });
        principal = model.Any(v => v.Id == entry.DefaultVariant) ? entry.DefaultVariant : model[0].Id;
        Text = T("Detalhes do mod", "Mod details");
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 760);
        MinimumSize = new Size(620, 440);
        var layout = new TableLayoutPanel {
            ColumnCount = 2,
            Padding = new Padding(20, 4, 20, 14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        var shell = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Margin = Padding.Empty };
        shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var scroll = new GeneralScrollView(layout, panelContent: true, reserveBarSpace: true, fitPanelContent: true) { Name = "ModEditorScroll" };
        shell.Controls.Add(scroll, 0, 0);
        Controls.Add(shell);

        modName.Text = entry.Name;
        category.Items.AddRange(categories.Cast<object>().ToArray());
        category.Text = entry.Category ?? "";
        description.Text = entry.Description ?? "";
        nsfw.Checked = entry.Nsfw;
        nsfw.Text = T("NSFW · conteúdo adulto", "NSFW · adult content");
        nsfw.Anchor = AnchorStyles.Left;
        nsfw.Margin = new Padding(12, 22, 3, 3);

        AddEditorRow(layout, EditorSection(T("Identificação", "Identification"), false));
        AddEditorRow(layout, EditorField(T("Nome do mod *", "Mod name *"), modName), EditorField(T("Categoria", "Category"), category));
        AddEditorRow(layout, EditorField(T("Tags (Enter para adicionar)", "Tags (Enter to add)"), tags), nsfw);
        AddEditorRow(layout, tagChips);
        tagChips.SizeChanged += (s, e) => {
            foreach (Control chip in tagChips.Controls) FitTagChip(chip);
        };
        tags.PreviewKeyDown += (s, e) => { if (e.KeyData == Keys.Enter) e.IsInputKey = true; };
        tags.KeyDown += (s, e) => {
            if (e.KeyData != Keys.Enter) return;
            e.SuppressKeyPress = true;
            string tag = tags.Text.Trim();
            if (tag.Length == 0 || selectedTags.Contains(tag, StringComparer.OrdinalIgnoreCase) || AddTag(tag))
                tags.Clear();
        };
        AddEditorRow(layout, EditorField(T("Descrição", "Description"), description, 72));

        AddEditorRow(layout, EditorSection(T("Versões para download", "Download versions"), true));
        AddEditorRow(layout, EditorField(T("Versão selecionada", "Selected version"), variants), EditorField(T("Nome da versão *", "Version name *"), variantName));
        variantActions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(3, 2, 0, 8)
        };
        var addVariant = GeneralStyle.Button(T("Adicionar versão", "Add version"), () => {
            if (model.Count >= 50) return;
            SaveCurrent();
            var variant = new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Nova versão", "New version") };
            model.Add(variant);
            RefreshVariants(variant.Id);
            variantName.Focus();
            variantName.SelectAll();
        });
        addVariant.Name = "AddDownloadVersion";
        removeVariant = GeneralStyle.Button(T("Excluir esta versão", "Delete this version"), () => {
            if (current == null || model.Count == 1) return;
            var removed = current;
            current = null;
            model.Remove(removed);
            if (principal == removed.Id) principal = model[0].Id;
            RefreshVariants(model[0].Id);
        });
        removeVariant.Name = "RemoveDownloadVersion";
        setDefault = GeneralStyle.Button(T("Usar por padrão", "Use by default"), () => {
            if (current == null) return;
            SaveCurrent();
            principal = current.Id;
            RefreshVariants(principal);
        });
        setDefault.Name = "SetDefaultDownloadVersion";
        foreach (var button in new[] { addVariant, removeVariant, setDefault }) {
            button.Margin = new Padding(0, 0, 8, 4);
            variantActions.Controls.Add(button);
        }
        editorTips.SetToolTip(setDefault, T("Versão usada quando nenhuma outra foi escolhida.", "Version used when no other version is selected."));
        AddEditorRow(layout, variantActions);
        AddEditorRow(layout, EditorField(T("Links ou arquivos * (um por linha)", "Links or files * (one per line)"), sources, 102));
        browse = GeneralStyle.Button(T("Adicionar arquivo local", "Add local file"), () => {
            using (var dialog = new OpenFileDialog { Filter = T("Pacotes de mods|*.zip;*.rar;*.7z", "Mod archives|*.zip;*.rar;*.7z"), Multiselect = true }) {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    sources.Lines = sources.Lines.Where(line => !String.IsNullOrWhiteSpace(line)).Concat(dialog.FileNames).Distinct().ToArray();
            }
        });
        browse.Anchor = AnchorStyles.Left;
        browse.Margin = new Padding(3, 3, 3, 6);
        AddEditorRow(layout, browse);
        sourceInfo.Font = GeneralStyle.Font(10);
        sourceInfo.Dock = DockStyle.Top;
        sourceInfo.Margin = new Padding(23, 4, 23, 0);
        sourceInfo.TextChanged += (s, e) => sourceInfo.Visible = sourceInfo.Text.Length > 0;
        sourceInfo.Visible = false;
        shell.Controls.Add(sourceInfo, 0, 1);

        if (alternatives != null) {
            var options = alternatives.Where(other => other.Id != entry.Id).ToList();
            AddEditorRow(layout, EditorSection(T("Mods alternativos", "Alternative mods"), true));
            alternative = GeneralStyle.Button("", () => {
                returningFromList = true;
                Complete(DialogResult.Retry);
            });
            alternative.Name = "AlternativeMod";
            alternative.Dock = DockStyle.Fill;
            alternative.AutoSize = false;
            alternative.Enabled = options.Count > 0;
            editorTips.SetToolTip(alternative, T("Escolha um mod da lista para instalar como alternativa a este.", "Choose a mod from the list to install as an alternative to this one."));
            unlink = GeneralStyle.Button(T("Desvincular", "Unlink"), () => SetLinkedAlternative(null));
            unlink.Name = "UnlinkAlternative";
            unlink.Margin = new Padding(8, 3, 0, 3);
            var pairing = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Margin = new Padding(3, 2, 3, 6) };
            pairing.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            pairing.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pairing.Controls.Add(alternative, 0, 0);
            pairing.Controls.Add(unlink, 1, 0);
            AddEditorRow(layout, pairing);
            SetLinkedAlternative(options.FirstOrDefault(other => entry.AlternativeGroup != null && other.AlternativeGroup == entry.AlternativeGroup));
        }

        installNote = LabelFor(installInfo ?? "");
        installNote.Font = validation.Font = usePending.Font = GeneralStyle.Font(10);
        AddEditorRow(layout, installNote);
        AddEditorRow(layout, usePending);
        AddEditorRow(layout, validation);
        layout.SizeChanged += (s, e) => ResizeFeedback(layout.ClientSize.Width - layout.Padding.Horizontal);
        foreach (var field in new Control[] { modName, category, tags, description, nsfw, variants, variantName, sources, browse, alternative, unlink, usePending, addVariant, removeVariant, setDefault }) {
            if (field == null) continue;
            field.Enter += (s, e) => {
                if (!Visible) return;
                var area = scroll.RectangleToClient(field.RectangleToScreen(field.ClientRectangle));
                if (area.Bottom > scroll.ClientSize.Height) scroll.ScrollTo(-layout.Top + area.Bottom - scroll.ClientSize.Height + 8);
                else if (area.Top < 0) scroll.ScrollTo(Math.Max(0, -layout.Top + area.Top - 8));
            };
        }        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0), Padding=new Padding(20,8,20,18) };
        install = GeneralStyle.Button(T("Instalar", "Install"), () => Finish(entry.Id, true), true);
        install.Visible = allowInstall;
        commands.Controls.Add(install);
        save = GeneralStyle.Button(T("Salvar", "Save"), () => Finish(entry.Id, false), false);
        commands.Controls.Add(save);
        var close = GeneralStyle.Button(T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(close);
        shell.Controls.Add(commands, 0, 2);
        AcceptButton = save;
        CancelButton = close;
        variants.SelectedIndexChanged += (s, e) => {
            if (changing || variants.SelectedIndex < 0) return;
            string next = model[variants.SelectedIndex].Id;
            SaveCurrent();
            RefreshVariants(next);
        };
        sources.ReadOnlyChanged += (s, e) => browse.Enabled = !sources.ReadOnly && !attachment;
        foreach (Control field in new Control[] { modName, category, tags, description, variantName, sources })
            field.TextChanged += (s, e) => ValidateInputs();
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        RemoveFieldBorders(this);
        MergeSourceTags(entry.Tags);
        RefreshVariants(selected);
        sources.TextChanged += (s, e) => ScheduleSourceInfo();
        sourceDelay.Tick += async (s, e) => { sourceDelay.Stop(); await UpdateSourceInfo(); };
        Shown += (s, e) => ScheduleSourceInfo();
        Disposed += (s, e) => { sourceDelay.Dispose(); editorTips.Dispose(); };
    }

    protected override void OnLoad(EventArgs e) {
        var working = Screen.FromControl(Owner ?? this).WorkingArea;
        Height = Math.Min(Height, working.Height - 32);
        base.OnLoad(e);
    }

    internal void SetLinkedAlternative(GeneralModEntry entry) {
        LinkedAlternative = entry;
        if (alternative == null) return;
        alternative.Text = entry == null ? T("Vincular outro mod", "Link another mod") : entry.Name;
        alternative.AccessibleName = entry == null ? alternative.Text : T("Trocar mod vinculado: ", "Change linked mod: ") + entry.Name;
        unlink.Enabled = entry != null;
    }

    static void AddEditorRow(TableLayoutPanel layout, Control left, Control right = null) {
        int row = layout.RowCount++;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(left, 0, row);
        if (right == null) layout.SetColumnSpan(left, 2);
        else layout.Controls.Add(right, 1, row);
    }

    static Control EditorSection(string title, bool divider) {
        var heading = new TableLayoutPanel {
            Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1,
            Margin = new Padding(3, divider ? 16 : 0, 3, 3)
        };
        if (divider) {
            var line = new Panel { Dock = DockStyle.Top, Height = 1, Margin = new Padding(0, 0, 0, 9) };
            line.Paint += (s, e) => e.Graphics.Clear(GeneralStyle.Line);
            heading.Controls.Add(line);
        }
        heading.Controls.Add(new Label { Text = title, AutoSize = true, Font = GeneralStyle.Font(13, true), Margin = Padding.Empty });
        return heading;
    }

    static Control EditorField(string title, Control field, int height = 0) {
        field.AccessibleName = title;
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = Padding.Empty };
        var label = LabelFor(title);
        label.Margin = new Padding(3, 4, 3, 3);
        panel.Controls.Add(label);
        Control body = field is GeneralPicker ? field : FieldFor(field, height > 0);
        body.Margin = new Padding(3, 0, 3, 3);
        if (height > 0) body.MinimumSize = new Size(100, height);
        panel.Controls.Add(body);
        return panel;
    }

    internal void ConfigureAttachment(string file) {
        attachment = true;
        installNote.Text = T("Arquivo recebido: ", "Received file: ") + file;
        variants.Enabled = variantName.Enabled = variantActions.Enabled = browse.Enabled = false;
        sources.ReadOnly = true;
        install.Text = T("Vincular e continuar", "Attach and continue");
        install.Visible = true;
        ValidateInputs();
    }
    internal void OfferAttachment(GeneralModEntry target) {
        var independent=Draft(null);
        usePending.Text=T("Este arquivo pertence ao item aguardando: ","This file belongs to the waiting item: ")+target.Name;
        usePending.Visible=true;
        ResizeFeedback(usePending.Parent.ClientSize.Width - usePending.Parent.Padding.Horizontal);
        usePending.CheckedChanged+=(s,e)=>{
            var metadata=usePending.Checked?target:independent;
            modName.Text=metadata.Name;category.Text=metadata.Category??"";description.Text=metadata.Description??"";
            foreach (Control chip in tagChips.Controls.Cast<Control>().ToArray()) chip.Dispose();
            selectedTags.Clear();
            tags.Clear();
            MergeSourceTags(metadata.Tags);
            nsfw.Checked=metadata.Nsfw;
            install.Text=usePending.Checked?T("Vincular e continuar","Attach and continue"):T("Instalar","Install");
        };
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }
    internal static Label LabelFor(string text) { return new Label { Text = text, AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 8, 3, 6) }; }

    internal static GeneralSurface FieldFor(Control field, bool multiline) {
        var surface = new GeneralSurface {
            Dock = DockStyle.Fill,
            AutoSize = !multiline,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Size = new Size(100, multiline ? 80 : 44),
            Padding = multiline ? new Padding(12, 10, 12, 10) : new Padding(12, 11, 12, 6),
            MinimumSize = new Size(100, multiline ? 58 : 44),
            Margin = new Padding(3, 3, 3, 3)
        };
        field.Dock = DockStyle.Fill;
        surface.Controls.Add(multiline&&(field is TextBox||field is ListBox)?(Control)new GeneralScrollView(field):field);
        return surface;
    }

    internal static void RemoveFieldBorders(Control parent) {
        foreach (Control control in parent.Controls) {
            var textbox = control as TextBox;
            if (textbox != null) textbox.BorderStyle = BorderStyle.None;
            var list = control as ListBox;
            if (list != null) list.BorderStyle = BorderStyle.None;
            RemoveFieldBorders(control);
        }
    }

    void ResizeFeedback(int width) {
        var maximum = new Size(Math.Max(120, width - 8), 0);
        installNote.MaximumSize = validation.MaximumSize = usePending.MaximumSize = sourceInfo.MaximumSize = maximum;
    }

    internal void MergeSourceTags(IEnumerable<string> incoming) {
        tagChips.SuspendLayout();
        try {
            foreach (string tag in incoming ?? Enumerable.Empty<string>()) AddTag((tag ?? "").Trim());
            tagChips.Visible = selectedTags.Count > 0;
        } finally { tagChips.ResumeLayout(true); }
        ValidateInputs();
    }

    bool AddTag(string tag) {
        if (tag.Length == 0 || tag.Length > 60 || tag.Any(Char.IsControl) || selectedTags.Count >= 32 ||
            selectedTags.Contains(tag, StringComparer.OrdinalIgnoreCase)) return false;

        selectedTags.Add(tag);
        var chip = new GeneralButton {
            Text = tag + " ×", AccessibleName = T("Remover tag ", "Remove tag ") + tag, AutoSize = true,
            MinimumSize = new Size(44, 30), Padding = new Padding(12, 0, 12, 0), Margin = new Padding(0, 0, 6, 4)
        };
        editorTips.SetToolTip(chip, chip.AccessibleName);
        chip.Click += (s, e) => {
            selectedTags.Remove(tag);
            tags.Focus();
            chip.Dispose();
            tagChips.Visible = selectedTags.Count > 0;
            ValidateInputs();
        };
        FitTagChip(chip);
        tagChips.Controls.Add(chip);
        tagChips.Visible = true;
        return true;
    }

    void FitTagChip(Control chip) {
        if (tagChips.ClientSize.Width > 0)
            chip.MaximumSize = new Size(Math.Max(44, tagChips.ClientSize.Width - chip.Margin.Horizontal), 0);
    }

    void SaveCurrent() {
        if (current == null || attachment) return;
        current.Name = variantName.Text.Trim();
        if (String.IsNullOrEmpty(current.CyberpunkId))
            current.Sources = sources.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).Distinct().ToList();
        if(current.SourceInfo==null)current.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
        foreach(string key in current.SourceInfo.Keys.Where(key=>!current.Sources.Contains(key)).ToArray())current.SourceInfo.Remove(key);
    }

    void RefreshVariants(string id) {
        changing = true;
        variants.Items.Clear();
        foreach (var variant in model) variants.Items.Add(VariantLabel(variant));
        int index = model.FindIndex(variant => variant.Id == id);
        variants.SelectedIndex = index < 0 ? 0 : index;
        current = model[variants.SelectedIndex];
        variantName.Text = current.Name;
        sources.Lines = current.Sources.ToArray();
        sources.ReadOnly = !String.IsNullOrEmpty(current.CyberpunkId);
        removeVariant.Enabled = model.Count > 1;
        setDefault.Enabled = current.Id != principal;
        changing = false;
        ValidateInputs();ScheduleSourceInfo();
    }

    string VariantLabel(GeneralModVariant variant) { return variant.Name + (variant.Id == principal ? T(" (padrão)", " (default)") : ""); }

    GeneralModEntry Draft(string id) {
        SaveCurrent();
        return new GeneralModEntry {
            Id = id, Name = modName.Text.Trim(), Category = category.Text.Trim(), Description = description.Text.Trim(),
            Tags = selectedTags.Concat(new[] { tags.Text.Trim() }).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Nsfw = nsfw.Checked, DefaultVariant = principal, Variants = model
        };
    }

    internal static string ValidationMessage(GeneralModEntry entry) {
        if (String.IsNullOrWhiteSpace(entry.Name) || entry.Name.Length > 300 || entry.Name.Any(Char.IsControl))
            return T("Informe o nome do mod.", "Enter the mod name.");
        if (entry.Category != null && (entry.Category.Length > 120 || entry.Category.Any(Char.IsControl)))
            return T("A categoria aceita até 120 caracteres.", "The category can contain up to 120 characters.");
        if (entry.Description != null && entry.Description.Length > 4000)
            return T("A descrição aceita até 4.000 caracteres.", "The description can contain up to 4,000 characters.");
        if (entry.Tags != null && (entry.Tags.Count > 32 || entry.Tags.Any(tag => tag.Length > 60 || tag.Any(Char.IsControl))))
            return T("Use até 32 tags de 60 caracteres cada.", "Use up to 32 tags of 60 characters each.");
        foreach (var variant in entry.Variants) {
            if (String.IsNullOrWhiteSpace(variant.Name) || variant.Name.Length > 300 || variant.Name.Any(Char.IsControl))
                return T("Informe o nome de cada variante.", "Enter a name for each variant.");
            if (variant.Sources.Count == 0 || variant.Sources.Count > 32)
                return T("Informe de 1 a 32 links ou arquivos para cada versão.", "Enter 1 to 32 links or files for each version.");
            try { foreach (string source in variant.Sources) GeneralModLibrary.ValidateSource(source); }
            catch (Exception) {
                return T("Fonte inválida. Use um link HTTPS público ou o caminho de um ZIP, RAR ou 7z.", "Invalid source. Use a public HTTPS link or a ZIP, RAR or 7z file path.");
            }
        }
        return "";
    }

    void ValidateInputs() {
        if (changing || install == null || save == null) return;
        string error = ValidationMessage(Draft(null));
        validation.Text = error;
        validation.Visible = error.Length != 0;
        install.Enabled = save.Enabled = error.Length == 0;
        if (current != null && variants.SelectedIndex >= 0) {
            changing = true;
            int index = variants.SelectedIndex;
            variants.Items[index] = VariantLabel(current);
            variants.SelectedIndex = index;
            changing = false;
        }
    }

    void Finish(string id, bool requested) {
        GeneralModEntry draft = Draft(id);
        string error = ValidationMessage(draft);
        if (error.Length != 0) { validation.Text = error; return; }
        foreach (var variant in draft.Variants) GeneralModLibrary.NormalizeSources(variant);
        Result = draft;
        SelectedVariant = current.Id;
        AttachRequested = usePending.Checked;
        InstallRequested = requested && !AttachRequested;
        DialogResult = DialogResult.OK;
    }
}

internal sealed class GeneralCategoryDetails : GeneralDialog {
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    internal string ResultName { get; private set; }
    internal string ResultDescription { get; private set; }
    internal bool DeleteRequested { get; private set; }

    internal GeneralCategoryDetails(string initialName, string initialDescription, bool isUncategorized = false, bool canDelete = false) {
        Text = GeneralStyle.T("Categoria", "Category");
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(500, 390);
        MinimumSize = new Size(440, 350);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 18), ColumnCount = 1, RowCount = 6 };
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Nome *", "Name *")));
        layout.Controls.Add(GeneralModEditor.FieldFor(name, false));
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Descrição", "Description")));
        layout.Controls.Add(GeneralModEditor.FieldFor(description, true));
        var feedback = new Label { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false, Font = GeneralStyle.Font(10, false), Margin = new Padding(3, 6, 3, 6) };
        layout.Controls.Add(feedback);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            ResultName = name.Text.Trim();
            ResultDescription = description.Text.Trim();
            DialogResult = DialogResult.OK;
        }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(save);
        commands.Controls.Add(cancel);
        if (canDelete && !isUncategorized && !String.IsNullOrWhiteSpace(initialName)) {
            var delete = (GeneralButton)GeneralStyle.Button(GeneralStyle.T("Excluir categoria", "Delete category"), () => {
                DeleteRequested = true;
                Complete(DialogResult.OK);
            });
            delete.Destructive = true;
            commands.Controls.Add(delete);
        }
        layout.Controls.Add(commands);
        name.TextChanged += (s, e) => {
            save.Enabled = !String.IsNullOrWhiteSpace(name.Text) && !name.Text.Any(Char.IsControl);
            feedback.Text = save.Enabled ? "" : GeneralStyle.T("Informe o nome da categoria.", "Enter the category name.");
        };
        name.Text = initialName ?? "";
        description.Text = initialDescription ?? "";
        save.Enabled = !String.IsNullOrWhiteSpace(name.Text);
        AcceptButton = save;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        GeneralModEditor.RemoveFieldBorders(this);
    }
}

internal sealed class GeneralPresetEditor : GeneralDialog {
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    internal GeneralModPreset Result { get; private set; }

    internal GeneralPresetEditor(GeneralModPreset preset, IEnumerable<GeneralModEntry> mods, IEnumerable<string> selectedIds) {
        Text = "Preset";
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(530, 510);
        MinimumSize = new Size(460, 420);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var entries = mods.ToList();
        var selected = new HashSet<string>(selectedIds ?? (preset == null ? Enumerable.Empty<string>() : preset.ModIds), StringComparer.Ordinal);
        var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        foreach (var mod in entries) choices.Items.Add(mod.Name, selected.Contains(mod.Id));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 18), ColumnCount = 1, RowCount = 5 };
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Nome do preset *", "Preset name *")));
        layout.Controls.Add(GeneralModEditor.FieldFor(name, false));
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Mods incluídos", "Included mods")));
        layout.Controls.Add(GeneralModEditor.FieldFor(choices, true));
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            Result = new GeneralModPreset {
                Id = preset == null ? Guid.NewGuid().ToString("N") : preset.Id,
                Name = name.Text.Trim(), ModIds = choices.CheckedIndices.Cast<int>().Select(index => entries[index].Id).ToList(),
                Categories = preset == null ? new List<string>() : new List<string>(preset.Categories)
            };
            DialogResult = DialogResult.OK;
        }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(save);
        commands.Controls.Add(cancel);
        layout.Controls.Add(commands);
        name.TextChanged += (s, e) => save.Enabled = !String.IsNullOrWhiteSpace(name.Text) && !name.Text.Any(Char.IsControl);
        name.Text = preset == null ? "" : preset.Name;
        save.Enabled = !String.IsNullOrWhiteSpace(name.Text);
        AcceptButton = save;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        GeneralModEditor.RemoveFieldBorders(this);
    }
}

internal sealed class GeneralCategoryEditor : GeneralDialog {
    readonly ListBox list = new ListBox { Dock = DockStyle.Fill };
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    internal readonly List<string> Categories;
    internal readonly Dictionary<string, string> Changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal GeneralCategoryEditor(IEnumerable<string> categories) {
        Categories = categories.ToList();
        foreach (string category in Categories) Changes[category] = category;
        Text = GeneralStyle.T("Categorias", "Categories");
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(470, 400);
        MinimumSize = new Size(430, 360);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 18), ColumnCount = 1, RowCount = 4 };
        Controls.Add(layout);
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(i == 0 ? SizeType.Percent : SizeType.AutoSize, i == 0 ? 100 : 0));
        layout.Controls.Add(GeneralModEditor.FieldFor(list, true), 0, 0);
        layout.Controls.Add(GeneralModEditor.FieldFor(name, false), 0, 1);
        var edit = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Adicionar", "Add"), () => Change(false, false), false));
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Renomear", "Rename"), () => Change(true, false), false));
        edit.Controls.Add(GeneralStyle.Button(GeneralStyle.T("Excluir", "Delete"), () => Change(true, true), false));
        layout.Controls.Add(edit, 0, 2);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => { DialogResult = DialogResult.OK; }, true);
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(save);
        commands.Controls.Add(cancel);
        layout.Controls.Add(commands, 0, 3);
        CancelButton = cancel;
        AcceptButton = save;
        list.SelectedIndexChanged += (s, e) => { if (list.SelectedItem != null) name.Text = (string)list.SelectedItem; };
        RefreshList();
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        GeneralModEditor.RemoveFieldBorders(this);
    }

    void RefreshList() { list.Items.Clear(); list.Items.AddRange(Categories.Cast<object>().ToArray()); }

    void Change(bool existing, bool remove) {
        string old = list.SelectedItem as string;
        string value = remove ? "" : name.Text.Trim();
        if (existing && old == null) return;
        if (!remove && (value.Length == 0 || value.Any(Char.IsControl) || Categories.Any(item => String.Equals(item, value, StringComparison.OrdinalIgnoreCase) && item != old))) return;
        if (existing) {
            foreach (string original in Changes.Where(pair => String.Equals(pair.Value, old, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToList()) Changes[original] = value;
            int index = Categories.IndexOf(old);
            if (remove) Categories.RemoveAt(index); else Categories[index] = value;
        } else Categories.Add(value);
        RefreshList();
        if (!remove) list.SelectedItem = value;
    }
}


