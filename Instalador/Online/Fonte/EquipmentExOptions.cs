using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

internal static class EquipmentExOptions {
    internal const string ModId = "mod-6945";
    internal const string FrameworkId = "mod-30726";
    internal const string StockId = "stock";
    internal const string WeaveId = "default";
    internal const string Operation = "equipment-ex-profile";

    internal static bool Available(FixedMod mod) {
        return mod != null && mod.Id == ModId && mod.Files.Any(FpvCustomization.IsWeaveUpgrade);
    }

    internal static FixedMod Resolve(FixedMod mod, string id) {
        if (!Available(mod) || id != StockId && id != WeaveId)
            throw new IOException("Versão do Equipment-EX inválida.");
        if (id == WeaveId) return mod;

        var original = mod.Files.Single(FpvCustomization.IsWeaveUpgrade);
        var stock = Safe.Json.Deserialize<FixedMod>(Safe.Json.Serialize(mod));
        stock.Name = "Equipment-EX";
        stock.Version = "1.2.10";
        stock.Dependencies = stock.Dependencies.Where(dependency => dependency != FrameworkId).ToArray();
        stock.Assets = stock.Assets.Where(asset => asset.ModId == 6945).ToList();
        var assetId = stock.Assets.Single().Id;
        stock.Files = stock.Files.Where(file => file.Asset == assetId).ToList();
        stock.Files.Add(new FixedFile {
            Asset = assetId, Entry = original.Path, Path = original.Path,
            Size = FpvCustomization.StockEquipmentExSize, Sha256 = FpvCustomization.StockEquipmentExHash
        });
        return stock;
    }

    internal static DensityProfileState Read(FixedMod mod, string game, Func<string,string> hashFile = null) {
        var stock = Resolve(mod, StockId);
        var script = mod.Files.Single(FpvCustomization.IsWeaveUpgrade);
        var stockFiles = stock.Files.ToDictionary(file => file.Path, StringComparer.OrdinalIgnoreCase);
        int found = 0, weaveFiles = 0;
        bool stockScript = false, weaveScript = false;
        foreach (var file in mod.Files) {
            string path = Safe.In(game, file.Path);
            Safe.NoLinks(path);
            if (Directory.Exists(path)) return Conflict();
            if (!File.Exists(path)) continue;
            found++;
            long size = new FileInfo(path).Length;
            string hash = hashFile == null ? Safe.Hash(path) : hashFile(path);
            if (file == script && FpvCustomization.IsStockEquipmentEx(file, size, hash)) {
                stockScript = true;
                continue;
            }
            if (size != file.Size || hash != file.Sha256) return Conflict();
            if (file == script) weaveScript = true;
            if (!stockFiles.ContainsKey(file.Path)) weaveFiles++;
        }
        if (found == 0) return new DensityProfileState { Id = "absent", CanApply = true };
        if (stockScript && weaveFiles > 0) return Conflict();
        bool weave = weaveScript || weaveFiles > 0;
        var selected = weave ? mod : stock;
        return new DensityProfileState {
            Id = weave ? WeaveId : StockId, Name = selected.Name, CanApply = true,
            Partial = found != selected.Files.Count, Version = selected.Version
        };
    }

    static DensityProfileState Conflict() {
        return new DensityProfileState {
            Id = "conflict", Name = "Versão diferente",
            Detail = "Há arquivos alterados ou uma mistura de versões do Equipment-EX. Confira os arquivos antes de trocar."
        };
    }

    internal static bool StockActive(FixedCatalog catalog, string root) {
        if (String.IsNullOrEmpty(root)) return false;
        var mod = catalog.Packages.SingleOrDefault(recipe => recipe.Id == ModId);
        if (!Available(mod)) return false;
        var file = mod.Files.Single(FpvCustomization.IsWeaveUpgrade);
        string path = Safe.In(root, file.Path);
        Safe.NoLinks(path);
        return File.Exists(path) && new FileInfo(path).Length == FpvCustomization.StockEquipmentExSize
            && Safe.Hash(path) == FpvCustomization.StockEquipmentExHash;
    }

    internal static void ValidateReceipt(FixedCatalog catalog, OnlineReceipt receipt) {
        var mod = catalog.Packages.SingleOrDefault(recipe => recipe.Id == ModId);
        if (!Available(mod) || receipt.Mods.Count != 1 || receipt.Mods[0] != ModId
            || receipt.DensityBefore != "absent" && receipt.DensityBefore != StockId && receipt.DensityBefore != WeaveId)
            throw new IOException("Registro de troca do Equipment-EX inválido.");
        var previous = receipt.DensityBefore == "absent"
            ? new List<FixedFile>() : Resolve(mod, receipt.DensityBefore).Files;
        var next = Resolve(mod, receipt.DensityAfter).Files;
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in receipt.Files) {
            if (file == null || !paths.Add(file.Path) || file.Before == file.After
                || file.Before != null && !previous.Any(expected => expected.Path == file.Path && expected.Sha256 == file.Before)
                || file.After != null && !next.Any(expected => expected.Path == file.Path && expected.Sha256 == file.After))
                throw new IOException("Registro não corresponde às versões do Equipment-EX.");
        }
    }
}
