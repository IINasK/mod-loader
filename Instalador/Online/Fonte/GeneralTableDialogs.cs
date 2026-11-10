using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralNameDialog : GeneralDialog {
    readonly TextBox input = new TextBox { Name = "NameInput", MaxLength = 120, Dock = DockStyle.Fill };
    internal string ResultName { get; private set; }

    internal GeneralNameDialog(string title, string initialName,string description=null,string fieldLabel=null,int maxLength=120) : base(title) {
        ClientSize = new Size(500, description==null?220:300);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 8, 20, 20), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(description==null?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.Absolute,52));
        if(description!=null)layout.RowStyles.Add(new RowStyle(SizeType.Absolute,52));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        input.MaxLength=maxLength;
        layout.Controls.Add(GeneralModEditor.LabelFor(fieldLabel??GeneralStyle.T("Nome *", "Name *")), 0, 0);
        input.AccessibleName = fieldLabel??GeneralStyle.T("Nome", "Name");
        layout.Controls.Add(GeneralTableDialogLayout.Input(input), 0, 1);
        var commands = GeneralTableDialogLayout.Commands();
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            ResultName = input.Text.Trim(); Complete(DialogResult.OK);
        }, true);
        save.Name = "SaveName";
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => Complete(DialogResult.Cancel));
        cancel.Name = "CancelName";
        commands.Controls.Add(save); commands.Controls.Add(cancel);
        if(description!=null){layout.RowCount=4;layout.Controls.Add(new Label{Name="NameDescription",Text=description,Dock=DockStyle.Fill,Font=GeneralStyle.Font(10.5f),Margin=new Padding(0,4,0,0)},0,2);}
        layout.Controls.Add(commands, 0, description==null?2:3); Content.Controls.Add(layout);
        Action validate = () => save.Enabled = !String.IsNullOrWhiteSpace(input.Text) && !input.Text.Any(Char.IsControl);
        input.TextChanged += (s, e) => validate();
        input.Text = initialName ?? ""; validate();
        AcceptButton = save; CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark); input.BorderStyle = BorderStyle.None;
    }
}

internal sealed class GeneralColumnFilterDialog : GeneralDialog {
    readonly CheckedListBox list = new CheckedListBox { Name = "ColumnChoices", Dock = DockStyle.Fill, CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false };
    readonly TextBox search = new TextBox { Name = "ColumnSearch", Dock = DockStyle.Fill, MaxLength = 300 };
    readonly string[] values;
    internal HashSet<string> ResultValues { get; private set; }
    internal string SearchText { get; private set; }

    internal GeneralColumnFilterDialog(string title, IEnumerable<string> choices, IEnumerable<string> selected = null, string search = "", bool withSearch = false) : base(title) {
        ClientSize = new Size(540, withSearch ? 570 : 490);
        values = (choices ?? Enumerable.Empty<string>()).Where(value => value != null).Distinct(StringComparer.Ordinal).ToArray();
        var initial = new HashSet<string>(selected ?? values, StringComparer.Ordinal);
        foreach (string value in values) list.Items.Add(String.IsNullOrEmpty(value) ? GeneralStyle.T("(Sem valor)", "(Empty)") : value, initial.Contains(value));
        this.search.Text = search ?? "";
        this.search.AccessibleName = GeneralStyle.T("Contém", "Contains");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 8, 20, 20), ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (withSearch) {
            var searchLayout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 12) };
            searchLayout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Contém", "Contains")));
            searchLayout.Controls.Add(GeneralTableDialogLayout.Input(this.search));
            layout.Controls.Add(searchLayout, 0, 0);
        }
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T("Valores", "Values")), 0, 1);
        var field = new GeneralSurface { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = Padding.Empty };
        field.Controls.Add(new GeneralScrollView(list)); layout.Controls.Add(field, 0, 2);
        var selection = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 8, 0, 0) };
        var all = GeneralStyle.Button(GeneralStyle.T("Selecionar todos", "Select all"), () => CheckAll(true)); all.Name = "SelectAllValues";
        var clear = GeneralStyle.Button(GeneralStyle.T("Limpar", "Clear"), () => CheckAll(false)); clear.Name = "ClearValues";
        selection.Controls.Add(all); selection.Controls.Add(clear); layout.Controls.Add(selection, 0, 3);
        var commands = GeneralTableDialogLayout.Commands();
        var apply = GeneralStyle.Button(GeneralStyle.T("Aplicar", "Apply"), () => {
            ResultValues = new HashSet<string>(list.CheckedIndices.Cast<int>().Select(index => values[index]), StringComparer.Ordinal);
            SearchText = this.search.Text.Trim(); Complete(DialogResult.OK);
        }, true);
        apply.Name = "ApplyColumnFilter";
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => Complete(DialogResult.Cancel)); cancel.Name = "CancelColumnFilter";
        commands.Controls.Add(apply); commands.Controls.Add(cancel); layout.Controls.Add(commands, 0, 4); Content.Controls.Add(layout);
        AcceptButton = apply; CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark); this.search.BorderStyle = BorderStyle.None; list.BorderStyle = BorderStyle.None;
    }

    void CheckAll(bool value) { for (int index = 0; index < list.Items.Count; index++) list.SetItemChecked(index, value); }
}

internal sealed class GeneralPresetActionsDialog : GeneralDialog {
    internal string ActionId { get; private set; }
    internal GeneralPresetActionsDialog(string title) : base(title) {
        ClientSize = new Size(500, 245);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 8, 20, 20), ColumnCount = 1, RowCount = 3 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var create = GeneralStyle.Button(GeneralStyle.T("Novo preset", "New preset"), () => Choose("new")); create.Name = "NewPreset"; create.Dock = DockStyle.Fill; create.Margin = new Padding(0, 0, 0, 8);
        var update = GeneralStyle.Button(GeneralStyle.T("Atualizar preset", "Update preset"), () => Choose("update"), true); update.Name = "UpdatePreset"; update.Dock = DockStyle.Fill; update.Margin = Padding.Empty;
        layout.Controls.Add(create, 0, 0); layout.Controls.Add(update, 0, 1);
        var commands = GeneralTableDialogLayout.Commands();
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => Complete(DialogResult.Cancel)); cancel.Name = "CancelPresetActions";
        commands.Controls.Add(cancel); layout.Controls.Add(commands, 0, 2); Content.Controls.Add(layout);
        CancelButton = cancel; GeneralStyle.Apply(this, GeneralStyle.Dark);
    }
    void Choose(string id) { ActionId = id; Complete(DialogResult.OK); }
}

internal sealed class GeneralModAlternativeDialog : GeneralDialog {
    internal string SelectedId { get; private set; }

    internal GeneralModAlternativeDialog(IReadOnlyList<GeneralModEntry> entries)
        : base(GeneralStyle.T("Escolher uma versão","Choose one version")) {
        Name="ModAlternativeChoice";ClientSize=new Size(680,445);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20,8,20,20)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(GeneralModEditor.LabelFor(GeneralStyle.T(
            "Estes mods são alternativas. Escolha uma versão para esta instalação.",
            "These mods are alternatives. Choose one version for this installation.")),0,0);
        var options=new GeneralPicker{Name="AlternativeChoices",Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
        foreach(var entry in entries)options.Items.Add(entry.Name);
        layout.Controls.Add(options,0,1);
        var text=new TextBox{Name="AlternativeDetails",Multiline=true,ReadOnly=true,WordWrap=true,Dock=DockStyle.Fill,BorderStyle=BorderStyle.None};
        layout.Controls.Add(new GeneralScrollView(text),0,2);
        var buttons=GeneralTableDialogLayout.Commands();
        var use=GeneralStyle.Button(GeneralStyle.T("Usar esta versão","Use this version"),()=>{
            SelectedId=entries[options.SelectedIndex].Id;Complete(DialogResult.OK);
        },true);
        use.Name="UseAlternative";use.Enabled=false;
        var skip=GeneralStyle.Button(GeneralStyle.T("Pular grupo","Skip group"),()=>{SelectedId="";Complete(DialogResult.OK);});
        var cancel=GeneralStyle.Button(GeneralStyle.T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));
        options.SelectedIndexChanged+=(s,e)=>{use.Enabled=options.SelectedIndex>=0;};
        text.Text=String.Join("\r\n\r\n",entries.Select(entry=>entry.Name+(String.IsNullOrWhiteSpace(entry.Description)?"":"\r\n"+entry.Description)));
        buttons.Controls.Add(use);buttons.Controls.Add(skip);buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons,0,3);Content.Controls.Add(layout);CancelButton=cancel;
        GeneralStyle.Apply(this,GeneralStyle.Dark);text.BorderStyle=BorderStyle.None;
    }
}
internal static class GeneralTableDialogLayout {
    internal static GeneralSurface Input(TextBox text) {
        var field = new GeneralSurface { Dock = DockStyle.Top, Height = 44, MinimumSize = new Size(120, 44), MaximumSize = new Size(0, 44), Padding = new Padding(14, 10, 14, 0), Margin = Padding.Empty };
        field.Controls.Add(text); return field;
    }
    internal static FlowLayoutPanel Commands() {
        return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, FlowDirection = FlowDirection.RightToLeft, Margin = new Padding(0, 14, 0, 0) };
    }
}

internal sealed class GeneralInstallConfirmation : GeneralDialog {
    internal GeneralInstallConfirmation(string destination, int queued, IReadOnlyList<string> downloads)
        : base(GeneralStyle.T("Confirmar instalação", "Confirm installation")) {
        ClientSize = new Size(660, downloads.Count == 0 ? 250 : 310 + Math.Min(6, downloads.Count - 1) * 34);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Padding = new Padding(20, 8, 20, 20)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var folder = new Label {
            Name = "InstallDestination", Text = destination, Dock = DockStyle.Top, AutoSize = true,
            AccessibleName = GeneralStyle.T("Pasta de instalação", "Installation folder"),
            MaximumSize = new Size(600, 0), Margin = new Padding(0, 0, 0, 10), Font = GeneralStyle.Font(11)
        };
        var summary = new Label {
            Name = "InstallSummary", Text = queued + GeneralStyle.T(" na fila · ", " queued · ") +
                downloads.Count + GeneralStyle.T(" com download pendente", " with pending downloads"),
            Dock = DockStyle.Top, AutoSize = true, Font = GeneralStyle.Font(11, true),
            Margin = new Padding(0, 0, 0, 10)
        };
        layout.Controls.Add(folder, 0, 0);
        layout.Controls.Add(summary, 0, 1);

        if (downloads.Count > 0) {
            var table = new GeneralModWindow.ModListGrid {
                Name = "PendingDownloads", AccessibleName = GeneralStyle.T("Mods com download pendente", "Mods with pending downloads"),
                Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, RowHeadersVisible = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false, AllowUserToResizeColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = GeneralStyle.Surface,
                BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 34, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            table.DefaultCellStyle = new DataGridViewCellStyle {
                BackColor = GeneralStyle.Surface, ForeColor = GeneralStyle.Foreground, SelectionBackColor = GeneralStyle.Selection,
                SelectionForeColor = GeneralStyle.Foreground, Font = GeneralStyle.Font(11), Padding = new Padding(8, 0, 8, 0)
            };
            table.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                BackColor = GeneralStyle.Surface, ForeColor = GeneralStyle.Muted, Font = GeneralStyle.Font(11, true), Padding = new Padding(8, 0, 8, 0)
            };
            table.RowTemplate.Height = 34;
            table.Columns.Add(new DataGridViewTextBoxColumn {
                HeaderText = GeneralStyle.T("Mod", "Mod"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable
            });
            foreach (string name in downloads) {
                int row = table.Rows.Add(name);
                table.Rows[row].Cells[0].ToolTipText = name;
            }
            var surface = new GeneralSurface { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = Padding.Empty };
            surface.Controls.Add(new GeneralScrollView(table));
            layout.Controls.Add(surface, 0, 2);
        }

        var actions = GeneralTableDialogLayout.Commands();
        var proceed = GeneralStyle.Button(GeneralStyle.T("Continuar", "Continue"), () => Complete(DialogResult.OK), true);
        proceed.Name = "ConfirmPendingInstall";
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => Complete(DialogResult.Cancel));
        cancel.Name = "CancelPendingInstall";
        actions.Controls.Add(proceed);
        actions.Controls.Add(cancel);
        layout.Controls.Add(actions, 0, 3);
        Content.Controls.Add(layout);
        AcceptButton = proceed;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
    }
}
