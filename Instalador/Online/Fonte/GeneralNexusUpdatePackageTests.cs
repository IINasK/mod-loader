using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;

internal static class GeneralNexusUpdatePackageTests {
    internal static void Run(string parent){
        string root=OnlineTests.NestedRoot(parent,"nexus-package-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,name)=>{if(!ok)throw new IOException(name);checks.Add(name);};
        string archive=Path.Combine(root,"update.zip");
        using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))
            using(var writer=new StreamWriter(zip.CreateEntry("mods/example/data.txt").Open()))writer.Write("new version");
        long bytes=new FileInfo(archive).Length;
        string md5;using(var input=File.OpenRead(archive))using(var algorithm=MD5.Create())md5=Safe.Hex(algorithm.ComputeHash(input));
        const string url="https://www.nexusmods.com/stardewvalley/mods/42?tab=files&file_id=120";
        var identity=GeneralNexusLink.Parse(url);
        string metadata=Safe.Json.Serialize(new{file_id=120,category_id=1,name="Example update",file_name="update.zip",version="2.0",size_in_bytes=bytes});
        Func<string,string,int,int,long,string> hashResponse=(game,version,mod,file,length)=>Safe.Json.Serialize(new[]{new{
            mod=new{domain_name=game,mod_id=mod},file_details=new{file_id=file,category_id=1,version=version,size_in_bytes=length}
        }});
        string match=hashResponse("stardewvalley","2.0",42,120,bytes);
        int lookups=0;bool leaseHeld=false;
        GeneralNexusPackageRequest request=(Uri uri,string credential,out HttpStatusCode status)=>{
            if(credential!="fixture-key")throw new IOException("The captured fixture credential was not reused.");
            status=HttpStatusCode.OK;
            if(uri.AbsoluteUri.EndsWith("/mods/42/files/120.json",StringComparison.Ordinal))return metadata;
            if(uri.AbsoluteUri!="https://api.nexusmods.com/v1/games/stardewvalley/mods/md5_search/"+md5+".json")
                throw new IOException("The hash lookup used the wrong game or bytes.");
            lookups++;
            try{using(var write=new FileStream(archive,FileMode.Open,FileAccess.Write,FileShare.ReadWrite)){} }
            catch(IOException){leaseHeld=true;}
            return match;
        };
        var target=GeneralNexusUpdatePackage.Read(url,"fixture-key",request).GetAwaiter().GetResult();
        check(target.Link.Game=="stardewvalley"&&target.Link.Mod==42&&target.Link.File==120&&target.Info.NexusChecked&&
            target.Info.Version=="2.0"&&target.Info.Bytes==bytes&&!target.Info.SizeApproximate,"Read exact target metadata without game-specific rules");
        var mutatedLink=target.Link;mutatedLink.File=999;var mutatedInfo=target.Info;mutatedInfo.FileName="wrong.zip";
        check(target.Link.File==120&&target.Info.FileName=="update.zip","Caller cannot mutate the verified target identity");
        var verified=target.Verify(archive).GetAwaiter().GetResult();
        check(verified.Sha256==Safe.Hash(archive)&&verified.Bytes==bytes&&verified.Path==archive&&leaseHeld&&lookups==1,
            "One leased archive is hashed, identified by Nexus and sealed with SHA-256");
        check(target.CandidatesInFolders(new[]{root}).SequenceEqual(new[]{archive}),"Named and sized archive is only a download candidate");
        string partial=Path.Combine(root,"update.zip.crdownload");File.Copy(archive,partial);
        check(target.CandidatesInFolders(new[]{root}).Count()==1,"Partial download suffixes are ignored");
        string duplicate=Path.Combine(root,"update (1).zip");File.Copy(archive,duplicate);
        check(target.CandidatesInFolders(new[]{root}).Count()==2,"Browser collision suffixes can select candidates");
        string numbered=Path.Combine(root,"variant (2).zip");File.Copy(archive,numbered);
        string numberedDuplicate=Path.Combine(root,"variant (2) (1).zip");File.Copy(archive,numberedDuplicate);
        var numberedTarget=GeneralNexusUpdatePackage.Read(url,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
            status=HttpStatusCode.OK;return metadata.Replace("update.zip","variant (2).zip");
        }).GetAwaiter().GetResult();
        check(numberedTarget.CandidatesInFolders(new[]{root}).Count()==2,
            "An author's numbered filename and a browser duplicate both remain valid candidates");
        string renamed=Path.Combine(root,"renamed.zip");File.Copy(archive,renamed);
        GeneralNexusPackageRequest renamedRequest=(Uri uri,string key,out HttpStatusCode status)=>{status=HttpStatusCode.OK;return uri.AbsolutePath.Contains("md5_search")?match:metadata;};
        var renamedTarget=GeneralNexusUpdatePackage.Read(url,"fixture-key",renamedRequest).GetAwaiter().GetResult();
        check(!renamedTarget.CandidatesInFolders(new[]{root}).Contains(renamed)&&renamedTarget.Verify(renamed).GetAwaiter().GetResult().Sha256==verified.Sha256,
            "Explicitly selected renamed archive is accepted by exact hash identity rather than filename");
        check(!GeneralNexusUpdatePackage.MatchesIdentity(hashResponse("fallout4","2.0",42,120,bytes),identity,bytes),"Same mod/file numbers in another game are rejected");
        check(!GeneralNexusUpdatePackage.MatchesIdentity(hashResponse("stardewvalley","2.0",43,120,bytes),identity,bytes),"Another mod cannot satisfy the update");
        check(!GeneralNexusUpdatePackage.MatchesIdentity(hashResponse("stardewvalley","2.0",42,121,bytes),identity,bytes),"Another variant/file cannot satisfy the update");
        check(!GeneralNexusUpdatePackage.MatchesIdentity(hashResponse("stardewvalley","2.0",42,120,bytes+1),identity,bytes),"Published exact byte size must match the archive");
        check(!GeneralNexusUpdatePackage.MatchesIdentity(match.Replace("\"domain_name\":\"stardewvalley\",",""),identity,bytes),"Missing game identity is not guessed");
        check(!GeneralNexusUpdatePackage.MatchesIdentity(match.Replace("\"category_id\":1","\"category_id\":6"),identity,bytes),"Deleted file identity is not authorized");
        check(!GeneralNexusUpdatePackage.MatchesIdentity("[]",identity,bytes)&&!GeneralNexusUpdatePackage.MatchesIdentity("{}",identity,bytes)&&
            !GeneralNexusUpdatePackage.MatchesIdentity("not json",identity,bytes),"Empty and malformed lookups do not authorize a package");
        check(GeneralNexusUpdatePackage.MatchesIdentity("["+hashResponse("fallout4","2.0",42,120,bytes).Trim('[',']')+","+match.Trim('[',']')+"]",identity,bytes),
            "An exact identity is found among all hash results, not just the first result");
        int wrongHashCalls=0;
        var wrong=GeneralNexusUpdatePackage.Read(url,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
            status=HttpStatusCode.OK;
            if(uri.AbsolutePath.Contains("md5_search")){wrongHashCalls++;return "[]";}return metadata;
        }).GetAwaiter().GetResult();
        Throws("wrong-file",()=>wrong.Verify(archive).GetAwaiter().GetResult());
        check(wrongHashCalls==1&&File.Exists(archive)&&Safe.Hash(archive)==verified.Sha256,"Even a correctly named/sized candidate must pass exact hash lookup");
        Throws("connect",()=>GeneralNexusUpdatePackage.Read(url,null,(Uri uri,string key,out HttpStatusCode status)=>{throw new Exception("No credential must not call the network.");}).GetAwaiter().GetResult());
        check(true,"No credential requests connection without making a network request");
        foreach(var state in new[]{new{Code=HttpStatusCode.Unauthorized,State="authentication"},new{Code=HttpStatusCode.Forbidden,State="authentication"},
            new{Code=(HttpStatusCode)429,State="rate-limit"},new{Code=(HttpStatusCode)0,State="unavailable"}}){
            Throws(state.State,()=>GeneralNexusUpdatePackage.Read(url,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{status=state.Code;return null;}).GetAwaiter().GetResult());
            check(true,"HTTP "+(int)state.Code+" is reported without accepting the archive");
        }
        var offline=GeneralNexusUpdatePackage.Read(url,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
            status=uri.AbsolutePath.Contains("md5_search")?(HttpStatusCode)0:HttpStatusCode.OK;
            return status==HttpStatusCode.OK?metadata:null;
        }).GetAwaiter().GetResult();
        Throws("unavailable",()=>offline.Verify(archive).GetAwaiter().GetResult());
        check(File.Exists(archive),"Offline hash verification preserves the downloaded archive and never falls back to a filename");
        foreach(int category in new[]{4,6,7}){
            Throws("unavailable",()=>GeneralNexusUpdatePackage.Read(url,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
                status=HttpStatusCode.OK;return metadata.Replace("\"category_id\":1","\"category_id\":"+category);
            }).GetAwaiter().GetResult());
        }
        check(true,"Old, deleted and archived update targets are not offered for installation");
        foreach(string unsafeMetadata in new[]{metadata.Replace("\"size_in_bytes\":"+bytes,"\"size_in_bytes\":-1"),
            metadata.Replace("update.zip","../update.zip"),metadata.Replace("update.zip","update.exe"),
            metadata.Replace("\"file_id\":120","\"file_id\":121")}){
            Throws("unavailable",()=>GeneralNexusUpdatePackage.Read(url,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
                status=HttpStatusCode.OK;return unsafeMetadata;
            }).GetAwaiter().GetResult());
        }
        check(true,"Invalid lengths, filenames, executable downloads and mismatched file metadata are rejected");
        Throws("wrong-file",()=>GeneralNexusUpdatePackage.Read("https://www.nexusmods.com/stardewvalley/mods/42","fixture-key",request).GetAwaiter().GetResult());
        check(true,"A mod page without an exact file is not a package target");
        string changed=Path.Combine(root,"changed.zip");File.WriteAllText(changed,"incomplete");
        Throws("incomplete",()=>target.Verify(changed).GetAwaiter().GetResult());
        check(true,"An incomplete archive is rejected before a hash request");
        bool active=true;
        GeneralNexusPackageRequest staleRequest=(Uri uri,string key,out HttpStatusCode status)=>{status=HttpStatusCode.OK;active=false;return metadata;};
        Cancelled(()=>GeneralNexusUpdatePackage.Read(url,"fixture-key",staleRequest,()=>active).GetAwaiter().GetResult());
        Cancelled(()=>target.Verify(archive,()=>false).GetAwaiter().GetResult());
        check(true,"Cancellation before or during a request discards stale results");
        string approximate=metadata.Replace("\"size_in_bytes\":"+bytes,"\"size_kb\":1");
        var oldApi=GeneralNexusUpdatePackage.Read(url,"fixture-key",(Uri uri,string key,out HttpStatusCode status)=>{
            status=HttpStatusCode.OK;return uri.AbsolutePath.Contains("md5_search")?match:approximate;
        }).GetAwaiter().GetResult();
        check(oldApi.Info.SizeApproximate&&oldApi.Verify(renamed).GetAwaiter().GetResult().Info.Bytes==bytes&&
            !oldApi.Verify(renamed).GetAwaiter().GetResult().Info.SizeApproximate,
            "Older KiB metadata stays approximate until the actual archive is identified and measured");
        Safe.WriteJson(Path.Combine(root,"result.json"),new{Passed=true,Checks=checks,LiveAuthenticatedNexus=false});
    }

    static void Throws(string state,Action action){
        try{action();}catch(NexusPackageException error){if(error.State==state)return;throw new IOException("Expected state "+state+", received "+error.State,error);}
        throw new IOException("Expected Nexus package failure "+state);
    }
    static void Cancelled(Action action){try{action();}catch(OperationCanceledException){return;}throw new IOException("Expected cancelled package lookup.");}
}
