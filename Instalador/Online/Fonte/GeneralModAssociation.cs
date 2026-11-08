using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    TaskCompletionSource<GeneralModEntry> associationChoice;
    string associationSourceId;

    void ConfirmAlternative() {
        var entry = FocusedEntry();
        if (entry != null && entry.Id != associationSourceId && grid.CurrentRow.Selected)
            associationChoice.TrySetResult(entry);
    }

    async Task<GeneralModEntry> PickAlternative(string sourceId, GeneralModEntry previous) {
        var oldEntry = FocusedEntry();
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
        var task = new TaskCompletionSource<GeneralModEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        associationChoice = task;
        associationSourceId = sourceId;
        using (var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(14, 4, 14, 4) }) {
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bar.Controls.Add(new Label {
                Text = T("Selecione o mod alternativo", "Select the alternative mod"), Dock = DockStyle.Fill,
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
            EventHandler selection = (s, e) => confirm.Enabled = FocusedEntry() != null && grid.CurrentRow.Selected;
            try {
                GeneralStyle.Apply(bar, GeneralStyle.Dark);
                SetListExpanded(true);
                presetControls.Visible = false;
                title.Controls.Add(bar);
                bar.BringToFront();
                tools.Visible = true;
                mainLayout.RowStyles[4].SizeType = SizeType.AutoSize;
                grid.Columns["Mark"].Visible = false;
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
                return await task.Task;
            } finally {
                associationChoice = null;
                associationSourceId = null;
                if (!IsDisposed) {
                    grid.SelectionChanged -= selection;
                    grid.CurrentCellChanged -= selection;
                    grid.ReadOnly = oldReadOnly;
                    grid.Columns["Mark"].Visible = oldMarkVisible;
                    grid.ContextMenuStrip = oldMenu;
                    title.Controls.Remove(bar);
                    presetControls.Visible = true;
                    SetAssociationSearch(oldSearch);
                    var row = oldEntry == null ? null : ModRows().FirstOrDefault(item => Equals(item.Tag, oldEntry.Id));
                    grid.CurrentCell = row == null ? null : row.Cells["Name"];
                    if (oldScroll >= 0 && oldScroll < grid.RowCount)
                        grid.FirstDisplayedScrollingRowIndex = oldScroll;
                    SetListExpanded(oldExpanded);
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
