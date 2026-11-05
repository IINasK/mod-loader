using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralModEditor : GeneralDialog {
    readonly TextBox modName = new TextBox { Dock = DockStyle.Fill, MaxLength = 300 };
    readonly GeneralPicker category = new GeneralPicker { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown, MaxLength = 120 };
    readonly TextBox tags = new TextBox { Dock = DockStyle.Fill, MaxLength = 2015 };
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    readonly CheckBox nsfw = new CheckBox { AutoSize = true };
    readonly CheckBox usePending = new CheckBox { AutoSize = true, Visible = false, Dock = DockStyle.Fill, UseMnemonic = false };
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
        Font = GeneralStyle.Font(11, false);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(780, 760);
        MinimumSize = new Size(700, 710);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 12, 20, 18), ColumnCount = 2, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 190));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < layout.RowCount; i++)
            layout.RowStyles.Add(new RowStyle(i == 3 ? SizeType.Absolute : i == 6 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 84 : i == 6 ? 100 : 0));
        Controls.Add(layout);
        layout.Controls.Add(LabelFor(T("Nome do mod *", "Mod name *")), 0, 0);
        layout.Controls.Add(FieldFor(modName, false), 1, 0);
        modName.Text = entry.Name;
        layout.Controls.Add(LabelFor(T("Categoria", "Category")), 0, 1);
        layout.Controls.Add(category, 1, 1);
        category.Items.AddRange(categories.Cast<object>().ToArray());
        category.Text = entry.Category ?? "";
        layout.Controls.Add(LabelFor(T("Tags (por vírgula)", "Tags (comma separated)")), 0, 2);
        layout.Controls.Add(FieldFor(tags, false), 1, 2);
        tags.Text = String.Join(", ", entry.Tags ?? new List<string>());
        layout.Controls.Add(LabelFor(T("Descrição", "Description")), 0, 3);
        layout.Controls.Add(FieldFor(description, true), 1, 3);
        description.Text = entry.Description ?? "";
        nsfw.Checked = entry.Nsfw;
        nsfw.Text = T("NSFW · conteúdo adulto", "NSFW · adult content");
        layout.Controls.Add(nsfw, 1, 4);
        layout.Controls.Add(LabelFor(T("Opções de download", "Download options")), 0, 5);
        layout.Controls.Add(LabelFor(T("Nome e fontes *", "Name and sources *")), 1, 5);
        layout.Controls.Add(FieldFor(variants, true), 0, 6);

        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1, Margin = new Padding(3, 3, 0, 3) };
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        fields.Controls.Add(FieldFor(variantName, false), 0, 0);
        var sourceHint = LabelFor(T("Link ou arquivo. Uma linha por arquivo complementar.", "Link or archive. One line per complementary file."));
        sourceHint.Font = GeneralStyle.Font(10, false);
        sourceHint.Margin = new Padding(3, 5, 3, 7);
        fields.Controls.Add(sourceHint, 0, 1);
        fields.SizeChanged += (s, e) => sourceHint.MaximumSize = new Size(Math.Max(100, fields.ClientSize.Width - 8), 0);
        fields.Controls.Add(FieldFor(sources, true), 0, 2);
        browse = GeneralStyle.Button(T("Escolher arquivo", "Choose file"), () => {
            using (var dialog = new OpenFileDialog { Filter = T("Pacotes de mods|*.zip;*.rar;*.7z", "Mod archives|*.zip;*.rar;*.7z"), Multiselect = true }) {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    sources.Lines = sources.Lines.Where(line => !String.IsNullOrWhiteSpace(line)).Concat(dialog.FileNames).Distinct().ToArray();
            }
        }, false);
        fields.Controls.Add(browse, 0, 3);
        layout.Controls.Add(fields, 1, 6);

        variantActions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Margin = new Padding(0, 5, 0, 3) };
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

        var feedback = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 4, 0, 6) };
        installNote = LabelFor(installInfo ?? "");
        installNote.Font = validation.Font = usePending.Font = GeneralStyle.Font(10, false);
        feedback.SizeChanged += (s, e) => ResizeFeedback(feedback.ClientSize.Width);
        feedback.Controls.Add(installNote);
        feedback.Controls.Add(usePending);
        feedback.Controls.Add(validation);
        layout.Controls.Add(feedback, 0, 8);
        layout.SetColumnSpan(feedback, 2);
        var commands = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 6, 0, 0) };
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
        RemoveFieldBorders(this);
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
        usePending.Visible=true;
        ResizeFeedback(usePending.Parent.ClientSize.Width);
        usePending.CheckedChanged+=(s,e)=>{
            var metadata=usePending.Checked?target:independent;
            modName.Text=metadata.Name;category.Text=metadata.Category??"";description.Text=metadata.Description??"";
            tags.Text=String.Join(", ",metadata.Tags??new List<string>());nsfw.Checked=metadata.Nsfw;
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
            Size = new Size(100, multiline ? 80 : 38),
            Padding = multiline ? new Padding(12, 10, 12, 10) : new Padding(12, 8, 12, 6),
            MinimumSize = new Size(100, multiline ? 58 : 38),
            Margin = new Padding(3, 3, 3, 3)
        };
        field.Dock = DockStyle.Fill;
        surface.Controls.Add(field);
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
        installNote.MaximumSize = validation.MaximumSize = usePending.MaximumSize = maximum;
    }

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
        validation.Visible = error.Length != 0;
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

internal sealed class GeneralCategoryDetails : GeneralDialog {
    readonly TextBox name = new TextBox { Dock = DockStyle.Fill, MaxLength = 120 };
    readonly TextBox description = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, MaxLength = 4000 };
    internal string ResultName { get; private set; }
    internal string ResultDescription { get; private set; }

    internal GeneralCategoryDetails(string initialName, string initialDescription, bool isUncategorized = false) {
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


