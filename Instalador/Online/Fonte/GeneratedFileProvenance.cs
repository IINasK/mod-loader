using System;
using System.IO;
using System.Linq;

// These are display hints, not install receipts or permission to copy/remove.
// Receipts remain the durable tag for files actually installed by the loader.
internal static class GeneratedFileProvenance {
    internal static string Category(string path) {
        string normalized=(path??"").Replace('\\','/');
        string extension=Path.GetExtension(normalized);
        string[] folders=normalized.Split('/');
        if(folders.Any(part=>part.Equals("saves",StringComparison.OrdinalIgnoreCase)||part.Equals("savegames",StringComparison.OrdinalIgnoreCase)||
                part.Equals("saves-archive",StringComparison.OrdinalIgnoreCase))||
            extension.Equals(".sav",StringComparison.OrdinalIgnoreCase)||extension.Equals(".preset",StringComparison.OrdinalIgnoreCase))return "save";
        if(extension.Equals(".log",StringComparison.OrdinalIgnoreCase)||
            folders.Any(part=>part.Equals("logs",StringComparison.OrdinalIgnoreCase)))return "log";
        if(extension.Equals(".cache",StringComparison.OrdinalIgnoreCase)||
            folders.Any(part=>part.Equals("cache",StringComparison.OrdinalIgnoreCase)||part.Equals("caches",StringComparison.OrdinalIgnoreCase)))return "cache";
        if(new[]{".json",".jsonc",".ini",".cfg",".conf",".toml"}.Contains(extension,StringComparer.OrdinalIgnoreCase)||
            Path.GetFileName(normalized).Equals("settings.lua",StringComparison.OrdinalIgnoreCase)||
            Path.GetFileName(normalized).EndsWith("_settings.lua",StringComparison.OrdinalIgnoreCase))return "configuration";
        if(new[]{".db",".sqlite",".sqlite3"}.Contains(extension,StringComparer.OrdinalIgnoreCase))return "data";
        return "unknown";
    }

    internal static string Describe(P2PSyncDifference file) {
        string kind=Category(file.Path),label=kind=="save"?GeneralStyle.T("Save ou perfil local","Local save or profile"):
            kind=="log"?GeneralStyle.T("Possível log","Possible log"):
            kind=="cache"?GeneralStyle.T("Possível cache","Possible cache"):
            kind=="configuration"?GeneralStyle.T("Possível configuração","Possible configuration"):
            kind=="data"?GeneralStyle.T("Possíveis dados locais","Possible local data"):
            GeneralStyle.T("Arquivo local","Local file");
        return label+GeneralStyle.T(" · autoria não confirmada"," · creator not confirmed")+
            (file.LocalInModFolder&&!String.IsNullOrWhiteSpace(file.Mod)
            ?GeneralStyle.T(" · pasta associada a "," · folder associated with ")+file.Mod
            :"");
    }
}
