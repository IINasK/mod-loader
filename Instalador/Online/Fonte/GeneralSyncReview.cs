using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralSyncReviewDialog : GeneralDialog {
    sealed class Choice {
        internal int Index;
        internal P2PSyncExtra Extra;
        public override string ToString(){return Extra.ToString();}
    }
    readonly List<CheckedListBox> sections=new List<CheckedListBox>();
    internal int[] SelectedExtras {get{return sections.SelectMany(list=>list.CheckedItems.Cast<Choice>())
        .Where(row=>row.Extra.Proven).Select(row=>row.Index).OrderBy(index=>index).ToArray();}}

    internal GeneralSyncReviewDialog(P2PSyncComparison comparison)
        : base(T("Mods extras neste PC", "Extra mods on this PC")) {
        RequiresDecision = true;
        Name = "SyncExtraReview";
        ClientSize = new Size(800, 550);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(24, 6, 24, 20), ColumnCount = 1, RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (comparison.HostOutsideDifferences.Length > 0)
            layout.Controls.Add(new Label {
                Text = String.Format(T("{0} diferenças fora do pacote", "{0} differences outside the pack"), comparison.HostOutsideDifferences.Length),
                AutoSize = true, Font = GeneralStyle.Font(10.5f), Margin = new Padding(0, 8, 0, 0)
            }, 0, 2);

        var commands = GeneralTableDialogLayout.Commands();
        var keep = GeneralStyle.Button(T("Manter todos", "Keep all"), () => Complete(DialogResult.No));
        keep.Name = "KeepAllSyncExtras";
        var remove = GeneralStyle.Button(T("Remover selecionados", "Remove selected"), ConfirmRemoval, true);
        remove.Name = "RemoveSelectedSyncExtras";
        ((GeneralButton)remove).Destructive = true;
        remove.Enabled = false;
        var choices=comparison.Extras.Select((extra,index)=>new Choice{Extra=extra,Index=index}).ToArray();
        layout.Controls.Add(BuildSection(T("Desinstalar mods do Mod Loader","Uninstall Mod Loader mods"),
            choices.Where(row=>row.Extra.Managed).ToArray(),remove,true),0,0);
        layout.Controls.Add(BuildSection(T("Remover mods e pastas externos","Remove external mods and folders"),
            choices.Where(row=>!row.Extra.Managed).ToArray(),remove,false),0,1);
        commands.Controls.Add(remove);
        commands.Controls.Add(keep);
        layout.Controls.Add(commands, 0, 3);
        Content.Controls.Add(layout);
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        foreach(var list in sections)list.BorderStyle=BorderStyle.None;
    }

    Control BuildSection(string title,Choice[] rows,Button remove,bool managed){
        var section=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Margin=new Padding(0,0,0,8)};
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        section.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        section.Controls.Add(new Label{Text=title,AutoSize=true,Font=GeneralStyle.Font(11,true)},0,0);
        var surface=new GeneralSurface{Dock=DockStyle.Fill,Padding=new Padding(10),Margin=Padding.Empty};
        section.Controls.Add(surface,0,1);
        var list=new CheckedListBox{ Name=managed?"SyncExtraMods":"SyncExternalFiles",Dock=DockStyle.Fill,
            CheckOnClick=true,IntegralHeight=false,AccessibleName=title};
        sections.Add(list);
        list.Items.AddRange(rows.Cast<object>().ToArray());
        if(rows.Length==0)surface.Controls.Add(new Label{Text=T("Nenhum item","No items"),Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleCenter});
        else surface.Controls.Add(new GeneralScrollView(list));
        Button all=null;
        int selectable=rows.Count(row=>row.Extra.Proven);
        if(managed&&selectable>0){
            all=GeneralStyle.Button(T("Selecionar mods ML","Select Mod Loader mods"),()=>{
                bool check=list.CheckedItems.Count<selectable;
                for(int i=0;i<rows.Length;i++)if(rows[i].Extra.Proven)list.SetItemChecked(i,check);
            });
            all.Name="SelectAllSyncExtras";
            section.Controls.Add(all,0,2);
        }else if(!managed&&rows.Length>0){
            section.Controls.Add(new Label{Text=T("Marque cada item conferido. Itens de origem desconhecida ficam protegidos.",
                "Check verified items individually. Items of unknown origin are protected."),AutoSize=true},0,2);
        }
        list.ItemCheck+=(s,e)=>{
            if(!rows[e.Index].Extra.Proven){e.NewValue=CheckState.Unchecked;return;}
            int delta=(e.NewValue==CheckState.Checked?1:0)-(e.CurrentValue==CheckState.Checked?1:0);
            remove.Enabled=SelectedExtras.Length+delta>0;
            if(all!=null)all.Text=list.CheckedItems.Count+delta==selectable
                ?T("Desmarcar mods ML","Clear Mod Loader mods"):T("Selecionar mods ML","Select Mod Loader mods");
        };
        return section;
    }

    void ConfirmRemoval() {
        int count = SelectedExtras.Length;
        if (count == 0) return;
        using (var dialog = new GeneralDialog(T("Confirmar remoção", "Confirm removal")) {
            RequiresDecision = true, Name = "ConfirmSyncRemoval", ClientSize = new Size(525, 210)
        }) {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(22, 8, 22, 16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label {
                Text = String.Format(T("Os {0} itens marcados serão retirados do jogo e guardados para restauração.",
                    "{0} checked items will be removed from the game and kept for restoration."), count), Dock = DockStyle.Fill
            }, 0, 0);
            var commands = GeneralTableDialogLayout.Commands();
            var remove = GeneralStyle.Button(T("Confirmar remoção", "Confirm removal"), () => dialog.Complete(DialogResult.Yes), true);
            remove.Name = "ApproveSyncRemoval";
            ((GeneralButton)remove).Destructive = true;
            var cancel = GeneralStyle.Button(T("Voltar", "Back"), () => dialog.Complete(DialogResult.Cancel));
            cancel.Name = "CancelSyncRemoval";
            commands.Controls.Add(remove);
            commands.Controls.Add(cancel);
            layout.Controls.Add(commands, 0, 1);
            dialog.Content.Controls.Add(layout);
            dialog.CancelButton = cancel;
            GeneralStyle.Apply(dialog, GeneralStyle.Dark);
            cancel.Select();
            if (dialog.ShowDialog(this) == DialogResult.Yes) Complete(DialogResult.Yes);
        }
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }
}

internal sealed class GeneralSyncDifferencesDialog : GeneralDialog {
    internal GeneralSyncDifferencesDialog(IReadOnlyList<P2PSyncDifference> differences,bool inventory=false,
        bool showSource=false)
        : base(showSource?T("Detalhes","Details"):
            inventory?T("Arquivos no jogo fora da lista","Game files outside the list"):T("Diferenças fora do pacote", "Differences outside the pack")) {
        ClientSize = new Size(810, 475);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(24, 8, 24, 18)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        if (differences == null || differences.Count == 0)
            layout.Controls.Add(new Label {
                Text = T("Esta transferência guardou só a contagem. Compare os mods novamente para ver os arquivos.",
                    "This transfer only saved the count. Compare mods again to see the files."),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter
            }, 0, 0);
        else {
            var grid = new GeneralModWindow.ModListGrid {
                Name = "SyncDifferenceFiles", Dock = DockStyle.Fill, ReadOnly = true, MultiSelect = false, RowHeadersVisible = false,
                AllowUserToAddRows = false, AllowUserToResizeRows = false, AllowUserToResizeColumns = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = GeneralStyle.Surface,
                BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.None,
                ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None, EnableHeadersVisualStyles = false,
                ColumnHeadersHeight = 38, ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle {
                BackColor = GeneralStyle.Surface, ForeColor = GeneralStyle.Foreground, SelectionBackColor = GeneralStyle.Selection,
                SelectionForeColor = GeneralStyle.Foreground, Font = GeneralStyle.Font(11), Padding = new Padding(8, 0, 8, 0)
            };
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle {
                BackColor = GeneralStyle.Surface, ForeColor = GeneralStyle.Muted, Font = GeneralStyle.Font(11, true), Padding = new Padding(8, 0, 8, 0)
            };
            grid.RowTemplate.Height = 36;
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Situação", "Status"), Width = 166 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = inventory?T("Nome encontrado","Detected name"):"Mod", Width = 215 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Arquivo ou pasta", "File or folder"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            if(showSource)grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("ZIP de origem","Source ZIP"), Width = 210 });
            foreach (var difference in differences.OrderBy(item => item.Kind).ThenBy(item => item.Mod).ThenBy(item => item.Path)) {
                string status = difference.Kind == "found" ? T("Encontrado no jogo","Found in game")
                    : difference.Kind == "kept" ? T("Extra mantido", "Extra kept")
                    : difference.Kind == "missing" ? T("Ausente neste PC", "Missing on this PC") : T("Arquivo diferente", "Different file");
                if(inventory){
                    if(difference.VerifiedZip!=null)status=T("Confirmado pelo ZIP","Verified by ZIP");
                    else if(difference.ProvenOwner!=null)status=T("Origem confirmada","Verified origin");
                    else if(difference.LocalInModFolder)status=T("Arquivo local","Local file");
                    else status="?";
                }
                int row = grid.Rows.Add(status, difference.Mod ?? "—", difference.Path);
                grid.Rows[row].Tag=difference;
                if(showSource){
                    grid.Rows[row].Cells[3].Value=difference.VerifiedZip??"—";
                    grid.Rows[row].Cells[3].ToolTipText=difference.VerifiedZip??T("Sem ZIP confirmado","No verified ZIP");
                }
                grid.Rows[row].Cells[1].ToolTipText = difference.Mod ?? "";
                grid.Rows[row].Cells[2].ToolTipText = difference.Path;
            }
            layout.Controls.Add(new GeneralScrollView(grid), 0, 0);
        }
        var commands = GeneralTableDialogLayout.Commands();
        var close = GeneralStyle.Button(T("Fechar", "Close"), () => Complete(DialogResult.OK), true);
        commands.Controls.Add(close);
        layout.Controls.Add(commands, 0, 1);
        Content.Controls.Add(layout);
        AcceptButton = CancelButton = close;
        GeneralStyle.Apply(this, GeneralStyle.Dark);
    }

    static string T(string pt, string en) { return GeneralStyle.T(pt, en); }
}
