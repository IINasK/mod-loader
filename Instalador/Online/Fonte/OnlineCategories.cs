using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

internal enum ModCategory {
    All, Essentials, Core, Gameplay, Stores, Vehicles, Clothes, Appearance, PhotoMode, Audio, Imported, Other
}

[Flags]
internal enum ClothingFit { All = 0, Feminine = 1, Masculine = 2, Both = Feminine | Masculine }

internal static class ModCategories {
    static readonly Dictionary<string, ModCategory> categories = new Dictionary<string, ModCategory>(StringComparer.Ordinal);
    static readonly Dictionary<string, ClothingFit> clothing = new Dictionary<string, ClothingFit>(StringComparer.Ordinal);

    static ModCategories() {
        Add(ModCategory.Core, "mod-10437 mod-34403 CET redscript ArchiveXL TweakXL Codeware InputLoader ModSettings Audioware Utilities2077 NativeSettings RadioExt mod-5266 mod-6945 mod-2987 mod-28744 mod-7049 mod-9429");
        Add(ModCategory.Essentials, "NightCoop");
        Add(ModCategory.Gameplay, "mod-36378 mod-35886 mod-17811 mod-3486 mod-30316 mod-30304 mod-7299 mod-36802 mod-36203 mod-35667");
        Add(ModCategory.Stores, "mod-14210 mod-4454 mod-4805 mod-11964 mod-5114 mod-14299 mod-8704 mod-20058 mod-8183 mod-13870 mod-18890 mod-12637 mod-6505 mod-6985 mod-27357 mod-27599 mod-16645 mod-37000");
        Add(ModCategory.Vehicles, "mod-11659 mod-10702 mod-11331 mod-12556 mod-8827 mod-13569 mod-8795 mod-8912 mod-12652 mod-8730 mod-11180 mod-24076 mod-12669 mod-22340 mod-33954 mod-29619 mod-22155 mod-14162 mod-10445 mod-17158 mod-13847 mod-35848");
        Add(ModCategory.Appearance, "mod-16697-109145 mod-16697-109148 mod-16697-109147 mod-16697-109146 mod-16697-109144 mod-28570 mod-19187 mod-20763 mod-24547 mod-24626 mod-25348 mod-25807 mod-31886");
        Add(ModCategory.PhotoMode, "mod-35772 mod-35353 mod-7912");

        AddClothes(ClothingFit.Feminine, "mod-10989 mod-11963 mod-15606 mod-15867 mod-5093 mod-11609 mod-7665 mod-7822 mod-18823 mod-11824 mod-24104 mod-8209 mod-26845 mod-32674 mod-33404 mod-15712 mod-17501 mod-18353 mod-18849 mod-21395 mod-23301 mod-15702 mod-7435 mod-22565");
        AddClothes(ClothingFit.Masculine, "mod-7640");
        AddClothes(ClothingFit.Both, "mod-15076 mod-15861 mod-17069 mod-18100 mod-19132 mod-19762 mod-24327 mod-31328 mod-18084 mod-29544 mod-14371 mod-16345 mod-19684 mod-6536 mod-7017 mod-37005 mod-30321 mod-30910 mod-30769 mod-29643 mod-30120 mod-27597");
    }

    static void Add(ModCategory category, string ids) {
        foreach (string id in ids.Split(' ')) categories.Add(id, category);
    }

    static void AddClothes(ClothingFit fit, string ids) {
        Add(ModCategory.Clothes, ids);
        foreach (string id in ids.Split(' ')) clothing.Add(id, fit);
    }

    internal static ModCategory CategoryOf(string id, string profile) {
        ModCategory category;
        if (categories.TryGetValue(id, out category)) return category;
        if (id.StartsWith("pending:", StringComparison.Ordinal)) return ModCategory.Imported;
        if (id.StartsWith("face-", StringComparison.Ordinal) || profile == "Conjunto facial 17331") return ModCategory.Appearance;
        return profile == "Kit inicial" ? ModCategory.Essentials : ModCategory.Other;
    }

    internal static bool Supports(string id, ClothingFit fit) {
        ClothingFit supported;
        return fit == ClothingFit.All || clothing.TryGetValue(id, out supported) && (supported & fit) == fit;
    }
}

public sealed partial class OnlineWindow {
    readonly ComboBox categoryFilter = new ComboBox {
        Name = "CategoryFilter", AccessibleName = "Filtrar por categoria",
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 200
    };
    readonly ComboBox clothingFilter = new ComboBox {
        Name = "ClothingFilter", AccessibleName = "Filtrar roupas por tipo de V",
        DropDownStyle = ComboBoxStyle.DropDownList, Width = 150
    };
    readonly Dictionary<string, ModCategory> rowCategories = new Dictionary<string, ModCategory>(StringComparer.Ordinal);

    void AddCategoryControls(FlowLayoutPanel filters) {
        filters.Controls.Add(new Label { Text = "Categoria", AutoSize = true, Margin = new Padding(0, 7, 6, 0) });
        categoryFilter.Items.AddRange(new object[] {
            "Todas", "Kit principal", "Núcleo e dependências", "Jogabilidade", "Lojas", "Veículos",
            "Roupas e acessórios", "Aparência", "Modo Foto", "Áudio", "Importados", "Outros"
        });
        categoryFilter.SelectedIndex = (int)ModCategory.All;
        filters.Controls.Add(categoryFilter);

        filters.Controls.Add(new Label { Text = "Roupas para", AutoSize = true, Margin = new Padding(14, 7, 6, 0) });
        clothingFilter.Items.AddRange(new object[] { "Todas", "V feminina", "V masculina", "Ambas" });
        clothingFilter.SelectedIndex = (int)ClothingFit.All;
        clothingFilter.Enabled = false;
        filters.Controls.Add(clothingFilter);

        categoryFilter.SelectedIndexChanged += (s, e) => {
            clothingFilter.Enabled = (ModCategory)categoryFilter.SelectedIndex == ModCategory.Clothes;
            ApplyListFilters();
        };
        clothingFilter.SelectedIndexChanged += (s, e) => ApplyListFilters();
    }

    void ApplyListFilters() {
        grid.EndEdit();
        var category = (ModCategory)categoryFilter.SelectedIndex;
        var fit = category == ModCategory.Clothes ? (ClothingFit)clothingFilter.SelectedIndex : ClothingFit.All;
        foreach (DataGridViewRow row in grid.Rows) {
            string id = (string)row.Tag;
            if (id == null) continue;
            ModCategory rowCategory;
            bool known = rowCategories.TryGetValue(id, out rowCategory);
            if (!known) rowCategory = ModCategories.CategoryOf(id, null);
            bool visible = (!known || EditionEligible(id)) &&
                (category == ModCategory.All || category == rowCategory) &&
                ModCategories.Supports(id, fit);
            if (!visible) {
                if (grid.CurrentCell != null && grid.CurrentCell.RowIndex == row.Index) grid.CurrentCell = null;
                if (Convert.ToBoolean(row.Cells["Select"].Value)) row.Cells["Select"].Value = false;
            }
            if (row.Visible != visible) row.Visible = visible;
        }
        UpdateSelectAll();
        UpdateSummary();
    }
}
