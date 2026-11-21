using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal interface IGeneralModMessageContext {
    IEnumerable<string> ModMessageNames {get;}
}

internal static class GeneralMessageText {
    internal static IEnumerable<string> NamesIn(GeneralModState state) {
        return state==null||state.List==null?Enumerable.Empty<string>():state.List.Mods.Select(mod=>mod.Name)
            .Concat(state.List.Presets.SelectMany(preset=>preset.Configurations.Values).Select(mod=>mod.Name));
    }
    internal static IEnumerable<string> ModNames(IWin32Window owner,IEnumerable<string> names=null) {
        var result=new List<string>(names??Enumerable.Empty<string>());
        var control=owner as Control;
        var window=owner as Form??(control==null?null:control.FindForm());
        for(int depth=0;window!=null&&depth<16;depth++,window=window.Owner) {
            var context=window as IGeneralModMessageContext;
            if(context!=null)result.AddRange(context.ModMessageNames??Enumerable.Empty<string>());
        }
        return result.Where(name=>!String.IsNullOrWhiteSpace(name)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static RichTextBox Create(string text,IEnumerable<string> names) {
        var font=GeneralStyle.Font(12);
        var body=new RichTextBox{Name="MessageText",ReadOnly=true,Multiline=true,BorderStyle=BorderStyle.None,
            Dock=DockStyle.Fill,ScrollBars=RichTextBoxScrollBars.None,DetectUrls=false,WordWrap=true,
            BackColor=GeneralStyle.Surface,ForeColor=GeneralStyle.Foreground,Font=font,Text=text??""};
        body.Disposed+=(s,e)=>font.Dispose();
        using(var bold=new Font(body.Font,FontStyle.Bold)) {
            string displayed=body.Text;
            foreach(string name in (names??Enumerable.Empty<string>()).Where(name=>!String.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(name=>name.Length)) {
                int start=0,index;
                while(start<displayed.Length&&(index=displayed.IndexOf(name,start,StringComparison.OrdinalIgnoreCase))>=0) {
                    int end=index+name.Length;start=end;
                    if(index>0&&Word(name[0])&&Word(displayed[index-1])||end<displayed.Length&&Word(name[name.Length-1])&&Word(displayed[end]))continue;
                    body.Select(index,name.Length);body.SelectionFont=bold;
                }
            }
        }
        body.Select(0,0);
        return body;
    }

    static bool Word(char value){return Char.IsLetterOrDigit(value)||value=='_';}
}
