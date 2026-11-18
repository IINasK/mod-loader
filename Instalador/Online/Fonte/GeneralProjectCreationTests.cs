using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunProjectCreationTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"create-project-");
        Directory.CreateDirectory(root);
        string game=Path.Combine(root,"game"),source=Path.Combine(root,"cover.png"),broken=Path.Combine(root,"invalid.png");
        Directory.CreateDirectory(game);File.WriteAllText(Path.Combine(game,".fixture-only"),"fixture");
        using(var image=new Bitmap(400,600))using(var graphics=Graphics.FromImage(image)){
            graphics.Clear(Color.SteelBlue);
            using(var font=new Font("Arial",32))graphics.DrawString("MEU JOGO",font,Brushes.White,new PointF(25,270));
            image.Save(source,System.Drawing.Imaging.ImageFormat.Png);
        }
        File.WriteAllText(broken,"not an image");
        var checks=new List<string>();Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};
        var store=new GeneralProjectStore(Path.Combine(root,"projects"));
        TestForm(catalog,new GeneralModState(),Path.Combine(root,"window"),root,async owner=>{
            bool animations=GeneralStyle.AnimationsEnabled;GeneralStyle.AnimationsEnabled=true;
            try{
                using(var dialog=new GeneralProjectCreationDialog())
                using(var timer=new Timer{Interval=40}){
                    Exception failure=null;
                    timer.Tick+=async(s,e)=>{
                        timer.Stop();
                        try{
                            var name=AllControls(dialog).OfType<TextBox>().Single(item=>item.Name=="ProjectNameInput");
                            var destination=AllControls(dialog).OfType<TextBox>().Single(item=>item.Name=="ProjectDestinationInput");
                            var save=(Button)dialog.AcceptButton;
                            var cover=AllControls(dialog).OfType<ProjectCoverPicker>().Single();
                            check(!save.Enabled,"O projeto novo começa sem nome ou pasta e não pode ser salvo vazio.");
                            CaptureUi(dialog,Path.Combine(root,"create-empty.png"));
                            name.Text="Meu jogo";destination.Text=source;save.PerformClick();
                            check(dialog.DialogResult!=DialogResult.OK&&store.Read().Projects.Count==0&&AllControls(dialog).Single(item=>item.Name=="ProjectCreationError").Text.Length>0,
                                "Uma pasta inválida mostra o erro no formulário sem criar um projeto.");
                            var enter=new DragEventArgs(new DataObject(DataFormats.FileDrop,new[]{source}),0,0,0,DragDropEffects.Copy,DragDropEffects.None);
                            typeof(Control).GetMethod("OnDragEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cover,new object[]{enter});
                            await Task.Delay(140);
                            check(enter.Effect==DragDropEffects.Copy&&!dialog.HasCover,"Arrastar uma imagem ativa a prévia sem gravá-la antes de soltar.");
                            CaptureUi(dialog,Path.Combine(root,"create-drag.png"));
                            typeof(Control).GetMethod("OnDragDrop",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(cover,new object[]{enter});
                            check(dialog.HasCover,"Soltar a imagem aplica a capa no formulário.");
                            dialog.SetCover(broken);
                            check(dialog.HasCover&&AllControls(dialog).Single(item=>item.Name=="ProjectCreationError").Text.Length>0,
                                "Uma imagem inválida não substitui a capa válida escolhida antes.");
                            destination.Text="\""+game+"\"";dialog.SetCover(source);
                            dialog.ClientSize=new Size(840,510);dialog.PerformLayout();Application.DoEvents();
                            var folder=AllControls(dialog).OfType<TableLayoutPanel>().Single(item=>item.Name=="ProjectDestinationRow");
                            var browse=folder.Controls.OfType<Button>().Single();
                            check(destination.Parent.Height==44&&browse.Height==44&&browse.PointToScreen(Point.Empty).Y==destination.Parent.PointToScreen(Point.Empty).Y,
                                "Pasta e Escolher pasta mantêm a mesma altura e alinhamento no formulário reduzido.");
                            CaptureUi(dialog,Path.Combine(root,"create-filled.png"));save.PerformClick();
                        }catch(Exception ex){failure=ex;dialog.Complete(DialogResult.Cancel);}
                    };
                    timer.Start();var answer=dialog.ShowDialog(owner);
                    if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                    check(answer==DialogResult.OK&&dialog.ResultName=="Meu jogo"&&dialog.ResultDestination==game,
                        "Salvar e continuar retorna o nome e normaliza a pasta copiada do Explorador.");
                    var created=store.Create(dialog.ResultName,dialog.ResultList,"dark","pt",dialog.ResultDestination,prepare:dialog.SaveCover);
                    string local=store.CoverPath(created);
                    check(File.Exists(local)&&store.Load(created).Destination==game,"A capa e a pasta são salvas com o cadastro do projeto.");
                    File.Move(source,source+".moved");
                    using(var image=ProjectCoverImage.ReadLocal(local))check(image!=null,"A capa local continua disponível sem o arquivo original.");
                    var clone=store.Duplicate(created,"Cópia do projeto",catalog);
                    check(File.Exists(store.CoverPath(clone))&&Safe.Hash(local)==Safe.Hash(store.CoverPath(clone)),
                        "Duplicar um projeto preserva sua capa local.");
                    using(var home=new GeneralProjectHome(catalog,new DownloadCache(Path.Combine(root,"cache")),store.Root,true)){
                        home.Show(owner);Application.DoEvents();
                        var card=AllControls(home).OfType<GeneralProjectHome.HomeProjectCard>().Single(item=>item.Project!=null&&item.Project.Id==created.Id);
                        check(card.HasCover,"A Home carrega a capa personalizada com a rede desativada.");
                        home.Close();
                    }
                }
                int count=store.Read().Projects.Count;
                using(var dialog=new GeneralProjectCreationDialog())
                using(var timer=new Timer{Interval=30}){
                    timer.Tick+=(s,e)=>{timer.Stop();dialog.SetCover(source+".moved");dialog.Complete(DialogResult.Cancel);};
                    timer.Start();check(dialog.ShowDialog(owner)==DialogResult.Cancel&&store.Read().Projects.Count==count,
                        "Cancelar o formulário com uma capa escolhida não registra um projeto.");
                }
                var entry=NewEntry("Mod importado","https://example.test/mod.zip");entry.Category="Roupas";
                var list=new GeneralModList{Mods=new List<GeneralModEntry>{entry},Categories=new List<string>{"Roupas"}};
                list.Presets.Add(new GeneralModPreset{Id="full",Name="Completo",ModIds=new List<string>{entry.Id},Categories=new List<string>{"Roupas"}});
                list.Presets.Add(new GeneralModPreset{Id="light",Name="Leve"});
                string definition=Path.Combine(root,"imported.json");
                new GeneralModLibrary(Path.Combine(root,"unused.json")).Export(definition,list);
                using(var dialog=new GeneralProjectCreationDialog())
                using(var timer=new Timer{Interval=30}){
                    Exception failure=null;
                    timer.Tick+=(s,e)=>{
                        timer.Stop();
                        try{
                            dialog.ImportDefinition(definition);
                            AllControls(dialog).OfType<TextBox>().Single(item=>item.Name=="ProjectDestinationInput").Text=game;
                            ((Button)dialog.AcceptButton).PerformClick();
                        }catch(Exception ex){failure=ex;dialog.Complete(DialogResult.Cancel);}
                    };
                    timer.Start();var answer=dialog.ShowDialog(owner);
                    if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                    check(answer==DialogResult.OK&&dialog.ResultList.Presets.Select(item=>item.Id).SequenceEqual(new[]{"full","light"})&&
                        dialog.ResultList.Mods.Single().Id==entry.Id&&dialog.ResultList.Categories.SequenceEqual(new[]{"Roupas"}),
                        "Importar no formulário preserva os mods, presets e categorias do arquivo.");
                    var imported=store.Create(dialog.ResultName,dialog.ResultList,"dark","pt",dialog.ResultDestination,prepare:dialog.SaveCover);
                    check(store.Load(imported).List.Presets.Count==2&&!File.Exists(store.CoverPath(imported)),
                        "Importar sem capa continua válido e não gera uma cópia de imagem vazia.");
                }
                check(ProjectCoverPicker.ImageDrop(new DataObject(DataFormats.FileDrop,new[]{definition}))==null&&
                    ProjectCoverPicker.ImageDrop(new DataObject(DataFormats.FileDrop,new[]{source,broken}))==null,
                    "O campo de capa recusa arquivos de projeto e múltiplos arquivos no mesmo arraste.");
            }finally{GeneralStyle.AnimationsEnabled=animations;}
            await Task.FromResult(0);
        });
        Safe.WriteJson(Path.Combine(root,"resultado-project-creation.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
