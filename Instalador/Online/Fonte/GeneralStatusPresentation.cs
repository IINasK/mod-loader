using System;
using System.Collections.Generic;
using System.IO;

internal static class GeneralStatusPresentation {
    internal static string Label(string raw) {
        string value=(raw??"").Trim().ToLowerInvariant();
        if(value=="atualização disponível")return GeneralStyle.T("Atualização disponível","Update available");
        if(value=="versão diferente")return GeneralStyle.T("Versão diferente","Different version");
        if(value=="versão mantida")return GeneralStyle.T("Versão mantida","Version kept");
        if(Kind(raw)=="error")return GeneralStyle.T("Falha","Failed");
        if(value=="baixando")return GeneralStyle.T("Baixando","Downloading");
        if(DownloadComplete(raw))return GeneralStyle.T("Download concluído","Download complete");
        return GeneralStyle.T("Pendente","Pending");
    }
    internal static bool DownloadComplete(string raw) {
        string value=(raw??"").Trim().ToLowerInvariant();
        return Kind(raw)=="installed"||value=="pronto para instalar"||value=="instalando";
    }
    internal static string Kind(string raw) {
        switch ((raw ?? "").Trim().ToLowerInvariant()) {
            case "instalado":
            case "instalado correto":
            case "instalado: baixa personalizada":
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
            case "atualização disponível":
            case "versão diferente":
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
            { "installed", new[] { "Instalado", "Instalado correto", " INSTALADO ", "Instalado: Baixa personalizada" } },
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
        foreach(var value in new[]{new[]{"Não conferido","Pendente"},new[]{"Na fila","Pendente"},new[]{"Baixando","Baixando"},new[]{"Pronto para instalar","Download concluído"},new[]{"Instalando","Download concluído"},new[]{"Instalado","Download concluído"},new[]{"Falhou","Falha"},new[]{"Parcial","Falha"}}){
            if(GeneralStatusPresentation.Label(value[0])!=value[1])throw new IOException("Rótulo incorreto: "+value[0]);
            results.Add(new{Status=value[0],Label=value[1]});
        }
        foreach(string status in new[]{"Pronto para instalar","Instalando","Instalado"})
            if(!GeneralStatusPresentation.DownloadComplete(status))throw new IOException("Cor de download concluído ausente: "+status);
        if(GeneralStatusPresentation.DownloadComplete("Na fila"))throw new IOException("A fila ainda tem download pendente.");
        GeneralStyle.Language="en";
        if(GeneralStatusPresentation.Label("Não conferido")!="Pending")throw new IOException("Rótulo pendente não traduzido.");
        if(GeneralStatusPresentation.Label("Instalado")!="Download complete")throw new IOException("Rótulo não traduzido.");
        results.Add(new{Status="Instalado",Label="Download complete"});GeneralStyle.Language=previous;
        string root = Path.Combine(Path.GetFullPath(parent), "online-testes-status-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Safe.WriteJson(Path.Combine(root, "resultado-status-presentation.json"), new { Passed = results.Count, Tests = results, RealGameTouched = false, NetworkUsed = false });
        return results.Count;
    }
}
