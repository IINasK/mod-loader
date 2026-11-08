using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed class GeneralSyncReviewDialog : GeneralDialog {
    readonly CheckedListBox choices = new CheckedListBox {
        Name = "SyncExtraMods", Dock = DockStyle.Fill, CheckOnClick = true, IntegralHeight = false
    };
    internal int[] SelectedExtras { get { return choices.CheckedIndices.Cast<int>().ToArray(); } }

    internal GeneralSyncReviewDialog(P2PSyncComparison comparison)
        : base(T("Mods extras neste PC", "Extra mods on this PC")) {
        Name = "SyncExtraReview";
        ClientSize = new Size(720, 450);
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(24, 6, 24, 20), ColumnCount = 1, RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        choices.Items.AddRange(comparison.Extras.Cast<object>().ToArray());
        var selectAll = GeneralStyle.Button(T("Selecionar todos", "Select all"), () => {
            bool check = choices.CheckedItems.Count < choices.Items.Count;
            choices.BeginUpdate();
            try { for (int index = 0; index < choices.Items.Count; index++) choices.SetItemChecked(index, check); }
            finally { choices.EndUpdate(); }
        });
        selectAll.Name = "SelectAllSyncExtras";
        selectAll.Anchor = AnchorStyles.Left;
        selectAll.Margin = new Padding(0, 0, 0, 8);
        selectAll.Visible = choices.Items.Count > 0;
        layout.Controls.Add(selectAll, 0, 0);
        var surface = new GeneralSurface { Dock = DockStyle.Fill, Padding = new Padding(12), Margin = Padding.Empty };
        if (choices.Items.Count > 0) surface.Controls.Add(new GeneralScrollView(choices));
        else surface.Controls.Add(new Label {
            Text = T("Nenhum mod extra neste PC.", "No extra mods on this PC."), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter
        });
        layout.Controls.Add(surface, 0, 1);
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
        choices.ItemCheck += (s, e) => {
            int count = choices.CheckedItems.Count + (e.NewValue == CheckState.Checked ? 1 : -1);
            remove.Enabled = count > 0;
            selectAll.Text = count == choices.Items.Count ? T("Desmarcar todos", "Clear all") : T("Selecionar todos", "Select all");
        };
        commands.Controls.Add(remove);
        commands.Controls.Add(keep);
        layout.Controls.Add(commands, 0, 3);
        Content.Controls.Add(layout);
        GeneralStyle.Apply(this, GeneralStyle.Dark);
        choices.BorderStyle = BorderStyle.None;
    }

    void ConfirmRemoval() {
        int count = choices.CheckedItems.Count;
        if (count == 0) return;
        using (var dialog = new GeneralDialog(T("Confirmar remoção", "Confirm removal")) {
            Name = "ConfirmSyncRemoval", ClientSize = new Size(525, 210)
        }) {
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, Padding = new Padding(22, 8, 22, 16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label {
                Text = String.Format(T("Atenção: {0} mod(s) selecionado(s) serão removidos do jogo.",
                    "Warning: {0} selected mod(s) will be removed from the game."), count), Dock = DockStyle.Fill
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
    internal GeneralSyncDifferencesDialog(IReadOnlyList<P2PSyncDifference> differences)
        : base(T("Diferenças fora do pacote", "Differences outside the pack")) {
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
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Mod", Width = 176 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = T("Arquivo ou pasta", "File or folder"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
            foreach (var difference in differences.OrderBy(item => item.Kind).ThenBy(item => item.Mod).ThenBy(item => item.Path)) {
                string status = difference.Kind == "kept" ? T("Extra mantido", "Extra kept")
                    : difference.Kind == "missing" ? T("Ausente neste PC", "Missing on this PC") : T("Arquivo diferente", "Different file");
                int row = grid.Rows.Add(status, difference.Mod ?? "—", difference.Path);
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
