using System;
using System.Collections.Generic;
using System.IO;

internal static class GeneralStatusPresentation {
    internal static string Label(string raw) {
        string value=(raw??"").Trim().ToLowerInvariant();
        if(Kind(raw)=="error")return GeneralStyle.T("Falha","Failed");
        if(value=="baixando")return GeneralStyle.T("Baixando","Downloading");
        if(Kind(raw)=="installed"||value=="pronto para instalar"||value=="instalando")return GeneralStyle.T("Download concluído","Download complete");
        return GeneralStyle.T("Não baixado","Not downloaded");
    }
    internal static string Kind(string raw) {
        switch ((raw ?? "").Trim().ToLowerInvariant()) {
            case "instalado":
            case "instalado correto":
                return "installed";
            case "falhou":
            case "incompatível":
            case "não verificável":
            case "diferente/alterado":
            case "arquivos alterados":
            case "arquivo não encontrado":
            case "parcial":
            case "instalação parcial":
            case "parcial/pulado":
                return "error";
            case "na fila":
            case "verificando":
            case "baixando":
            case "instalando":
            case "conferindo download":
            case "aguardando download":
            case "aguardando arquivo":
                return "active";
            default:
                return "pending";
        }
    }
}

internal static class GeneralStatusPresentationTests {
    internal static int Run(string parent) {
        var cases = new Dictionary<string, string[]> {
            { "installed", new[] { "Instalado", "Instalado correto", " INSTALADO " } },
            { "error", new[] { "Falhou", "Incompatível", "Não verificável", "Diferente/alterado", "Arquivos alterados", "Arquivo não encontrado", "Parcial", "Instalação parcial", "Parcial/pulado" } },
            { "active", new[] { "Na fila", "Verificando", "Baixando", "Instalando", "Conferindo download", "Aguardando download", "Aguardando arquivo" } },
            { "pending", new[] { "", "Não instalado", "Não iniciado", "Não conferido", "A baixar", "Pronto para instalar", "Pulado", "Verificação pulada", "Setup NASK disponível", "Instalado: Média", "Instalado: Baixa", "Instalado: Alta", "Requer Phantom Liberty", "Requer Cyberpunk 2077", "Installed", "Instalado parcialmente", "Estado futuro" } }
        };
        var results = new List<object>();
        foreach (var group in cases) foreach (string status in group.Value) {
            string actual = GeneralStatusPresentation.Kind(status);
            if (actual != group.Key) throw new IOException("Cor de status incorreta para “" + status + "”: " + actual + ".");
            results.Add(new { Status = status, Kind = actual });
        }
        if (GeneralStatusPresentation.Kind(null) != "pending") throw new IOException("Status ausente precisa ser tratado como não iniciado.");
        results.Add(new { Status = (string)null, Kind = "pending" });
        string previous=GeneralStyle.Language;GeneralStyle.Language="pt";
        foreach(var value in new[]{new[]{"Não conferido","Não baixado"},new[]{"Na fila","Não baixado"},new[]{"Baixando","Baixando"},new[]{"Pronto para instalar","Download concluído"},new[]{"Instalando","Download concluído"},new[]{"Instalado","Download concluído"},new[]{"Falhou","Falha"},new[]{"Parcial","Falha"}}){
            if(GeneralStatusPresentation.Label(value[0])!=value[1])throw new IOException("Rótulo incorreto: "+value[0]);
            results.Add(new{Status=value[0],Label=value[1]});
        }
        GeneralStyle.Language="en";
        if(GeneralStatusPresentation.Label("Instalado")!="Download complete")throw new IOException("Rótulo não traduzido.");
        results.Add(new{Status="Instalado",Label="Download complete"});GeneralStyle.Language=previous;
        string root = Path.Combine(Path.GetFullPath(parent), "online-testes-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Safe.WriteJson(Path.Combine(root, "resultado-status-presentation.json"), new { Passed = results.Count, Tests = results, RealGameTouched = false, NetworkUsed = false });
        return results.Count;
    }
}
