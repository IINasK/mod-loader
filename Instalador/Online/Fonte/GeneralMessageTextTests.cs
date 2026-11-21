using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal static class GeneralMessageTextTests {
    sealed class Owner : Form, IGeneralModMessageContext {
        public IEnumerable<string> ModMessageNames {get{return new[]{"Mitsubishi Lancer Evolution IX MR","CET","Mod {teste} \\extra"};}}
    }
    internal static int Run(string parent) {
        string root=OnlineTests.NestedRoot(parent,"message-text-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        string theme=GeneralStyle.BlackTheme?"black":GeneralStyle.RedTheme?"light-red":GeneralStyle.Dark?"dark":"light";
        string language=GeneralStyle.Language;bool animations=GeneralStyle.AnimationsEnabled;
        try {
            GeneralStyle.AnimationsEnabled=false;GeneralStyle.Language="pt";
            using(var owner=new Owner{Location=new Point(-30000,-30000),ShowInTaskbar=false})
            using(var nested=new Form{Owner=owner}) {
                var names=GeneralMessageText.ModNames(nested);
                check(names.Contains("CET"),"Nested confirmations inherit the project mod-name context.");
                string message="CET; CET. RECETOR / Mod {teste} \\extra";
                using(var body=GeneralMessageText.Create(message,names)) {
                    check(body.Text==message,"Formatting preserves literal braces, slashes and the entire message.");
                    foreach(int index in new[]{0,5}){body.Select(index,3);check(body.SelectionFont!=null&&body.SelectionFont.Bold,"Each standalone mod-name mention is bold.");}
                    body.Select(message.IndexOf("RECETOR",StringComparison.Ordinal),7);
                    check(body.SelectionFont!=null&&!body.SelectionFont.Bold,"A mod acronym does not highlight part of an unrelated word.");
                    int literalIndex=message.IndexOf("Mod {teste}",StringComparison.Ordinal);body.Select(literalIndex,message.Length-literalIndex);
                    check(body.SelectionFont!=null&&body.SelectionFont.Bold,"Punctuation in a mod name remains literal while receiving emphasis.");
                    check(body.ReadOnly&&!body.DetectUrls,"Message text remains read-only without introducing clickable sources.");
                }
                foreach(string selected in new[]{"light","dark","black"}) {
                    GeneralStyle.UseTheme(selected);
                    string text="Mitsubishi Lancer Evolution IX MR\n\nA atualização também afeta outros projetos deste jogo.";
                    using(var dialog=GeneralDialog.CreateMessage(owner,text,"Atualizar mod",MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question,MessageBoxDefaultButton.Button2,yesLabel:"Atualizar",noLabel:"Cancelar",signal:false)) {
                        dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
                        dialog.Show();dialog.PerformLayout();Application.DoEvents();
                        var body=Children(dialog).OfType<RichTextBox>().Single();
                        body.Select(0,"Mitsubishi Lancer Evolution IX MR".Length);
                        check(body.SelectionFont!=null&&body.SelectionFont.Bold,"The real confirmation renders the mod name in bold: "+selected);
                        body.Select(body.Text.IndexOf("A atualização",StringComparison.Ordinal),12);
                        check(body.SelectionFont!=null&&!body.SelectionFont.Bold,"Only the mod name is emphasized: "+selected);
                        var buttons=Children(dialog).OfType<Button>().Where(button=>button.Text=="Atualizar"||button.Text=="Cancelar").ToArray();
                        check(buttons.Length==2&&buttons.All(button=>button.Visible)&&dialog.AcceptButton==dialog.CancelButton,
                            "Clear actions preserve the cancel-by-default decision: "+selected);
                        check(body.GetPositionFromCharIndex(body.TextLength-1).Y+body.Font.Height<=body.ClientSize.Height,
                            "The complete short confirmation fits above its actions: "+selected+"; body="+body.ClientSize+"; last="+body.GetPositionFromCharIndex(body.TextLength-1)+"; font="+body.Font.Height+"; dialog="+dialog.ClientSize);
                        check(dialog.ClientSize.Height<320,"The short message does not retain a large empty dialog: "+selected);
                        body.Select(0,0);
                        using(var capture=new Bitmap(dialog.Width,dialog.Height)) {
                            dialog.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));capture.Save(Safe.In(root,"message-shell-"+selected+".png"));
                        }
                        dialog.Complete(DialogResult.Cancel);
                    }
                    string failure=new ModUpdateFinalizationException("Compiler fixture",new IOException(
                        "O cadastro não foi salvo. Caminho recusado pelo scanner: engine/tools/compiler.exe")).Message;
                    using(var dialog=GeneralDialog.CreateMessage(owner,failure,"Concluir atualização",MessageBoxButtons.OK,
                        MessageBoxIcon.Warning,signal:false,modNames:new[]{"Compiler fixture"})){
                        dialog.StartPosition=FormStartPosition.Manual;dialog.Location=new Point(-30000,-30000);
                        dialog.Show();dialog.PerformLayout();Application.DoEvents();
                        var body=Children(dialog).OfType<RichTextBox>().Single();
                        int index=body.Text.IndexOf("Compiler fixture",StringComparison.Ordinal);body.Select(index,"Compiler fixture".Length);
                        check(body.SelectionFont!=null&&body.SelectionFont.Bold&&body.Text.Contains("F5")&&body.Text.Contains("sem baixar novamente"),
                            "The pending-update message emphasizes the mod and explains recovery without another download: "+selected);
                        check(body.GetPositionFromCharIndex(body.TextLength-1).Y+body.Font.Height<=body.ClientSize.Height,
                            "The recovery action and refused path fit above the popup buttons: "+selected);
                        body.Select(0,0);
                        using(var capture=new Bitmap(dialog.Width,dialog.Height)){
                            dialog.DrawToBitmap(capture,new Rectangle(Point.Empty,capture.Size));capture.Save(Safe.In(root,"update-pending-"+selected+".png"));
                        }
                        dialog.Complete(DialogResult.OK);
                    }
                }
            }
        }finally{GeneralStyle.UseTheme(theme);GeneralStyle.Language=language;GeneralStyle.AnimationsEnabled=animations;}
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,RealGameTouched=false,
            CaptureLimit="RichEdit text is omitted by DrawToBitmap; shell captures verify framing only. Text ranges, font styles and visible bounds are checked natively."});return checks.Count;
    }
    static IEnumerable<Control> Children(Control owner) {
        foreach(Control child in owner.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}
    }
}
