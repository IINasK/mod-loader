using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

internal sealed class PackCoverageSegment {
    internal string Label;
    internal int Count;
    internal Color Color;
}
internal static class PackCoverage {
    static string paletteKey;
    static Dictionary<string,Color> palette;
    internal static List<PackCoverageSegment> Build(IEnumerable<GeneralModEntry> entries,Func<GeneralModEntry,bool> available,string uncategorized){
        var mods=entries.ToList();var segments=new List<PackCoverageSegment>();int count=mods.Count(available);
        if(count>0)segments.Add(new PackCoverageSegment{Label=GeneralStyle.T("Baixados / instalados","Downloaded / installed"),Count=count,Color=Color.FromArgb(19,122,77)});
        var categories=mods.Select(m=>m.Category??uncategorized).Distinct().OrderBy(c=>c,StringComparer.Ordinal).ToList();string key=String.Join("\n",categories);
        if(palette==null||paletteKey!=key){
            var colors=new Dictionary<string,Color>();
            foreach(string category in categories){
                var choices=Enumerable.Range(0,12).Select(i=>CategoryColor(category,i));
                colors[category]=colors.Count==0?choices.First():choices.OrderByDescending(color=>colors.Values.Min(other=>Distance(color,other))).First();
            }
            palette=colors;paletteKey=key;
        }
        foreach(var group in mods.Where(m=>!available(m)).GroupBy(m=>m.Category??uncategorized))segments.Add(new PackCoverageSegment{Label=group.Key,Count=group.Count(),Color=palette[group.Key]});
        return segments;
    }
    static int Distance(Color a,Color b){return (a.R-b.R)*(a.R-b.R)+(a.G-b.G)*(a.G-b.G)+(a.B-b.B)*(a.B-b.B);}
    internal static Color CategoryColor(string name,int shift=0){
        byte[] bytes;using(var sha=SHA256.Create())bytes=sha.ComputeHash(Encoding.UTF8.GetBytes(name.ToUpperInvariant()));
        double hue=((bytes[0]*256+bytes[1]+shift*103)%270+160)%360,c=.78,x=c*(1-Math.Abs(hue/60%2-1)),r=0,g=0,b=0;
        if(hue<60){r=c;g=x;}else if(hue<120){r=x;g=c;}else if(hue<180){g=c;b=x;}else if(hue<240){g=x;b=c;}else if(hue<300){r=x;b=c;}else{r=c;b=x;}
        return Color.FromArgb((int)((r+.12)*255),(int)((g+.12)*255),(int)((b+.12)*255));
    }
}
internal sealed partial class GeneralModWindow {
    void UpdatePackCoverage(){
        if(totalProgress==null)return;var preset=state.List.Presets.FirstOrDefault(p=>p.Id==state.SelectedPreset);
        Func<GeneralModEntry,bool> available=mod=>{string status;return statuses.TryGetValue(Key(mod,Selected(mod)),out status)&&(GeneralStatusPresentation.Kind(status)=="installed"||status=="Pronto para instalar"||status=="Instalando");};
        var mods=state.List.Mods.Where(mod=>preset==null||preset.ModIds.Contains(mod.Id)).GroupBy(GeneralModLibrary.ChoiceKey)
            .Select(choice=>choice.FirstOrDefault(available)??choice.First()).ToList();
        int count=mods.Count(available);totalProgress.Segments=PackCoverage.Build(mods,available,UncatName());totalProgress.Value=mods.Count==0?0:100*count/mods.Count;
        totalProgress.Caption=T("Mods baixados / instalados: ","Downloaded / installed mods: ")+count+" / "+mods.Count;totalProgress.Invalidate();
    }
}
