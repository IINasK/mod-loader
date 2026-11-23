using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralNameDialog : GeneralDialog {
    readonly TextBox input = new TextBox { Name = "NameInput", MaxLength = 120, Dock = DockStyle.Fill };
    internal string ResultName { get; private set; }
    internal bool SingleSelection { get; private set; }

    internal GeneralNameDialog(string title, string initialName,string description=null,string fieldLabel=null,int maxLength=120,
        bool chooseSelectionMode=false) : base(title) {
        ClientSize = new Size(500,(description==null?230:300)+(chooseSelectionMode?82:0));
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 8, 20, 20),
            ColumnCount = 1, RowCount = 3+(description==null?0:1)+(chooseSelectionMode?2:0) };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        input.MaxLength=maxLength;
        layout.Controls.Add(GeneralModEditor.LabelFor(fieldLabel??GeneralStyle.T("Nome *", "Name *")), 0, 0);
        input.AccessibleName = fieldLabel??GeneralStyle.T("Nome", "Name");
        layout.Controls.Add(GeneralTableDialogLayout.Input(input), 0, 1);
        int row=2;
        GeneralPicker mode=null;
        if(chooseSelectionMode){
            string selection=GeneralStyle.T("Seleção na pasta","Selection in folder");
            var label=GeneralModEditor.LabelFor(selection);
            label.Margin=new Padding(0,10,0,6);
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.Controls.Add(label,0,row++);
            mode=new GeneralPicker{Name="FolderSelectionMode",Dock=DockStyle.Fill,Margin=Padding.Empty,AccessibleName=selection};
            mode.Items.AddRange(new object[]{GeneralStyle.T("Vários itens","Multiple items"),GeneralStyle.T("Um item por vez","One item at a time")});
            mode.SelectedIndex=0;
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.Controls.Add(mode,0,row++);
        }
        if(description!=null){
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            layout.Controls.Add(new Label{Name="NameDescription",Text=description,Dock=DockStyle.Fill,
                Font=GeneralStyle.Font(10.5f),Margin=new Padding(0,8,0,0)},0,row++);
        }
        var commands = GeneralTableDialogLayout.Commands();
        var save = GeneralStyle.Button(GeneralStyle.T("Salvar", "Save"), () => {
            ResultName=input.Text.Trim();SingleSelection=mode!=null&&mode.SelectedIndex==1;
            Complete(DialogResult.OK);
        }, true);
        save.Name = "SaveName";
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => Complete(DialogResult.Cancel));
        cancel.Name = "CancelName";
        commands.Controls.Add(save); commands.Controls.Add(cancel);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(commands,0,row);Content.Controls.Add(layout);
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
    internal bool? ReverseOrder { get; private set; }

    internal GeneralColumnFilterDialog(string title, IEnumerable<string> choices, IEnumerable<string> selected = null, string search = "", bool withSearch = false, string[] sorts = null, bool withValues = true) : base(title) {
        ClientSize = new Size(540, withSearch ? 570 : !withValues ? 200 : sorts==null ? 490 : 530);
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
        if(sorts!=null){
            var sortActions=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=false,Margin=new Padding(0,0,0,10)};
            for(int index=0;index<sorts.Length;index++){
                bool reverse=index!=0;
                var button=GeneralStyle.Button(sorts[index],()=>SaveSelection(reverse));
                button.Name=reverse?"ReverseOrder":"DefaultOrder";
                sortActions.Controls.Add(button);
            }
            layout.Controls.Add(sortActions,0,0);
        }
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
        var apply = GeneralStyle.Button(GeneralStyle.T("Aplicar", "Apply"), () => SaveSelection(null), true);
        apply.Name = "ApplyColumnFilter";
        var cancel = GeneralStyle.Button(GeneralStyle.T("Cancelar", "Cancel"), () => Complete(DialogResult.Cancel)); cancel.Name = "CancelColumnFilter";
        commands.Controls.Add(apply); commands.Controls.Add(cancel); layout.Controls.Add(commands, 0, 4); Content.Controls.Add(layout);
        if(!withValues){
            foreach(int row in new[]{1,2,3})layout.GetControlFromPosition(0,row).Visible=false;
            apply.Visible=false;
        }else AcceptButton = apply;
        CancelButton = cancel;
        GeneralStyle.Apply(this, GeneralStyle.Dark); this.search.BorderStyle = BorderStyle.None; list.BorderStyle = BorderStyle.None;
    }

    void SaveSelection(bool? reverse){
        ReverseOrder=reverse;
        ResultValues=new HashSet<string>(list.CheckedIndices.Cast<int>().Select(index=>values[index]),StringComparer.Ordinal);
        SearchText=search.Text.Trim();
        Complete(DialogResult.OK);
    }

    void CheckAll(bool value) { for (int index = 0; index < list.Items.Count; index++) list.SetItemChecked(index, value); }
}

internal sealed class GeneralModAlternativeDialog : GeneralDialog {
    internal string SelectedId { get; private set; }

    internal GeneralModAlternativeDialog(IReadOnlyList<GeneralModEntry> entries,string title=null,int suggested=-1)
        : base(title??GeneralStyle.T("Escolher uma versão","Choose one version")) {
        RequiresDecision=true;Name="ModAlternativeChoice";ClientSize=new Size(680,445);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20,8,20,20)};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(GeneralModEditor.LabelFor(title==null?GeneralStyle.T(
            "Estes mods são alternativas. Escolha uma versão para esta instalação.",
            "These mods are alternatives. Choose one version for this installation."):GeneralStyle.T(
            "Escolha o corpo. A troca desabilita a versão ativa e seus complementos incompatíveis.",
            "Choose the body. Switching disables the active version and its incompatible add-ons.")),0,0);
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
        var skip=GeneralStyle.Button(title==null?GeneralStyle.T("Pular grupo","Skip group"):GeneralStyle.T("Pular mod","Skip mod"),()=>{SelectedId="";Complete(DialogResult.OK);});
        var cancel=GeneralStyle.Button(GeneralStyle.T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));
        options.SelectedIndexChanged+=(s,e)=>{use.Enabled=options.SelectedIndex>=0;};
        options.SelectedIndex=suggested;
        text.Text=String.Join("\r\n\r\n",entries.Select(entry=>entry.Name+(String.IsNullOrWhiteSpace(entry.Description)?"":"\r\n"+entry.Description)));
        buttons.Controls.Add(use);buttons.Controls.Add(skip);buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons,0,3);Content.Controls.Add(layout);CancelButton=cancel;
        GeneralStyle.Apply(this,GeneralStyle.Dark);text.BorderStyle=BorderStyle.None;
    }
}
internal static class GeneralTableDialogLayout {
    internal static GeneralModWindow.ModListGrid ReadOnlyTable(string name, string accessibleName) {
        var table = new GeneralModWindow.ModListGrid {
            Name = name, AccessibleName = accessibleName,
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
        return table;
    }
    internal static GeneralModWindow.ModListGrid SingleColumnTable(string name, string accessibleName, string heading) {
        var table=ReadOnlyTable(name,accessibleName);
        table.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = heading, AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable
        });
        return table;
    }
    internal static GeneralSurface TableSurface(DataGridView table) {
        var surface = new GeneralSurface { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = Padding.Empty };
        surface.Controls.Add(new GeneralScrollView(table));
        return surface;
    }
    internal static GeneralSurface Input(TextBox text) {
        var field = new GeneralSurface { Dock = DockStyle.Top, Height = 44, MinimumSize = new Size(120,44),
            MaximumSize = new Size(0,44), Padding = new Padding(14,4,14,4), Margin = Padding.Empty };
        text.Dock=DockStyle.None;
        field.Controls.Add(text);
        field.Layout+=(s,e)=>{
            var body=field.DisplayRectangle;
            text.SetBounds(body.Left,Math.Max(body.Top,(field.ClientSize.Height-text.PreferredHeight)/2),
                body.Width,text.PreferredHeight);
        };
        return field;
    }
    internal static FlowLayoutPanel Commands() {
        var actions=new FlowLayoutPanel{Name="DialogActions",Dock=DockStyle.Fill,AutoSize=true,WrapContents=false,
            FlowDirection=FlowDirection.RightToLeft,Margin=new Padding(0,14,0,0)};
        actions.ControlAdded+=(s,e)=>e.Control.Margin=new Padding(6,0,0,0);
        return actions;
    }
}

internal sealed class GeneralInstallConfirmation : GeneralDialog {
    internal sealed class Choice {
        internal readonly string Name;
        internal readonly bool PendingDownload;
        internal readonly int[] Required;

        internal Choice(string name,bool pendingDownload,params int[] required){
            Name=name;PendingDownload=pendingDownload;Required=required;
        }
    }

    readonly IReadOnlyList<Choice> choices;
    readonly DataGridView table;
    readonly CheckBox selectAll;
    readonly Label summary;
    readonly Button proceed, defer;
    readonly CheckBox backup;
    readonly HashSet<int> deferrableUpdates;
    internal bool CreateBackup {get{return backup.Checked;}}
    int[] confirmed;
    internal int[] SelectedIndices {
        get {return confirmed??table.Rows.Cast<DataGridViewRow>().Where(row=>(bool)row.Cells[0].Value).Select(row=>row.Index).ToArray();}
    }

    internal GeneralInstallConfirmation(IReadOnlyList<Choice> choices,bool offerBackup=false,IEnumerable<int> deferrableUpdates=null)
        : base(offerBackup?GeneralStyle.T("Você deseja atualizar?","Update now?"):
            GeneralStyle.T("Confirmar instalação","Confirm installation")) {
        this.choices=choices;
        this.deferrableUpdates=new HashSet<int>(deferrableUpdates??Enumerable.Empty<int>());
        RequiresDecision=true;
        ClientSize=new Size(660,326+Math.Min(6,Math.Max(0,choices.Count-1))*34);
        var layout=new TableLayoutPanel {
            Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(20,8,20,20)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        summary=new Label {
            Name="InstallSummary",Dock=DockStyle.Top,AutoSize=true,Font=GeneralStyle.Font(11,true),
            Margin=new Padding(0,0,0,6)
        };
        selectAll=new CheckBox {
            Name="ToggleInstallSelection",AutoSize=true,MinimumSize=new Size(0,30),
            Margin=new Padding(0,0,0,8),AutoCheck=false,Font=GeneralStyle.Font(11)
        };
        selectAll.Click+=(s,e)=>{
            bool mark=SelectedIndices.Length!=choices.Count;
            foreach(DataGridViewRow row in table.Rows)row.Cells[0].Value=mark;
            RefreshSelection();
        };
        layout.Controls.Add(summary,0,0);
        layout.Controls.Add(selectAll,0,1);

        table=GeneralTableDialogLayout.SingleColumnTable("InstallChoices",
            GeneralStyle.T("Mods da instalação","Mods to install"),GeneralStyle.T("Mod","Mod"));
        table.Columns.Insert(0,new DataGridViewCheckBoxColumn {
            Name="InstallSelected",HeaderText="",Width=36,SortMode=DataGridViewColumnSortMode.NotSortable,
            DefaultCellStyle=new DataGridViewCellStyle{Alignment=DataGridViewContentAlignment.MiddleCenter,Padding=Padding.Empty}
        });
        foreach(var choice in choices){
            int index=table.Rows.Add(true,choice.Name);
            table.Rows[index].Cells[1].ToolTipText=choice.Name;
        }
        table.CellClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=0)ToggleChoice(e.RowIndex);};
        table.KeyDown+=(s,e)=>{
            if(e.KeyCode!=Keys.Space||table.CurrentRow==null)return;
            ToggleChoice(table.CurrentRow.Index);e.SuppressKeyPress=true;
        };
        layout.Controls.Add(GeneralTableDialogLayout.TableSurface(table),0,2);

        var actions=GeneralTableDialogLayout.Commands();
        proceed=GeneralStyle.Button(offerBackup?GeneralStyle.T("Sim","Yes"):GeneralStyle.T("Continuar","Continue"),()=>{
            confirmed=SelectedIndices;
            if(confirmed.Length>0)Complete(DialogResult.OK);
        },true);
        proceed.Name="ConfirmPendingInstall";
        var cancel=GeneralStyle.Button(offerBackup?GeneralStyle.T("Não","No"):GeneralStyle.T("Cancelar","Cancel"),
            ()=>Complete(DialogResult.Cancel));
        cancel.Name="CancelPendingInstall";
        actions.Controls.Add(proceed);
        actions.Controls.Add(cancel);
        if(this.deferrableUpdates.Count>0){
            defer=GeneralStyle.Button(GeneralStyle.T("Não, não perguntar novamente","No, don't ask again"),()=>{
                confirmed=SelectedIndices.Where(this.deferrableUpdates.Contains).ToArray();
                if(confirmed.Length>0)Complete(DialogResult.Ignore);
            });
            defer.Name="DeferSelectedUpdates";
            defer.AccessibleDescription=GeneralStyle.T("As atualizações marcadas ficarão disponíveis nas opções de cada mod.",
                "The selected updates will remain available in each mod's options.");
            actions.Controls.Add(defer);
        }
        backup=new CheckBox{Name="BackupUpdateSelection",Text=GeneralStyle.T("Criar backup","Create backup"),
            Checked=offerBackup,Visible=offerBackup,AutoSize=true,Dock=DockStyle.Fill,UseMnemonic=false,Margin=new Padding(0,14,0,0)};
        var footer=new TableLayoutPanel{ColumnCount=2,AutoSize=true,Dock=DockStyle.Fill,Margin=Padding.Empty};
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.Controls.Add(backup,0,0);footer.Controls.Add(actions,1,0);
        layout.Controls.Add(footer,0,3);
        Content.Controls.Add(layout);
        AcceptButton=proceed;
        CancelButton=cancel;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
        RefreshSelection();
    }

    void ToggleChoice(int index){
        bool mark=!(bool)table.Rows[index].Cells[0].Value;
        var changed=new HashSet<int>{index};
        var pending=new Queue<int>();
        pending.Enqueue(index);
        while(pending.Count>0){
            int item=pending.Dequeue();
            table.Rows[item].Cells[0].Value=mark;
            var related=mark?choices[item].Required:Enumerable.Range(0,choices.Count).Where(i=>choices[i].Required.Contains(item));
            foreach(int next in related)
                if(changed.Add(next))pending.Enqueue(next);
        }
        RefreshSelection();
    }

    void RefreshSelection(){
        var selected=SelectedIndices;
        bool all=choices.Count>0&&selected.Length==choices.Count;
        selectAll.Enabled=choices.Count>0;
        selectAll.CheckState=all?CheckState.Checked:selected.Length==0?CheckState.Unchecked:CheckState.Indeterminate;
        selectAll.Text=all?GeneralStyle.T("Desselecionar tudo","Deselect all"):GeneralStyle.T("Selecionar tudo","Select all");
        summary.Text=selected.Length+GeneralStyle.T(" na fila · "," queued · ")+
            selected.Count(index=>choices[index].PendingDownload)+GeneralStyle.T(" com download pendente"," with pending downloads");
        proceed.Enabled=selected.Length>0;
        if(defer!=null)defer.Enabled=selected.Any(deferrableUpdates.Contains);
    }
}
