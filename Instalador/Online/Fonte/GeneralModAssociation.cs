using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    TaskCompletionSource<GeneralModEntry[]> associationChoice;
    string associationSourceId;
    bool pickingInventoryOwner;
    double inventoryOwnerPulse;
    readonly HashSet<string> inventoryOwnerIds=new HashSet<string>(StringComparer.Ordinal);

    void ConfirmAlternative() {
        if(pickingInventoryOwner){
            if(inventoryOwnerIds.Count>0)associationChoice.TrySetResult(state.List.Mods.Where(mod=>inventoryOwnerIds.Contains(mod.Id)).ToArray());
            return;
        }
        var entry = FocusedEntry();
        if (entry != null && entry.Id != associationSourceId && grid.CurrentRow.Selected)
            associationChoice.TrySetResult(new[]{entry});
    }

    async Task<GeneralModEntry> PickAlternative(string sourceId, GeneralModEntry previous, bool dependency = false) {
        var choices=await PickAssociation(sourceId,previous,dependency,null);
        return choices==null?null:choices.SingleOrDefault();
    }

    void ToggleInventoryOwner(DataGridViewRow row){
        var id=row==null?null:row.Tag as string;
        if(!pickingInventoryOwner||id==null)return;
        if(!inventoryOwnerIds.Add(id))inventoryOwnerIds.Remove(id);
        row.Cells["Mark"].Value=inventoryOwnerIds.Contains(id);
        row.DefaultCellStyle.BackColor=inventoryOwnerIds.Contains(id)?GeneralStyle.Selection:GeneralStyle.Surface;
        grid.InvalidateRow(row.Index);
        RefreshPrimaryAction();
    }

    async Task<GeneralModEntry[]> PickAssociation(string sourceId, GeneralModEntry previous, bool dependency, string inventoryName) {
        bool inventoryOwner=inventoryName!=null;
        var oldEntry = FocusedEntry();
        var oldInventory=FocusedInventoryFile();
        int oldScroll = grid.FirstDisplayedScrollingRowIndex;
        bool oldExpanded = listExpanded;
        bool oldReadOnly = grid.ReadOnly;
        bool oldMarkVisible = grid.Columns["Mark"].Visible;
        string oldSearch = searchBox.Text;
        var oldMenu = grid.ContextMenuStrip;
        var tools = (TableLayoutPanel)mainLayout.GetControlFromPosition(0, 4);
        var title = tools.GetControlFromPosition(0, 0);
        var presetControls = title.Controls[0];
        bool oldToolsVisible = tools.Visible;
        var task = new TaskCompletionSource<GeneralModEntry[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var footer=mainLayout.GetControlFromPosition(0,7);
        associationChoice = task;
        associationSourceId = sourceId;
        pickingInventoryOwner = inventoryOwner;
        using (var pulse = new Timer { Interval = 32 })
        using (var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(14, 4, 14, 4) }) {
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.Controls.Add(new Label {
                Text = inventoryOwner?String.Format(T("Associar {0} aos mods selecionados","Link {0} to the selected mods"),inventoryName):
                    dependency?T("Selecione a dependência","Select the required mod"):T("Selecione o mod alternativo", "Select the alternative mod"),
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, Font = GeneralStyle.Font(13, true), AutoEllipsis = true
            }, 0, 0);
            var cancel = GeneralStyle.Button(T("Cancelar", "Cancel"), () => task.TrySetResult(null));
            cancel.Name = "CancelAlternativeSelection";
            cancel.Margin = new Padding(0, 0, 8, 0);
            var confirm = GeneralStyle.Button("OK", ConfirmAlternative, true);
            confirm.Name = "ConfirmAlternativeSelection";
            confirm.Margin = Padding.Empty;
            confirm.Enabled = false;
            bar.Controls.Add(cancel, 1, 0);
            bar.Controls.Add(confirm, 2, 0);
            cancel.Visible=confirm.Visible=!inventoryOwner;
            EventHandler selection = (s, e) => confirm.Enabled = FocusedEntry() != null && grid.CurrentRow.Selected;
            try {
                GeneralStyle.Apply(bar, GeneralStyle.Dark);
                SetListExpanded(true);
                presetControls.Visible = false;
                title.Controls.Add(bar);
                bar.BringToFront();
                tools.Visible = true;
                mainLayout.RowStyles[4].SizeType = SizeType.AutoSize;
                if(inventoryOwner){
                    footer.Visible=true;
                    mainLayout.RowStyles[7].SizeType=SizeType.AutoSize;
                    ShowModUpdateNotice();
                }
                grid.Columns["Mark"].Visible = inventoryOwner;
                grid.ReadOnly = true;
                grid.ContextMenuStrip = null;
                grid.CurrentCell = null;
                grid.SelectionChanged += selection;
                grid.CurrentCellChanged += selection;
                SetAssociationSearch("");
                var linkedRow = previous == null ? null : ModRows().FirstOrDefault(row => Equals(row.Tag, previous.Id));
                if (linkedRow != null) {
                    grid.CurrentCell = linkedRow.Cells["Name"];
                    linkedRow.Selected = true;
                    grid.FirstDisplayedScrollingRowIndex = linkedRow.Index;
                } else {
                    grid.CurrentCell = null;
                    grid.FirstDisplayedScrollingRowIndex = 0;
                }
                selection(this, EventArgs.Empty);
                grid.Focus();
                if(inventoryOwner&&GeneralStyle.AnimationsEnabled){
                    var elapsed=System.Diagnostics.Stopwatch.StartNew();
                    pulse.Tick+=(s,e)=>{
                        double progress=Math.Min(1,elapsed.Elapsed.TotalMilliseconds/820);
                        inventoryOwnerPulse=Math.Sin(Math.PI*progress);
                        grid.InvalidateColumn(grid.Columns["Name"].Index);
                        if(progress>=1)pulse.Stop();
                    };
                    pulse.Start();
                }
                return await task.Task;
            } finally {
                associationChoice = null;
                associationSourceId = null;
                pickingInventoryOwner = false;
                inventoryOwnerIds.Clear();
                inventoryOwnerPulse = 0;
                if (!IsDisposed) {
                    grid.SelectionChanged -= selection;
                    grid.CurrentCellChanged -= selection;
                    grid.ReadOnly = oldReadOnly;
                    grid.Columns["Mark"].Visible = oldMarkVisible;
                    grid.ContextMenuStrip = oldMenu;
                    title.Controls.Remove(bar);
                    presetControls.Visible = true;
                    SetAssociationSearch(oldSearch);
                    var row=oldEntry!=null?ModRows().FirstOrDefault(item=>Equals(item.Tag,oldEntry.Id)):
                        oldInventory==null?null:grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(item=>item.Tag is P2PSyncDifference&&
                            InventoryPaths.Equals(InventoryLinkStore.Key((P2PSyncDifference)item.Tag),InventoryLinkStore.Key(oldInventory)));
                    if(row!=null&&!row.Visible)row=null;
                    grid.CurrentCell = row == null ? null : row.Cells["Name"];
                    if (oldScroll >= 0 && oldScroll < grid.RowCount && grid.Rows[oldScroll].Visible)
                        grid.FirstDisplayedScrollingRowIndex = oldScroll;
                    SetListExpanded(oldExpanded);
                    if(inventoryOwner)ShowModUpdateNotice();
                    tools.Visible = oldToolsVisible;
                    mainLayout.RowStyles[4].SizeType = oldToolsVisible ? SizeType.AutoSize : SizeType.Absolute;
                    if (!oldToolsVisible) mainLayout.RowStyles[4].Height = 0;
                }
            }
        }
    }

    void SetAssociationSearch(string text) {
        searchQuery = searchBox.Text = text;
        searchTerms = GeneralModSearch.Terms(text);
        DrawRows();
    }
}
