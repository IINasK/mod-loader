using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class GeneralCategoryDeleteTests {
    internal static int Run(string parent) {
        string root = Path.Combine(Path.GetFullPath(parent), "category-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<string>();
        Action<bool, string> check = (ok, message) => { if (!ok) throw new IOException(message); checks.Add(message); };
        string archive = Path.Combine(root, "preserved.zip");
        File.WriteAllText(archive, "An untouched archive reference; this test never installs it.");
        string archiveHash = Safe.Hash(archive);
        var member = new GeneralModEntry {
            Id = "member", Name = "Member", Category = "Roupas", DefaultVariant = "default", Nsfw = true,
            Tags = new List<string> { "Favorite" }, Description = "Preserved description",
            Variants = new List<GeneralModVariant> { new GeneralModVariant { Id = "default", Name = "Default", Sources = new List<string> { archive }, CyberpunkId = "trusted-recipe" } }
        };
        var untouched = new GeneralModEntry {
            Id = "other", Name = "Other", Category = "Veículos", DefaultVariant = "default",
            Variants = new List<GeneralModVariant> { new GeneralModVariant { Id = "default", Name = "Default", Sources = new List<string> { "https://example.com/other.zip" } } }
        };
        var list = new GeneralModList {
            UncategorizedName = "Avulsos", Categories = new List<string> { "Roupas", "Veículos", "Vazia" },
            CategoryDescriptions = new Dictionary<string, string> { { "Roupas", "Clothing" }, { "Veículos", "Vehicles" }, { "Vazia", "Empty" } },
            Mods = new List<GeneralModEntry> { member, untouched },
            Presets = new List<GeneralModPreset> { new GeneralModPreset { Id = "friends", Name = "Friends", ModIds = new List<string> { "member", "other" } } }
        };
        var state = new GeneralModState { List = list, SelectedPreset = "friends", Theme = "dark" };
        state.ChosenVariants["member"] = "default";
        state.ResolvedDownloads["member|default|0"] = archive;
        var library = new GeneralModLibrary(Path.Combine(root, "state.json"));
        library.Save(state);
        GeneralModLibrary.DeleteCategory(list, "roupas");
        check(!list.Categories.Contains("Roupas") && !list.CategoryDescriptions.ContainsKey("Roupas"), "Delete removes only the chosen category and its description, ignoring case.");
        check(list.Mods.Count == 2 && Object.ReferenceEquals(list.Mods[0], member) && member.Category == null, "Members move to the uncategorized group without removing mods.");
        check(member.Tags.SequenceEqual(new[] { "Favorite" }) && member.Nsfw && member.Description == "Preserved description" && member.Variants[0].CyberpunkId == "trusted-recipe", "Deletion preserves tags, content flag, description and trusted variant mapping.");
        check(untouched.Category == "Veículos" && list.CategoryDescriptions["Veículos"] == "Vehicles" && list.Presets[0].ModIds.SequenceEqual(new[] { "member", "other" }), "Other categories and preset memberships remain unchanged.");
        library.Save(state);
        var loaded = library.Load();
        check(loaded.List.Mods[0].Category == null && !loaded.List.Categories.Contains("Roupas") && loaded.SelectedPreset == "friends" && loaded.ChosenVariants["member"] == "default" && loaded.ResolvedDownloads["member|default|0"] == archive, "Deletion and user choices survive closing and reopening.");
        string exported = Path.Combine(root, "export.json");
        library.Export(exported, loaded.List);
        var imported = library.Import(exported);
        check(imported.Mods[0].Category == null && imported.Presets[0].ModIds.Contains("member") && imported.Mods[0].Variants[0].Sources[0] == archive, "Portable export/import retains the moved mod and its source.");
        GeneralModLibrary.DeleteCategory(loaded.List, "Vazia");
        check(!loaded.List.Categories.Contains("Vazia") && loaded.List.Mods.Count == 2, "Empty categories can be deleted.");
        string before = Safe.Json.Serialize(loaded.List);
        foreach (string invalid in new[] { null, "", "Avulsos", "Missing" }) {
            bool rejected = false;
            try { GeneralModLibrary.DeleteCategory(loaded.List, invalid); } catch (IOException) { rejected = true; }
            check(rejected && before == Safe.Json.Serialize(loaded.List), "Invalid or root category deletion is rejected without changes: " + (invalid ?? "null"));
        }
        check(Safe.Hash(archive) == archiveHash, "Category deletion never touches the referenced mod archive.");

        bool animations = GeneralStyle.AnimationsEnabled;
        string language = GeneralStyle.Language;
        GeneralStyle.AnimationsEnabled = false; GeneralStyle.Language = "en";
        try {
            using (var dialog = new GeneralCategoryDetails("Roupas", "Description", false, true)) {
                dialog.StartPosition = FormStartPosition.Manual; dialog.Location = new Point(-30000, -30000); dialog.ShowInTaskbar = false;
                dialog.Show(); Application.DoEvents();
                var delete = Descendants(dialog).OfType<Button>().Single(button => button.Text == "Delete category");
                check(((GeneralButton)delete).Destructive, "Delete category is a destructive action in the category editor.");
                delete.PerformClick();
                check(dialog.DeleteRequested && dialog.DialogResult == DialogResult.OK, "The editor returns a deletion request for the caller to confirm.");
            }
            using (var rootDialog = new GeneralCategoryDetails("Avulsos", "", true, true))
                check(!Descendants(rootDialog).OfType<Button>().Any(button => button.Text == "Delete category"), "The uncategorized editor never offers deletion.");
            using (var newDialog = new GeneralCategoryDetails("", ""))
                check(!Descendants(newDialog).OfType<Button>().Any(button => button.Text == "Delete category"), "A new unsaved category does not offer deletion.");
        } finally { GeneralStyle.AnimationsEnabled = animations; GeneralStyle.Language = language; }
        Safe.WriteJson(Path.Combine(root, "resultado-category-delete.json"), new { Passed = true, Tests = checks, RealGameTouched = false, NetworkUsed = false });
        return checks.Count;
    }
    static IEnumerable<Control> Descendants(Control parent) {
        foreach (Control child in parent.Controls) { yield return child; foreach (Control next in Descendants(child)) yield return next; }
    }
}
