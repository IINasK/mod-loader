using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

public sealed class FixedTextChange { public string Old,New; }
public sealed class FixedTextPatch { public string SourceSha256; public long SourceSize; public List<FixedTextChange> Changes; }

public static class FpvCustomization {
    const string Prefix="bin/x64/plugins/cyber_engine_tweaks/mods/FPVDrone/fpv/";
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
    public static void Validate(FixedMod mod,FixedFile file) {
        var patch=file.Patch;if(patch==null)return;
        if(mod.Id!="mod-31642"||mod.Version!="1.31.4"||file.UserData||!new[]{"settings.lua","profile.lua","physics_model.lua"}.Any(n=>file.Path==Prefix+n)||mod.Assets.Single(a=>a.Id==file.Asset).Format!="zip")throw new IOException("Personalização fora dos scripts FPV autorizados.");
        if(!FixedData.HashValid(patch.SourceSha256)||patch.SourceSha256==file.Sha256||patch.SourceSize<=0||patch.SourceSize>524288||file.Size<=0||file.Size>524288||patch.Changes==null||patch.Changes.Count==0||patch.Changes.Count>8)throw new IOException("Identidade da personalização FPV inválida.");
        foreach(var change in patch.Changes)if(change==null||String.IsNullOrEmpty(change.Old)||change.New==null||change.Old==change.New||change.Old.Length>65536||change.New.Length>65536)throw new IOException("Ajuste FPV inválido.");
    }
    public static bool IsOriginal(FixedFile file,long size,string hash){return file.Patch!=null&&size==file.Patch.SourceSize&&hash==file.Patch.SourceSha256;}
    public static bool KnownHash(FixedFile file,string hash){return file.Sha256==hash||(file.Patch!=null&&file.Patch.SourceSha256==hash)||(file.CanonicalSha256!=null&&(file.CanonicalSha256==hash||(file.PriorApprovedHashes??new string[0]).Contains(hash)));}
    public static bool MatchesFile(FixedFile file,long size,string hash){return size==file.Size&&hash==file.Sha256||IsOriginal(file,size,hash)||file.CanonicalSha256!=null&&(size==file.CanonicalSize&&hash==file.CanonicalSha256||(file.PriorApprovedHashes??new string[0]).Contains(hash));}
    public static bool UpgradePair(FixedFile file,string before,string after){return file.Patch!=null&&before==file.Patch.SourceSha256&&after==file.Sha256;}
    public static MemoryStream Apply(Stream input,FixedFile file) {
        var patch=file.Patch;if(patch==null)throw new IOException("Personalização não declarada.");
        byte[] original;
        using(var buffer=new MemoryStream()){
            byte[] block=new byte[16384];int count;long total=0;
            while((count=input.Read(block,0,block.Length))>0){total+=count;if(total>patch.SourceSize||total>524288)throw new IOException("Fonte FPV maior que a versão aprovada.");buffer.Write(block,0,count);}
            original=buffer.ToArray();
        }
        using(var sha=SHA256.Create())if(original.LongLength!=patch.SourceSize||Safe.Hex(sha.ComputeHash(original))!=patch.SourceSha256)throw new IOException("Script FPV diferente do original aprovado. Nenhum ajuste foi aplicado.");
        string text=Utf8.GetString(original);
        foreach(var change in patch.Changes){
            int first=text.IndexOf(change.Old,StringComparison.Ordinal);
            if(first<0||text.IndexOf(change.Old,first+change.Old.Length,StringComparison.Ordinal)>=0)throw new IOException("O contexto do ajuste FPV mudou. Nenhum arquivo do jogo foi alterado.");
            text=text.Substring(0,first)+change.New+text.Substring(first+change.Old.Length);
        }
        byte[] result=Utf8.GetBytes(text);
        using(var sha=SHA256.Create())if(result.LongLength!=file.Size||Safe.Hex(sha.ComputeHash(result))!=file.Sha256)throw new IOException("Resultado da personalização FPV não corresponde à versão aprovada.");
        return new MemoryStream(result,false);
    }
}
