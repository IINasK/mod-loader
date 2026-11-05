using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralModEditor : Form {
    readonly TextBox modName = new TextBox { Dock = DockStyle.Fill, MaxLength = 300 };
    readonly ComboBox category = new GeneralPicker { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 120 };
    readonly TextBox tags = new TextBox { Dock = DockStyle.Fill, MaxLength = 2015 };
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    readonly CheckBox nsfw = new CheckBox { AutoSize = true };
    readonly CheckBox usePending = new CheckBox { AutoSize = true, Visible = false, Dock = DockStyle.Fill };
    readonly ListBox variants = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false };
    readonly TextBox variantName = new TextBox { Dock = DockStyle.Fill, MaxLength = 300 };
    readonly TextBox sources = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 131104 };
    readonly Label validation = new Label { AutoSize = true, Dock = DockStyle.Fill, UseMnemonic = false };
    readonly Label installNote;
    readonly Button save, install, removeVariant, browse;
    readonly FlowLayoutPanel variantActions;
    readonly List<GeneralModVariant> model;
    GeneralModVariant current;
    string principal;
    bool changing, attachment;
    internal GeneralModEntry Result { get; private set; }
    internal string SelectedVariant { get; private set; }
    internal bool InstallRequested { get; private set; }
    internal bool AttachRequested { get; private set; }

    internal GeneralModEditor(GeneralModEntry entry, IEnumerable<string> categories, string selected, bool allowInstall, string installInfo) {
        model = entry.Variants.Select(v => new GeneralModVariant {
            Id = v.Id, Name = v.Name, Sources = v.Sources.ToList(), CyberpunkId = v.CyberpunkId, CyberpunkVariant = v.CyberpunkVariant
        }).ToList();
        if (model.Count == 0) model.Add(new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Principal", "Default") });
        principal = model.Any(v => v.Id == entry.DefaultVariant) ? entry.DefaultVariant : model[0].Id;
        Text = T("Detalhes do mod", "Mod details");
        Font = GeneralStyle.Font(10, false);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(780, 670);
        MinimumSize = new Size(690, 620);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 2, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 200));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < layout.RowCount; i++)
            layout.RowStyles.Add(new RowStyle(i == 3 || i == 6 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 24 : i == 6 ? 76 : 0));
        Controls.Add(layout);
        layout.Controls.Add(LabelFor(T("Nome do mod *", "Mod name *")), 0, 0);
        layout.Controls.Add(modName, 1, 0);
        modName.Text = entry.Name;
        layout.Controls.Add(LabelFor(T("Categoria", "Category")), 0, 1);
        layout.Controls.Add(category, 1, 1);
        category.Items.AddRange(categories.Cast<object>().ToArray());
        category.Text = entry.Category ?? "";
        layout.Controls.Add(LabelFor(T("Tags (separadas por vírgula)", "Tags (comma separated)")), 0, 2);
        layout.Controls.Add(tags, 1, 2);
        tags.Text = String.Join(", ", entry.Tags ?? new List<string>());
        layout.Controls.Add(LabelFor(T("Descrição", "Description")), 0, 3);
        layout.Controls.Add(description, 1, 3);
        description.Text = entry.Description ?? "";
        nsfw.Checked = entry.Nsfw;
        nsfw.Text = T("NSFW · conteúdo adulto", "NSFW · adult content");
        layout.Controls.Add(nsfw, 1, 4);
        layout.Controls.Add(LabelFor(T("Opções de download", "Download options")), 0, 5);
        layout.Controls.Add(LabelFor(T("Nome e fontes *", "Name and sources *")), 1, 5);
        layout.Controls.Add(variants, 0, 6);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(variantName, 0, 0);
        fields.Controls.Add(LabelFor(T("Link ou arquivo. Várias linhas = arquivos complementares.", "Link or archive. Multiple lines = complementary files.")), 0, 1);
        fields.Controls.Add(sources, 0, 2);
        browse = GeneralStyle.Button(T("Escolher arquivo", "Choose file"), () => {
            using (var dialog = new OpenFileDialog { Filter = T("Pacotes de mods|*.zip;*.rar;*.7z", "Mod archives|*.zip;*.rar;*.7z"), Multiselect = true }) {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    sources.Lines = sources.Lines.Where(line => !String.IsNullOrWhiteSpace(line)).Concat(dialog.FileNames).Distinct().ToArray();
            }
        }, false);
        fields.Controls.Add(browse, 0, 3);
        layout.Controls.Add(fields, 1, 6);

        variantActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        variantActions.Controls.Add(GeneralStyle.Button(T("Adicionar opção", "Add option"), () => {
            if (model.Count >= 50) return;
            SaveCurrent();
            var variant = new GeneralModVariant { Id = Guid.NewGuid().ToString("N"), Name = T("Nova variante", "New variant") };
            model.Add(variant);
            RefreshVariants(variant.Id);
            variantName.Focus();
            variantName.SelectAll();
        }, false));
        removeVariant = GeneralStyle.Button(T("Excluir variante", "Delete variant"), () => {
            if (current == null || model.Count == 1) return;
            var removed = current;
            current = null;
            model.Remove(removed);
            if (principal == removed.Id) principal = model[0].Id;
            RefreshVariants(model[0].Id);
        }, false);
        variantActions.Controls.Add(removeVariant);
        variantActions.Controls.Add(GeneralStyle.Button(T("Definir como principal", "Set as default"), () => {
            if (current == null) return;
            SaveCurrent();
            principal = current.Id;
            RefreshVariants(principal);
        }, false));
        layout.Controls.Add(variantActions, 0, 7);
        layout.SetColumnSpan(variantActions, 2);

        var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 6, 0, 8) };
        installNote = LabelFor(installInfo ?? "");
        installNote.MaximumSize = new Size(700, 0);
        validation.MaximumSize = new Size(700, 0);
        feedback.Controls.Add(installNote);
        feedback.Controls.Add(usePending);
        feedback.Controls.Add(validation);
        layout.Controls.Add(feedback, 0, 8);
        layout.SetColumnSpan(feedback, 2);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        install = GeneralStyle.Button(T("Instalar", "Install"), () => Finish(entry.Id, true), true);
        install.Visible = allowInstall;
        commands.Controls.Add(install);
        save = GeneralStyle.Button(T("Salvar", "Save"), () => Finish(entry.Id, false), false);
        commands.Controls.Add(save);
        var close = GeneralStyle.Button(T("Cancelar", "Cancel"), () => { DialogResult = DialogResult.Cancel; }, false);
        commands.Controls.Add(close);
        layout.Controls.Add(commands, 0, 9);
        layout.SetColumnSpan(commands, 2);
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
        RefreshVariants(selected);
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
        usePending.MaximumSize=new Size(700,0);usePending.Visible=true;
        usePending.CheckedChanged+=(s,e)=>{
            var metadata=usePending.Checked?target:independent;
            modName.Text=metadata.Name;category.Text=metadata.Category??"";description.Text=metadata.Description??"";
            tags.Text=String.Join(", ",metadata.Tags??new List<string>());nsfw.Checked=metadata.Nsfw;
            install.Text=usePending.Checked?T("Vincular e continuar","Attach and continue"):T("Instalar","Install");
        };
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }
    internal static Label LabelFor(string text) { return new Label { Text = text, AutoSize = true, UseMnemonic = false, Margin = new Padding(3, 7, 3, 5) }; }

    void SaveCurrent() {
        if (current == null || attachment) return;
        current.Name = variantName.Text.Trim();
        if (String.IsNullOrEmpty(current.CyberpunkId))
            current.Sources = sources.Lines.Select(line => line.Trim()).Where(line => line.Length > 0).Distinct().ToList();
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
        changing = false;
        ValidateInputs();
    }

    string VariantLabel(GeneralModVariant variant) { return variant.Name + (variant.Id == principal ? T(" (principal)", " (default)") : ""); }

    GeneralModEntry Draft(string id) {
        SaveCurrent();
        return new GeneralModEntry {
            Id = id, Name = modName.Text.Trim(), Category = category.Text.Trim(), Description = description.Text.Trim(),
            Tags = tags.Text.Split(',').Select(tag => tag.Trim()).Where(tag => tag.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
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
                return T("Informe de 1 a 32 fontes para cada variante.", "Enter between 1 and 32 sources for each variant.");
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
        install.Enabled = save.Enabled = error.Length == 0;
        if (current != null && variants.SelectedIndex >= 0) {
            changing = true;
            variants.Items[variants.SelectedIndex] = VariantLabel(current);
            changing = false;
        }
    }

    void Finish(string id, bool requested) {
        GeneralModEntry draft = Draft(id);
        string error = ValidationMessage(draft);
        if (error.Length != 0) { validation.Text = error; return; }
        foreach (var variant in draft.Variants) variant.Sources = variant.Sources.Select(GeneralModLibrary.ValidateSource).ToList();
        Result = draft;
        SelectedVariant = current.Id;
        AttachRequested = usePending.Checked;
        InstallRequested = requested && !AttachRequested;
        DialogResult = DialogResult.OK;
    }
}

internal sealed class GeneralCategoryDetails : Form {
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    internal string ResultName { get; private set; }
    internal string ResultDescription { get; private set; }

    internal GeneralCategoryDetails(string initialName, string initialDescription, bool isUncategorized = false) {
        Text = GeneralStyle.T("Categoria", "Category");
        Font = GeneralStyle.Font(10, false);
        ClientSize = new Size(500, 330);
        MinimumSize = new Size(440, 300);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 6 };
        for (int i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Nome *", "Name *")));
        layout.Controls.Add(name);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Descrição", "Description")));
        layout.Controls.Add(description);
        var feedback = new Label { AutoSize = true, Dock = DockStyle.Fill };
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
    }
}

internal sealed class GeneralPresetEditor : Form {
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    internal GeneralModPreset Result { get; private set; }

    internal GeneralPresetEditor(GeneralModPreset preset, IEnumerable<GeneralModEntry> mods, IEnumerable<string> selectedIds) {
        Text = "Preset";
        Font = GeneralStyle.Font(10, false);
        ClientSize = new Size(530, 460);
        MinimumSize = new Size(460, 350);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var entries = mods.ToList();
        var selected = new HashSet<string>(selectedIds ?? (preset == null ? Enumerable.Empty<string>() : preset.ModIds), StringComparer.Ordinal);
        var choices = new CheckedListBox { Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false };
        foreach (var mod in entries) choices.Items.Add(mod.Name, selected.Contains(mod.Id));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 5 };
        for (int i = 0; i < 5; i++) layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Nome do preset *", "Preset name *")));
        layout.Controls.Add(name);
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Mods incluídos", "Included mods")));
        layout.Controls.Add(choices);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            Result = new GeneralModPreset {
                Id = preset == null ? Guid.NewGuid().ToString("N") : preset.Id,
                Name = name.Text.Trim(), ModIds = choices.CheckedIndices.Cast<int>().Select(index => entries[index].Id).ToList()
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
    }
}

internal sealed class GeneralCategoryEditor : Form {
    readonly ListBox list = new ListBox { Dock = DockStyle.Fill };
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    internal readonly List<string> Categories;
    internal readonly Dictionary<string, string> Changes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    internal GeneralCategoryEditor(IEnumerable<string> categories) {
        Categories = categories.ToList();
        foreach (string category in Categories) Changes[category] = category;
        Text = GeneralStyle.T("Categorias", "Categories");
        Font = GeneralStyle.Font(10, false);
        ClientSize = new Size(430, 340);
        StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 1, RowCount = 4 };
        Controls.Add(layout);
        for (int i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(i == 0 ? SizeType.Percent : SizeType.AutoSize, i == 0 ? 100 : 0));
        layout.Controls.Add(list, 0, 0);
        layout.Controls.Add(name, 0, 1);
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

