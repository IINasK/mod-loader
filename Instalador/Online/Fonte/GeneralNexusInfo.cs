using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

public sealed class GeneralSourceInfo {
    public string Name,FileName,Version,ModVersion;
    public bool SizeApproximate,NexusChecked;
    public bool GithubChecked;
    public string GithubAssetUrl;
    internal bool Nsfw,NsfwKnown;
    internal string[] Tags;
    internal string Reference;
    internal GeneralUpdateChecker.Asset[] ReleaseChoices;
    public long Bytes;
    internal GeneralSourceInfo SavedCopy(){
        var github=GithubChecked?GeneralGithubLink.Parse(GithubAssetUrl):null;
        bool verified=github!=null&&github.Asset!=null;
        return new GeneralSourceInfo{Name=Name,FileName=FileName,Version=Version,ModVersion=ModVersion,
            Bytes=Bytes,SizeApproximate=SizeApproximate,NexusChecked=NexusChecked,
            GithubChecked=verified,GithubAssetUrl=verified?github.Url:null};
    }
}
internal sealed class GeneralNexusLink {
    internal string Game;
    internal int Mod,File;
    internal string Url {get{return "https://www.nexusmods.com/"+Game+"/mods/"+Mod+(File>0?"?tab=files&file_id="+File:"");}}
    internal static GeneralNexusLink Parse(string value){
        Uri uri;if(!GeneralModDownload.IsWebSource(value)||!Uri.TryCreate(value,UriKind.Absolute,out uri)||!(uri.Host=="www.nexusmods.com"||uri.Host=="nexusmods.com"))return null;
        var path=Regex.Match(uri.AbsolutePath,@"\A/(?:[a-z]{2}/)?(?:games/)?(?<game>[a-z0-9][a-z0-9_-]{0,99})/mods/(?<mod>[1-9][0-9]{0,9})/?\z",RegexOptions.IgnoreCase);
        int mod;if(!path.Success||!Int32.TryParse(path.Groups["mod"].Value,out mod))return null;
        var link=new GeneralNexusLink{Game=path.Groups["game"].Value.ToLowerInvariant(),Mod=mod};var seen=new HashSet<string>();
        foreach(string part in uri.Query.TrimStart('?').Split(new[]{'&'},StringSplitOptions.RemoveEmptyEntries)){
            var pair=part.Split('=');if(pair.Length!=2||!seen.Add(pair[0]))return null;
            if(pair[0]=="file_id"){if(!Int32.TryParse(pair[1],out link.File)||link.File<=0)return null;}
            else if(pair[0]!="tab"||!new[]{"files","description","posts","images","logs","bugs","stats","videos"}.Contains(pair[1]))return null;
        }
        return link;
    }
}
internal sealed class GeneralNexusRequirement {
    internal string Name,MinVersion,MaxVersion;
    internal GeneralNexusLink Source;
    internal int MinFile,MaxFile;
    internal bool Nsfw;
}

internal static partial class GeneralNexusInfo {
    const string ApiHost = "https://api.nexusmods.com";
    const string ApiKeyVariable = "MOD_LOADER_NEXUS_API_KEY";
    const int MaxResponse = 4 * 1024 * 1024;
    const long MaxFileBytes = 128L * 1024 * 1024 * 1024;

    // Cyberpunk sources confirmed by Nexus REST v3 on 2026-11-08.
    static readonly HashSet<int> AuditedAdultMods = new HashSet<int> {
        1873, 4654, 6985, 7054, 7638, 7822, 9074, 9083, 18353, 20058, 35772
    };

    internal static bool IsAuditedAdultSource(string source) {
        var link=GeneralNexusLink.Parse(source);
        return link!=null&&link.Game=="cyberpunk2077"&&AuditedAdultMods.Contains(link.Mod);
    }

    internal static string Advice {get{return GeneralStyle.T("No Nexus, copie o link da tela Slow download da versão desejada.","On Nexus, copy the link to the Slow download page for the version you want.");}}

    internal static bool HasTestKey {get{return TestKey()!=null;}}

    static string TestKey() {
        // Personal keys are for local testing until the application is registered.
        string key = NexusCredentials.Read();
        return String.IsNullOrWhiteSpace(key) || key.Length > 256 || key.Any(c => Char.IsControl(c) || Char.IsWhiteSpace(c)) ? null : key;
    }

    internal static Task<GeneralSourceInfo> Read(string source){return Read(source,false);}
    internal static Task<GeneralSourceInfo> Read(string source,bool editor) {
        var link = GeneralNexusLink.Parse(source);
        string testKey = TestKey();
        if (link == null) return Task.FromResult<GeneralSourceInfo>(null);
        return Task.Run(() => {
            string key = testKey;
            string resource = "/games/" + link.Game + "/mods/" + link.Mod;
            HttpStatusCode status;
            string json = ReadJson("/v3" + resource, key, out status);
            if (key != null && status == HttpStatusCode.Unauthorized) {
                key = null;
                json = ReadJson("/v3" + resource, null, out status);
            }
            if ((int)status == 451) return new GeneralSourceInfo { Nsfw = true };
            var info = ParseMod(json, link);
            if (info != null && link.File > 0 && key != null)
            {
                ApplyFile(ReadJson("/v1" + resource + "/files/" + link.File + ".json", key, out status), link, info);
                if(editor&&info.Reference==null&&status==HttpStatusCode.OK){
                    var details=ParseObject(ReadJson("/v1"+resource+".json",key,out status));
                    if(MatchesId(details,"mod_id",link.Mod))info.Reference=GeneralNexusGithubMirror.Unique(
                        GeneralNexusGithubMirror.Repositories(Value(details,"description") as string));
                }
            }
            return info;
        });
    }

    internal static void ReviewReceivedList(GeneralModList list,Func<string,Task<GeneralSourceInfo>> lookup=null) {
        bool exact=HasTestKey;
        var sources=list.Mods.SelectMany(mod=>mod.Variants.SelectMany(variant=>variant.Sources
            .Select(source=>new{Mod=mod,Variant=variant,Source=source,Link=GeneralNexusLink.Parse(source)})))
            .Where(item=>item.Link!=null).ToArray();
        Func<GeneralNexusLink,string> key=link=>exact?link.Url:new GeneralNexusLink{Game=link.Game,Mod=link.Mod}.Url;
        var queries=sources.Select(item=>key(item.Link)).Distinct(StringComparer.Ordinal).ToArray();
        if(queries.Length==0)return;
        var found=new ConcurrentDictionary<string,GeneralSourceInfo>(StringComparer.Ordinal);
        var deadline=Stopwatch.StartNew();
        var limit=TimeSpan.FromSeconds(45);
        int next=-1;
        Func<Task> work=async()=>{
            while(deadline.Elapsed<limit){
                int index=Interlocked.Increment(ref next);
                if(index>=queries.Length)return;
                var url=queries[index];
                try{
                    var info=await (lookup==null?Read(url):lookup(url)).ConfigureAwait(false);
                    if(info!=null)found[url]=info;
                }catch(Exception ex){OnlineDiagnostics.Record("p2p.nexus.lookup",null,ex.GetType().Name);}
            }
        };
        var workers=Task.WhenAll(Enumerable.Range(0,Math.Min(4,queries.Length)).Select(i=>Task.Run(work)));
        Task.WhenAny(workers,Task.Delay(limit)).GetAwaiter().GetResult();
        foreach(var item in sources){
            GeneralSourceInfo info;
            if(!found.TryGetValue(key(item.Link),out info))continue;
            if(item.Mod.NsfwOverride==null)item.Mod.Nsfw|=info.Nsfw;
            foreach(var tag in info.Tags??new string[0]){
                if(item.Mod.Tags.Count>=32)break;
                if(!item.Mod.Tags.Contains(tag,StringComparer.OrdinalIgnoreCase))item.Mod.Tags.Add(tag);
            }
            if(!info.NexusChecked||String.IsNullOrWhiteSpace(info.Name))continue;
            GeneralSourceInfo previous;
            item.Variant.SourceInfo.TryGetValue(item.Source,out previous);
            var saved=info.SavedCopy();
            if(previous!=null&&String.IsNullOrEmpty(saved.Version)){
                saved.Version=previous.Version;
                saved.FileName=previous.FileName;
                saved.Bytes=previous.Bytes;
                saved.SizeApproximate=previous.SizeApproximate;
            }
            item.Variant.SourceInfo[item.Source]=saved;
        }
        OnlineDiagnostics.Record("p2p.nexus.review",null,"Consulta da lista recebida.",new{
            Links=sources.Length,Queries=queries.Length,Verified=found.Values.Count(info=>info.NexusChecked),ExactFileLookup=exact
        });
    }

    internal static Task<List<GeneralNexusRequirement[]>> ReadRequirements(GeneralNexusLink link) {
        string key=TestKey();
        if(link.File<=0||key==null)return Task.FromResult<List<GeneralNexusRequirement[]>>(null);
        return Task.Run(()=>{
            HttpStatusCode status;
            string game="/games/"+link.Game;
            var selected=ParseObject(ReadJson("/v1"+game+"/mods/"+link.Mod+"/files/"+link.File+".json",key,out status));
            if(!MatchesId(selected,"file_id",link.File))return null;
            var file=Object(ParseObject(ReadJson("/v3"+game+"/mod-file-versions/"+link.File,key,out status)),"data");
            string id=Text(file,"id",120);
            if(!MatchesId(file,"game_scoped_id",link.File)||id==null||!Regex.IsMatch(id,@"\A[a-zA-Z0-9_-]+\z"))return null;
            return ParseRequirements(ReadJson("/v3/mod-file-versions/"+id+"/dependencies",key,out status));
        });
    }

    internal static List<GeneralNexusRequirement[]> ParseRequirements(string json) {
        var definitions=Value(ParseObject(json),"dependency_definitions") as object[];
        if(definitions==null||definitions.Length>128)return null;
        var result=new List<GeneralNexusRequirement[]>();
        foreach(var definition in definitions){
            var ranges=Value(definition as Dictionary<string,object>,"ranges") as object[];
            if(ranges==null||ranges.Length==0||ranges.Length>128)return null;
            var alternatives=new List<GeneralNexusRequirement>();
            foreach(var item in ranges){
                var range=item as Dictionary<string,object>;
                var mod=Object(Object(range,"target_mod_file"),"mod");
                string name=Text(mod,"name",300);
                string game=Text(Object(mod,"game"),"domain_name",100);
                int modId=ReadId(mod,"game_scoped_id"), min=ReadId(Object(range,"min_version"),"game_scoped_id");
                var maxVersion=Value(range,"max_version");
                int max=ReadId(maxVersion as Dictionary<string,object>,"game_scoped_id");
                if(name==null||game==null||!Regex.IsMatch(game,@"\A[a-z0-9][a-z0-9_-]*\z")||modId==0||min==0||
                    (maxVersion!=null&&max==0))return null;
                alternatives.Add(new GeneralNexusRequirement{Name=name,
                    Source=new GeneralNexusLink{Game=game,Mod=modId},MinFile=min,MaxFile=max,
                    Nsfw=Equals(Value(mod,"adult_content"),true),
                    MinVersion=Text(Object(range,"min_version"),"version",120),
                    MaxVersion=Text(maxVersion as Dictionary<string,object>,"version",120)});
            }
            result.Add(alternatives.ToArray());
        }
        return result;
    }

    static Dictionary<string,object> Object(Dictionary<string,object> data,string key) {
        return Value(data,key) as Dictionary<string,object>;
    }
    static int ReadId(Dictionary<string,object> data,string key) {
        int id;
        return Int32.TryParse(Convert.ToString(Value(data,key),CultureInfo.InvariantCulture),
            NumberStyles.None,CultureInfo.InvariantCulture,out id)&&id>0?id:0;
    }

    static string ReadJson(string route, string key, out HttpStatusCode status) {
        return ReadJsonUrl(new Uri(ApiHost+route),key,out status,8000);
    }

    static string ReadJsonUrl(Uri uri,string key,out HttpStatusCode status,int timeout,bool preview=false) {
        status = 0;
        var request = (HttpWebRequest)WebRequest.Create(uri);
        request.UserAgent = "ModLoader/" + GeneralUpdateChecker.CurrentVersion;
        request.Headers["Application-Name"] = "Mod Loader";
        request.Headers["Application-Version"] = GeneralUpdateChecker.CurrentVersion;
        if (key != null) request.Headers["apikey"] = key;
        request.Accept = "application/json";
        request.AllowAutoRedirect = false;
        request.Timeout = request.ReadWriteTimeout = timeout;
        request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        try {
            using (var response = (HttpWebResponse)request.GetResponse()) {
                status = response.StatusCode;
                if (status != HttpStatusCode.OK || response.ContentLength > MaxResponse ||
                    !(response.ContentType ?? "").StartsWith("application/json",StringComparison.OrdinalIgnoreCase)&&
                    !(preview&&(response.ContentType??"").StartsWith("application/octet-stream",StringComparison.OrdinalIgnoreCase)))return null;
                using (var input = response.GetResponseStream())
                using (var reader = new StreamReader(input, Encoding.UTF8)) {
                    var text = new StringBuilder();
                    var buffer = new char[8192];
                    int count;
                    while ((count = reader.Read(buffer, 0, buffer.Length)) > 0) {
                        if (text.Length + count > MaxResponse) return null;
                        text.Append(buffer, 0, count);
                    }
                    return text.ToString();
                }
            }
        } catch (WebException ex) {
            using (var response = ex.Response as HttpWebResponse)
                if (response != null) status = response.StatusCode;
            return null;
        } catch (IOException) {
            return null;
        }
    }

    internal static GeneralSourceInfo ParseMod(string json, GeneralNexusLink link) {
        var data = Value(ParseObject(json), "data") as Dictionary<string, object>;
        string name = Text(data, "name", 300);
        if (link == null || name == null || !MatchesId(data, "game_scoped_id", link.Mod)) return null;
        var tags = Value(data, "tags") as object[];
        var adult = Value(data, "adult_content");
        return new GeneralSourceInfo {
            Name = name,
            Reference = GeneralNexusGithubMirror.Unique(GeneralNexusGithubMirror.Repositories(Value(data,"description") as string)),
            NexusChecked = true,
            ModVersion = Text(data, "version", 120),
            Nsfw = adult is bool && (bool)adult,
            NsfwKnown = adult is bool,
            Tags = tags == null ? null : tags.Select(tag => Text(tag as Dictionary<string, object>, "name", 60))
                .Where(tag => tag != null).Distinct(StringComparer.OrdinalIgnoreCase).Take(32).ToArray()
        };
    }

    internal static void ApplyFile(string json, GeneralNexusLink link, GeneralSourceInfo info) {
        var data = ParseObject(json);
        if (link == null || info == null || link.File <= 0 || !MatchesId(data, "file_id", link.File)) return;
        info.FileName = Text(data, "file_name", 300);
        info.Version = Text(data, "version", 120);
        long kib;
        if (Int64.TryParse(Convert.ToString(Value(data, "size_kb"), CultureInfo.InvariantCulture),
                NumberStyles.None, CultureInfo.InvariantCulture, out kib) && kib > 0 && kib <= MaxFileBytes / 1024) {
            info.Bytes = kib * 1024;
            info.SizeApproximate = true;
        }
    }

    static Dictionary<string, object> ParseObject(string json) {
        if (String.IsNullOrWhiteSpace(json) || json.Length > MaxResponse) return null;
        try { return Safe.Json.DeserializeObject(json) as Dictionary<string, object>; }
        catch (ArgumentException) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    static object Value(Dictionary<string, object> data, string key) {
        object value;
        return data != null && data.TryGetValue(key, out value) ? value : null;
    }

    static bool MatchesId(Dictionary<string, object> data, string key, int expected) {
        int id;
        return Int32.TryParse(Convert.ToString(Value(data, key), CultureInfo.InvariantCulture),
            NumberStyles.None, CultureInfo.InvariantCulture, out id) && id == expected;
    }

    static string Text(Dictionary<string, object> data, string key, int limit) {
        string value = Value(data, key) as string;
        if (String.IsNullOrWhiteSpace(value) || value.Any(Char.IsControl)) return null;
        value = value.Trim();
        return value.Length <= limit ? value : null;
    }
}

internal sealed partial class GeneralModWindow {
    Dictionary<string,List<GeneralNexusRequirement[]>> publishedRequirements=new Dictionary<string,List<GeneralNexusRequirement[]>>(StringComparer.Ordinal);
    readonly HashSet<string> choosingDependencyFiles=new HashSet<string>(StringComparer.Ordinal);

    async Task ChooseDependencyFile(Work work){
        string id=work.Entry.Id;
        if(GeneralDialog.FocusOpen<GeneralDependencyFileDialog>(this,id)||!choosingDependencyFiles.Add(id))return;
        try{
            if(projectDraft!=null&&!ConfirmProjectNavigation())return;
            var live=state.List.Mods.FirstOrDefault(mod=>mod.Id==id);
            if(live==null||!GeneralModLibrary.NeedsDependencyVersion(live,Selected(live)))return;
            work=MakeWork(live);
            var link=GeneralNexusLink.Parse(work.Entry.DependencySource);
            if(link==null)return;
            var preset=state.SelectedPreset;
            var destination=state.Destination;
            var original=Safe.Json.Serialize(work.Entry);
            Func<bool> active=()=>!IsDisposed&&!exitApproved&&!working&&!scanning&&associationChoice==null&&
                projectDraft==null&&preset==state.SelectedPreset&&InventoryPaths.Equals(destination,state.Destination)&&
                state.List.Mods.Any(mod=>mod.Id==id&&Selected(mod).Id==work.Variant.Id&&Safe.Json.Serialize(mod)==original);
            if(!active())return;
            var ranges=PresetWorks().SelectMany(parent=>parent.Variant.Sources).Select(GeneralNexusLink.Parse)
                .Where(source=>source!=null&&publishedRequirements.ContainsKey(source.Url)).Select(source=>source.Url)
                .Distinct(StringComparer.Ordinal).SelectMany(source=>publishedRequirements[source])
                    .Select(group=>group.Where(range=>range.Source.Url==link.Url).ToArray()).Where(group=>group.Length>0).ToArray();
            var session=testModUpdateSession??(testMode?new NexusUpdateSession(null):NexusUpdateSession.Session);
            var result=await session.DependencyChoices(link,work.Entry.DependencyFile,ranges,()=>!IsDisposed);
            if(!active())return;
            var file=result.Automatic;
            if(file==null){
                using(var dialog=new GeneralDependencyFileDialog(work.Entry.Name,link,work.Entry.DependencyFile,result,launch)){
                    if(await dialog.ShowModelessAsync(this,id)!=DialogResult.OK||!active())return;
                    file=dialog.SelectedFile;
                }
            }
            if(file==null)return;
            var entry=state.List.Mods.Single(mod=>mod.Id==id);
            var updated=GeneralPresetProfiles.Copy(entry);
            var variant=updated.Variants.Single(option=>option.Id==work.Variant.Id);
            string url=new GeneralNexusLink{Game=link.Game,Mod=link.Mod,File=file.file_id}.Url;
            bool replaced=false;
            for(int i=0;i<variant.Sources.Count;i++){
                var old=GeneralNexusLink.Parse(variant.Sources[i]);
                if(old==null||old.Game!=link.Game||old.Mod!=link.Mod||old.File>0)continue;
                variant.SourceInfo.Remove(variant.Sources[i]);
                variant.Sources[i]=url;replaced=true;
            }
            if(!replaced)return;
            variant.Sources=variant.Sources.Distinct(StringComparer.Ordinal).ToList();
            if(variant.Name=="Indefinida"||variant.Name=="Undefined")variant.Name=file.version??T("Principal","Main");
            if(file.file_name!=null)variant.SourceInfo[url]=new GeneralSourceInfo{Name=entry.Name,Version=file.version,FileName=file.file_name,NexusChecked=true};
            BeginProjectEdit();
            GeneralPresetProfiles.Edit(state,entry,updated,variant.Id,false);
            FinishProjectEdit();SaveProjectEdits();DrawRows();
            await Scan();
        }finally{choosingDependencyFiles.Remove(id);}
    }
}

internal sealed class GeneralDependencyFileDialog : GeneralDialog {
    static string T(string pt,string en){return GeneralStyle.T(pt,en);}
    internal NexusUpdateSession.FileInfoItem SelectedFile;
    internal GeneralDependencyFileDialog(string name,GeneralNexusLink link,int pinned,NexusUpdateSession.DependencyFiles result,Action<string> open)
        :base(T("Escolher arquivo da dependência","Choose dependency file")){
        ClientSize=new System.Drawing.Size(620,275);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20,8,20,18),RowCount=4,ColumnCount=1};
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var title=GeneralModEditor.LabelFor(name);
        layout.Controls.Add(title,0,0);
        bool incompatible=result.Failure=="no-compatible-file";
        bool manual=result.Choices.Length==0&&!incompatible;
        string advice=incompatible?T("O Nexus não confirmou um arquivo compatível com os requisitos deste preset.","Nexus could not confirm a file compatible with the preset requirements."):manual?(result.Failure=="connect"||result.Failure=="authentication"
            ?T("Conecte ao Nexus para consultar os arquivos, ou abra a página e cole o link do arquivo abaixo.","Connect to Nexus to check the files, or open the page and paste the file link below.")
            :T("A consulta não confirmou um arquivo. Abra a lista do Nexus e cole o link do arquivo desejado.","The check did not confirm a file. Open the Nexus file list and paste the desired file link."))
            :T("Escolha o arquivo publicado que corresponde à dependência do seu mod.","Choose the published file matching your mod’s requirement.");
        var note=new Label{Text=advice,Dock=DockStyle.Fill,Margin=new Padding(0,8,0,6)};layout.Controls.Add(note,0,1);
        var picker=new GeneralPicker{Name="DependencyFiles",Dock=DockStyle.Top,Height=44,AccessibleName=T("Arquivos publicados","Published files")};
        var input=new TextBox{Name="DependencyFileLink",MaxLength=4096,AccessibleName=T("Link do arquivo","File link")};
        if(manual){layout.Controls.Add(GeneralTableDialogLayout.Input(input),0,2);picker.Dispose();}
        else if(incompatible){input.Dispose();picker.Dispose();}
        else{
            input.Dispose();
            picker.Items.AddRange(result.Choices.Select(f=>(object)(f.file_name+(String.IsNullOrWhiteSpace(f.version)?"":" · "+f.version))).ToArray());
            picker.SelectedIndex=result.Choices.Length==1?0:-1;layout.Controls.Add(picker,0,2);
        }
        Func<NexusUpdateSession.FileInfoItem> choice=()=>{
            if(incompatible)return null;
            if(!manual)return picker.SelectedIndex<0?null:result.Choices[picker.SelectedIndex];
            var value=GeneralNexusLink.Parse(input.Text.Trim());
            return value!=null&&value.Game==link.Game&&value.Mod==link.Mod&&value.File>0&&(pinned==0||pinned==value.File)
                ?new NexusUpdateSession.FileInfoItem{file_id=value.File}:null;
        };
        var actions=GeneralTableDialogLayout.Commands();
        var use=GeneralStyle.Button(T("Usar arquivo","Use file"),()=>{SelectedFile=choice();if(SelectedFile!=null)Complete(DialogResult.OK);},true);
        var cancel=GeneralStyle.Button(T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));
        actions.Controls.Add(use);actions.Controls.Add(cancel);actions.Controls.Add(GeneralStyle.Button(T("Ver arquivos no Nexus","View files on Nexus"),()=>open(link.Url+"?tab=files")));
        layout.Controls.Add(actions,0,3);Content.Controls.Add(layout);AcceptButton=use;CancelButton=cancel;
        if(manual)input.TextChanged+=(s,e)=>use.Enabled=choice()!=null;
        else if(!incompatible)picker.SelectedIndexChanged+=(s,e)=>use.Enabled=choice()!=null;
        use.Enabled=choice()!=null;
        GeneralStyle.Apply(this,GeneralStyle.Dark);title.Font=GeneralStyle.Font(13,true);
    }
}

internal sealed partial class GeneralModEditor {
    void ScheduleSourceInfo(){
        if(changing||attachment)return;sourceGeneration++;sourceDelay.Stop();sourceDelay.Start();
    }
    Task CurrentSourceInfo(){
        if(sourceQuery==null||queriedGeneration!=sourceGeneration){
            queriedGeneration=sourceGeneration;
            sourceQuery=UpdateSourceInfo();
        }
        return sourceQuery;
    }

    async Task QuickFillFromSource(){
        string source=sources.Text.Trim();
        if(finishing||current==null||refreshLookup==null||!GeneralModSourceMetadata.Supports(source)||
            SourceLinkWarning(source,current).Length>0)return;
        string caption=quickFill.Text;
        var choice=GeneralDialog.Message(this,T(
            "Completar preserva seus dados e adiciona o que falta.\r\nSobrescrever usa as informações retornadas pela fonte.",
            "Fill gaps keeps your data and adds what is missing.\r\nOverwrite uses information returned by the source."),
            caption,MessageBoxButtons.YesNoCancel,yesLabel:T("Completar","Fill gaps"),noLabel:T("Sobrescrever","Overwrite"));
        if(choice!=DialogResult.Yes&&choice!=DialogResult.No)return;
        sourceDelay.Stop();
        int generation=++sourceGeneration;
        queriedGeneration=generation;
        finishing=true;
        ValidateInputs();
        try{
            sourceQuery=UpdateSourceInfo(choice==DialogResult.No);
            await sourceQuery;
            if(!IsDisposed&&generation==sourceGeneration)await ReadNexusDependencies();
        }finally{
            finishing=false;
            if(!IsDisposed)ValidateInputs();
        }
    }

    async Task UpdateSourceInfo(bool? overwrite=null){
        if(current==null||attachment||IsDisposed)return;
        fileDetails.Text="";
        int generation=sourceGeneration;
        var variant=current;
        string source=sources.Text.Trim();
        if(source.Length>0){
            string warning=SourceLinkWarning(source,variant);
            if(warning.Length>0){sourceInfo.ForeColor=GeneralStyle.Danger;sourceInfo.Text=warning;return;}
        }
        var candidates=new[]{source}
            .Select(value=>new {Source=value,Nexus=GeneralNexusLink.Parse(value),Github=GeneralGithubLink.Parse(value)})
            .Where(item=>item.Nexus!=null||item.Github!=null||GeneralModDownload.IsArchivePath(item.Source))
            .GroupBy(item=>item.Nexus==null?item.Source:item.Nexus.Url).Select(group=>group.First()).Take(32).ToList();
        if(candidates.Count==0){sourceInfo.Text="";return;}
        bool overview=candidates.Any(item=>item.Nexus!=null&&item.Nexus.File==0);
        string advice=overview?GeneralNexusInfo.Advice+"\r\n":"";
        sourceInfo.ForeColor=overview?System.Drawing.ColorTranslator.FromHtml(GeneralStyle.Dark?"#ffbf80":"#9a4e00"):GeneralStyle.Muted;
        sourceInfo.Text=advice+T("Lendo informações do arquivo…","Reading file information…");
        GeneralSourceInfo preview=null;
        GeneralNexusLink previewLink=null;
        bool restricted=false,received=false;
        foreach(var item in candidates){
            GeneralSourceInfo known;
            if(variant.SourceInfo==null)variant.SourceInfo=new Dictionary<string,GeneralSourceInfo>();
            variant.SourceInfo.TryGetValue(item.Source,out known);
            GeneralSourceInfo fresh=null;
            try{
                if(item.Nexus==null&&known==null&&File.Exists(item.Source))
                    fresh=new GeneralSourceInfo{Name=Path.GetFileNameWithoutExtension(item.Source),FileName=Path.GetFileName(item.Source),Bytes=new FileInfo(item.Source).Length};
                else if(item.Nexus!=null||item.Github!=null){
                    var query=overwrite.HasValue?refreshLookup:lookup;
                    if(query!=null)fresh=await query(item.Source);
                }
            }catch(Exception){}
            if(IsDisposed||generation!=sourceGeneration||current!=variant)return;
            if(fresh!=null){
                ApplyReferenceMetadata(source,fresh);
                received|=fresh.Nsfw||!String.IsNullOrWhiteSpace(fresh.Name);
                if(overwrite==true&&(fresh.NsfwKnown||fresh.Nsfw)){
                    adultOverride=null;
                    SetAdult(fresh.Nsfw);
                }else if(fresh.Nsfw&&adultOverride==null)SetAdult(true);
                if(fresh.Tags!=null&&(overwrite.HasValue||known==null||!known.NexusChecked)){
                    if(overwrite==true)ClearTags();
                    MergeSourceTags(fresh.Tags);
                }
                if(overwrite.HasValue&&!String.IsNullOrWhiteSpace(fresh.Name)&&
                    (overwrite==true||String.IsNullOrWhiteSpace(modName.Text))){
                    lastAutoName=fresh.Name;
                    modName.Text=fresh.Name;
                }
                if(item.Nexus!=null&&item.Nexus.File>0&&!String.IsNullOrWhiteSpace(fresh.Version)&&
                    variants.Text.Trim()==T("Nova versão","New version"))
                {
                    variant.Name=fresh.Version;
                    SyncVariantPicker();
                    ValidateInputs();
                }
                restricted|=fresh.Nsfw&&String.IsNullOrWhiteSpace(fresh.Name);
            }

            var info=fresh!=null&&!String.IsNullOrWhiteSpace(fresh.Name)?fresh.SavedCopy():known;
            if(info==null)continue;
            if(known!=null&&fresh!=null&&!ReferenceEquals(info,known)){
                if(known.GithubChecked&&item.Github!=null) {
                    info.GithubChecked=true;info.GithubAssetUrl=known.GithubAssetUrl;
                    info.Version=known.Version;info.FileName=known.FileName;info.Bytes=known.Bytes;info.SizeApproximate=known.SizeApproximate;
                }
                info.FileName=info.FileName??known.FileName;
                info.Version=info.Version??known.Version;
                if(info.Bytes==0||info.SizeApproximate&&!known.SizeApproximate&&known.Bytes>0){
                    info.Bytes=known.Bytes;info.SizeApproximate=known.SizeApproximate;
                }
            }
            variant.SourceInfo[item.Source]=info;
            if(preview==null){preview=info;previewLink=item.Nexus;}
        }
        if(preview==null){
            sourceInfo.Text=advice+(restricted?
                T("O Nexus restringe os dados deste conteúdo adulto.","Nexus restricts metadata for this adult content."):
                T("Dados indisponíveis. Preencha o nome para continuar.","Metadata unavailable. Enter the name to continue."));
            return;
        }
        if(!overwrite.HasValue&&(String.IsNullOrWhiteSpace(modName.Text)||modName.Text==lastAutoName)){
            lastAutoName=preview.Name;
            modName.Text=preview.Name;
        }
        sourceInfo.Text=overwrite.HasValue&&!received
            ? advice+T("A fonte não retornou novos dados.","The source returned no new data.")
            : advice.TrimEnd();
        fileDetails.Text=SourceDetails(preview,previewLink);
        editorTips.SetToolTip(fileDetails,fileDetails.Text);
    }

    async Task ReadNexusDependencies(){
        const string caption="Nexus Mods";
        string source=sources.Text.Trim();
        var link=GeneralNexusLink.Parse(source);
        if(addDependency==null||link==null||link.File==0||!GeneralNexusInfo.HasTestKey)return;
        var variant=current;
        int generation=sourceGeneration;
        List<GeneralNexusRequirement[]> groups=null;
        try{groups=await GeneralNexusInfo.ReadRequirements(link);}
        catch(Exception){}
        if(IsDisposed||!Visible||generation!=sourceGeneration||current!=variant||groups==null||groups.Count==0)return;
        var exact=groups.Where(group=>group.Length==1&&group[0].MinFile==group[0].MaxFile).Select(group=>group[0])
            .Select(target=>{
                var matches=groupList.Mods.Where(mod=>mod.Variants.Count==1&&mod.Variants[0].Sources.Select(GeneralNexusLink.Parse).Any(item=>
                    item!=null&&item.Game==target.Source.Game&&item.Mod==target.Source.Mod&&item.File==target.MinFile)).Take(2).ToList();
                return matches.Count==1?matches[0]:null;
            }).Where(mod=>mod!=null&&mod.Id!=editingId&&!variant.RequiredMods.Contains(mod.Id)).Distinct().ToList();
        string summary=String.Join(Environment.NewLine,groups.Select(group=>String.Join(T(" ou "," or "),group.Select(target=>
            target.Name+" · "+(target.MinVersion??target.MinFile.ToString())+
            (target.MinFile==target.MaxFile?"":target.MaxFile==0?"+":" – "+(target.MaxVersion??target.MaxFile.ToString()))))));
        if(exact.Count==0){
            GeneralDialog.Message(this,summary+"\r\n\r\n"+T(
                "Use Verificar no projeto para revisar arquivos ausentes ou faixas de versão.",
                "Use Verify in the project to review missing files or version ranges."),caption);
            return;
        }
        if(GeneralDialog.Message(this,summary+"\r\n\r\n"+T("Vincular os mods com arquivo exato na sua lista?","Link the mods with an exact file in your list?")+"\r\n"+
            String.Join(", ",exact.Select(mod=>mod.Name)),caption,MessageBoxButtons.YesNo)==DialogResult.Yes)
            foreach(var mod in exact)AddRequiredMod(mod);
    }

    static string SourceDetails(GeneralSourceInfo info,GeneralNexusLink link){
        var details=new List<string>();

        if(!String.IsNullOrWhiteSpace(info.Version))details.Add(T("Versão do arquivo: ","File version: ")+info.Version);
        if(link==null&&!String.IsNullOrWhiteSpace(info.ModVersion)&&info.ModVersion!=info.Version)
            details.Add(T("Versão na origem: ","Version at source: ")+info.ModVersion);
        if(info.Bytes>0)details.Add(info.SizeApproximate?T("Aprox. ","About ")+GeneralStorageSize.Format(info.Bytes):
            GeneralStorageSize.Format(info.Bytes));
        if(link!=null&&link.File>0&&String.IsNullOrWhiteSpace(info.Version)&&info.Bytes==0)
            details.Add(T("Dados do arquivo indisponíveis.","File details unavailable."));
        return String.Join(Environment.NewLine,details);
    }
}
