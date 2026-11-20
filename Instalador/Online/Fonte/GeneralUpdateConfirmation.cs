using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed class GeneralUpdateConfirmation : GeneralDialog {
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

    internal GeneralUpdateConfirmation(IReadOnlyList<Choice> choices,bool offerBackup,IEnumerable<int> deferrableUpdates=null)
        : base(GeneralStyle.T("Você deseja atualizar?","Update now?")) {
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
        proceed=GeneralStyle.Button(GeneralStyle.T("Sim","Yes"),()=>{
            confirmed=SelectedIndices;
            if(confirmed.Length>0)Complete(DialogResult.OK);
        },true);
        proceed.Name="ConfirmPendingInstall";
        var cancel=GeneralStyle.Button(GeneralStyle.T("Não","No"),
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

    internal Task<DialogResult> ShowForUpdate(Form owner){
        var result=new TaskCompletionSource<DialogResult>();
        FormClosed+=(s,e)=>result.TrySetResult(DialogResult);
        GeneralAttention.Play();
        Show(owner);
        return result.Task;
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
