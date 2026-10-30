using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

public sealed class ModImportItem {
    public string Id,Kind,Name,Source,Sha256,State;
    public int ModId,FileId;
    public long Size;
}
public sealed class ModImportResult {
    public string[] SelectedIds=new string[0];
    public string Message;
    public bool Pending;
    public ModImportItem[] PendingItems=new ModImportItem[0];
}
public sealed class NexusImportLink { public int ModId,FileId; public string Url; }

public sealed class ModImporter {
    internal const string HystExcludedHash="57b106087f766589616b3f98d0851f643657abae2a778bc42a0a05b314488a82";
    internal static bool ExcludedFromVanillaProfile(ModImportItem item){return item!=null&&item.Kind=="Arquivo"&&item.Sha256==HystExcludedHash;}
    readonly FixedCatalog catalog;readonly DownloadCache cache;readonly string queue;
    const string PendingState="Pendente de auditoria";
    const long MaxArchive=8L*1024*1024*1024;
    public ModImporter(FixedCatalog c,DownloadCache downloads,string queueFile) {
        if(c==null||downloads==null)throw new ArgumentNullException();
        catalog=c;cache=downloads;queue=LocalPath(queueFile);Safe.NoLinks(queue);
    }
    public static NexusImportLink ParseNexusLink(string input) {
        string text=(input??"").Trim();Uri uri;
        if(text.Length>4096||text.Length==0||text.Any(c=>Char.IsControl(c)||Char.IsWhiteSpace(c))||text.IndexOf('\\')>=0||!Uri.TryCreate(text,UriKind.Absolute,out uri)||uri.Scheme!="https"||uri.DnsSafeHost!="www.nexusmods.com"||!uri.IsDefaultPort||uri.UserInfo!="")
            throw new IOException("Use um link HTTPS de mod do Cyberpunk 2077 no www.nexusmods.com.");
        // Refuse escaped/normalized paths; only the public mod page is an import source.
        Match raw=Regex.Match(text,@"^https://www\.nexusmods\.com(?::443)?(/[^?#]*)(?:\?[^#]*)?(?:#.*)?$",RegexOptions.IgnoreCase);
        Match path=Regex.Match(raw.Success?raw.Groups[1].Value:"",@"^/cyberpunk2077/mods/([0-9]+)/?$",RegexOptions.IgnoreCase);
        int mod,file=0;if(!path.Success||!Int32.TryParse(path.Groups[1].Value,out mod)||mod<=0)throw new IOException("O link precisa apontar para uma página de mod do Cyberpunk 2077.");
        bool fileSeen=false,tabSeen=false;
        foreach(string pair in uri.Query.TrimStart('?').Split(new[]{'&'},StringSplitOptions.RemoveEmptyEntries)) {
            string[] parts=pair.Split('=');if(parts.Length!=2)throw new IOException("Parâmetros inválidos no link Nexus.");
            if(parts[0]=="file_id") {if(fileSeen||!Regex.IsMatch(parts[1],"^[0-9]+$")||!Int32.TryParse(parts[1],out file)||file<=0)throw new IOException("file_id inválido no link Nexus.");fileSeen=true;}
            else if(parts[0]=="tab") {if(tabSeen||!new[]{"files","description","posts","images","logs","bugs","stats","videos"}.Contains(parts[1]))throw new IOException("Aba Nexus inválida.");tabSeen=true;}
            else throw new IOException("O link contém parâmetros não necessários. Cole somente a página do mod ou do arquivo.");
        }
        return new NexusImportLink{ModId=mod,FileId=file,Url="https://www.nexusmods.com/cyberpunk2077/mods/"+mod+(file>0?"?tab=files&file_id="+file:"")};
    }
    static string LocalPath(string input) {
        if(String.IsNullOrWhiteSpace(input)||input.Any(Char.IsControl))throw new IOException("Escolha um arquivo local.");
        string path=Path.GetFullPath(input);if(path.StartsWith(@"\\",StringComparison.Ordinal)||path.Length<=3)throw new IOException("Escolha um arquivo em uma pasta local comum.");return path;
    }
    static ModImportResult KnownResult(List<FixedMod> matches,bool cached) {
        OnlineDiagnostics.Record("import.known",null,"Importação corresponde a uma receita fixada.",new{Mods=matches.Select(m=>m.Id).ToArray(),FileVerified=cached});
        var allowed=matches.Where(m=>String.IsNullOrWhiteSpace(m.BlockedReason)).ToList();
        string[] blocked=matches.Where(m=>!String.IsNullOrWhiteSpace(m.BlockedReason)).Select(m=>(m.Name??m.Id)+": "+m.BlockedReason).ToArray();
        if(allowed.Count==0)return new ModImportResult{Message="Mod retirado da lista por incompatibilidade. Não será baixado ou instalado. "+String.Join("; ",blocked)};
        string message=cached?"Pacote conferido e importado para o cache. Pronto para instalação automática após as verificações.":"Mod reconhecido na lista fixa. Pronto para baixar e instalar automaticamente após as verificações.";
        if(allowed.Count>1)message=allowed.Count+" variantes fixadas reconhecidas. Consulte os detalhes de corpo e compatibilidade de cada variante. "+message;
        if(blocked.Length>0)message+=" Itens retidos não foram selecionados: "+String.Join("; ",blocked);
        return new ModImportResult{SelectedIds=allowed.Select(m=>m.Id).ToArray(),Message=message};
    }
    public ModImportResult Import(string input) {
        string text=(input??"").Trim();if(text.Length>1&&text[0]=='"'&&text[text.Length-1]=='"')text=text.Substring(1,text.Length-2);
        OnlineDiagnostics.Record("import.request",null,"Entrada enviada ao importador.",new{Kind=text.IndexOf("://",StringComparison.Ordinal)>=0?"Link":"Arquivo"});
        if(text.IndexOf("://",StringComparison.Ordinal)>=0)throw new IOException("A importação aceita somente arquivos. Baixe o ZIP, RAR ou 7z e arraste o arquivo para o gerenciador.");
        string file=LocalPath(text);Safe.NoLinks(file);
        if(!File.Exists(file))throw new IOException("Arquivo não encontrado. Arraste um ZIP, RAR ou 7z já baixado para o gerenciador.");
        if(!new[]{".zip",".rar",".7z"}.Contains(Path.GetExtension(file).ToLowerInvariant()))throw new IOException("Escolha o pacote original ZIP, RAR ou 7z concluído; arquivos parciais e executáveis não são importados.");
        long size=new FileInfo(file).Length;if(size<=0||size>MaxArchive)throw new IOException("Pacote vazio ou maior que o limite de 8 GB.");
        string hash=Safe.Hash(file);
        if(hash==HystExcludedHash)return new ModImportResult{Message="Este corpo Hyst foi retirado do perfil escolhido, que usa corpo padrão. O arquivo original foi preservado."};
        var matches=catalog.Packages.Where(m=>NightCityDensity.Assets(m).Any(a=>a.Size==size&&a.Sha256==hash)).ToList();
        if(matches.Count>0) {
            foreach(var a in matches.Where(m=>String.IsNullOrWhiteSpace(m.BlockedReason)).SelectMany(m=>NightCityDensity.Assets(m)).Where(a=>a.Size==size&&a.Sha256==hash).GroupBy(a=>a.Sha256).Select(g=>g.First()))cache.Import(file,a);
            return KnownResult(matches,true);
        }
        var unknown=new ModImportItem{Id="file-"+hash,Kind="Arquivo",Name=Path.GetFileName(file),Source=file,Sha256=hash,Size=size,State=PendingState};
        AddPending(unknown);return Pending(unknown,"Arquivo registrado para auditoria. O original foi preservado; não foi extraído, executado ou instalado.");
    }
    static ModImportResult Pending(ModImportItem item,string message) {return new ModImportResult{Message=message,Pending=true,PendingItems=new[]{item}};}
    // Prepared downloads remain review records; recording a verified cache never authorizes installation.
    public void RegisterPrepared(FixedAsset asset){
        if(asset==null||!FixedData.HashValid(asset.Sha256)||asset.Size<=0||!cache.Contains(asset))throw new IOException("O pacote preparado precisa estar conferido no cache antes do registro.");
        if(String.IsNullOrWhiteSpace(asset.Name)||asset.Name.Length>300||Path.GetFileName(asset.Name)!=asset.Name||asset.Name.Any(Char.IsControl))throw new IOException("Nome do pacote preparado inválido.");
        AddPending(new ModImportItem{Id="file-"+asset.Sha256,Kind="Arquivo",Name=asset.Name,Source=cache.PathFor(asset),Sha256=asset.Sha256,Size=asset.Size,State=PendingState});
    }
    List<ModImportItem> ReadAll() {
        Safe.NoLinks(queue);if(!File.Exists(queue))return new List<ModImportItem>();
        if(new FileInfo(queue).Length>2000000)throw new IOException("A fila de importação está maior que o limite.");
        List<ModImportItem> items;try{items=Safe.Json.Deserialize<List<ModImportItem>>(File.ReadAllText(queue));}catch(ArgumentException){throw new IOException("A fila de importação está inválida. Preserve-a para revisão.");}
        if(items==null||items.Count>500)throw new IOException("Fila de importação inválida ou cheia.");
        var seen=new HashSet<string>();
        foreach(var item in items) {
            if(item==null||!seen.Add(item.Id??"")||String.IsNullOrWhiteSpace(item.Name)||item.Name.Length>300||item.Name.Any(Char.IsControl)||item.State!=PendingState)throw new IOException("Item inválido na fila de importação.");
            if(item.Kind=="Link") {var link=ParseNexusLink(item.Source);if(item.ModId!=link.ModId||item.FileId!=link.FileId||item.Source!=link.Url||item.Id!="nexus-"+link.ModId+"-"+link.FileId)throw new IOException("Link alterado na fila de importação.");}
            else if(item.Kind=="Arquivo") {if(!FixedData.HashValid(item.Sha256)||item.Id!="file-"+item.Sha256||item.Size<=0||item.Size>MaxArchive||LocalPath(item.Source)!=item.Source)throw new IOException("Arquivo inválido na fila de importação.");}
            else throw new IOException("Tipo inválido na fila de importação.");
        }return items;
    }
    public List<ModImportItem> ReadPending() {
        return ReadAll().Where(item=>item.Kind=="Arquivo"&&!ExcludedFromVanillaProfile(item)&&!catalog.Packages.Any(m=>NightCityDensity.Assets(m).Any(a=>a.Size==item.Size&&a.Sha256==item.Sha256))).ToList();
    }
    void AddPending(ModImportItem item) {
        OnlineDiagnostics.Record("import.unknown",null,"Item importado para revisão, sem instalação.",new{Kind=item.Kind,Name=item.Name,ModId=item.ModId,FileId=item.FileId,Sha256=item.Sha256,Size=item.Size});
        var items=ReadAll();int old=items.FindIndex(x=>x.Id==item.Id);if(old>=0)items[old]=item;else{if(items.Count>=500)throw new IOException("A fila tem 500 itens. Revise a fila antes de adicionar outros.");items.Add(item);}
        if(Encoding.UTF8.GetByteCount(Safe.Json.Serialize(items))>2000000)throw new IOException("A fila de importação atingiu o limite de tamanho. Nenhum item foi alterado.");Safe.WriteJson(queue,items);
    }
}
