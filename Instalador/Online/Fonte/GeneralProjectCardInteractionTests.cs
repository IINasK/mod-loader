using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunProjectCardInteractionTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"project-card-");Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(ok,message)=>{if(!ok)throw new IOException(message);checks.Add(message);};
        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};
        var store=new GeneralProjectStore(Path.Combine(root,"projects"));
        GeneralProject project;
        using(var cover=new Bitmap(300,450)){
            using(var graphics=Graphics.FromImage(cover)){
                graphics.Clear(Color.MidnightBlue);
                for(int y=0;y<450;y+=20)for(int x=0;x<300;x+=20)
                    if((x/20+y/20)%2==0)graphics.FillRectangle(Brushes.DarkCyan,x,y,20,20);
            }
            project=store.Create("Meu projeto",new GeneralModList(),"black","pt",prepare:data=>ProjectCoverImage.Save(cover,data));
        }
        string definition=Path.Combine(store.DataPath(project),"lista.json"),definitionHash=Safe.Hash(definition);
        string replacement=Path.Combine(root,"replacement.png");
        using(var cover=new Bitmap(200,300)){using(var graphics=Graphics.FromImage(cover))graphics.Clear(Color.Goldenrod);cover.Save(replacement,System.Drawing.Imaging.ImageFormat.Png);}
        TestForm(catalog,new GeneralModState(),Path.Combine(root,"window"),root,async owner=>{
            bool animations=GeneralStyle.AnimationsEnabled;
            using(var home=new GeneralProjectHome(catalog,new DownloadCache(Path.Combine(root,"cache")),store.Root,true)){
                home.ShowInTaskbar=false;home.StartPosition=FormStartPosition.Manual;home.Location=new Point(-30000,-30000);
                home.Show(owner);Application.DoEvents();GeneralStyle.AnimationsEnabled=true;
                try{
                    foreach(string theme in new[]{"light","light-red","dark","black"}){
                        store.SavePreferences(theme,"pt",root);home.Build();home.PerformLayout();await Task.Delay(40);
                        var menu=home.Controls.Find("HomeMenu",true).Single();menu.Focus();
                        var card=HomeCards(home,"SavedProjects").Single();card.SetPointerOver(false);await Until(()=>card.InteractionAmount==0);
                        var bounds=card.Bounds;var slot=card.Parent.Bounds;
                        check(!card.RemoveButton.Visible&&!card.EditButton.Visible,"Os controles ficam ocultos em repouso: "+theme);
                        using(var before=new Bitmap(card.Width,card.Height)){
                            card.DrawToBitmap(before,new Rectangle(Point.Empty,before.Size));
                            card.SetPointerOver(true);await Task.Delay(40);
                            check(card.InteractionAmount>0&&card.InteractionAmount<1,"O destaque progride suavemente em vez de saltar: "+theme);
                            await Until(()=>card.InteractionAmount==1);
                            check(card.RemoveButton.Visible&&card.EditButton.Visible&&card.Bounds==bounds&&card.Parent.Bounds==slot,
                                "O destaque revela X e opções sem deslocar o card: "+theme);
                            int changed=0,art=card.Width*3/2;
                            using(var after=new Bitmap(card.Width,card.Height)){
                                card.DrawToBitmap(after,new Rectangle(Point.Empty,after.Size));
                                for(int y=70;y<art-20;y+=4)for(int x=65;x<card.Width-20;x+=4)
                                    if(before.GetPixel(x,y).ToArgb()!=after.GetPixel(x,y).ToArgb())changed++;
                                var edge=after.GetPixel(3,art/2);
                                check(changed>100&&edge.R>230&&edge.G>230&&edge.B>230,
                                    "A imagem amplia dentro da máscara e a borda fica branca: "+theme);
                            }
                            CaptureHomeGallery(home,Path.Combine(root,"card-hover-"+theme+".png"));
                            typeof(Control).GetMethod("OnMouseEnter",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(card.RemoveButton,new object[]{EventArgs.Empty});
                            await Task.Delay(30);
                            check(card.RemoveButton.Visible&&card.EditButton.Visible&&card.InteractionAmount==1,
                                "Passar do card para o X mantém os controles estáveis: "+theme);
                            menu.Focus();card.SetPointerOver(false);await Until(()=>card.InteractionAmount==0);
                            check(!card.RemoveButton.Visible&&!card.EditButton.Visible,"Sair do card recolhe os controles e o zoom: "+theme);
                            using(var restored=new Bitmap(card.Width,card.Height)){
                                card.DrawToBitmap(restored,new Rectangle(Point.Empty,restored.Size));
                                check(restored.GetPixel(3,art/2).ToArgb()==before.GetPixel(3,art/2).ToArgb(),
                                    "A borda original reaparece sem resíduo de branco: "+theme);
                            }
                            CaptureHomeGallery(home,Path.Combine(root,"card-idle-"+theme+".png"));
                            card.SetPointerOver(true);await Until(()=>card.InteractionAmount>0);float partial=card.InteractionAmount;
                            card.SetPointerOver(false);
                            check(card.InteractionAmount==partial,"Reverter conserva a posição visual atual: "+theme);
                            await Until(()=>card.InteractionAmount<partial);
                            check(card.InteractionAmount<partial,"Uma saída durante a entrada reverte a animação sem reiniciar no extremo: "+theme);
                            await Until(()=>card.InteractionAmount==0);
                            GeneralStyle.AnimationsEnabled=false;card.SetPointerOver(true);
                            check(card.InteractionAmount==1&&card.RemoveButton.Visible,"A preferência sem animações conserva os controles: "+theme);
                            card.SetPointerOver(false);check(card.InteractionAmount==0,"Sem animações, a saída é imediata: "+theme);
                            GeneralStyle.AnimationsEnabled=true;
                        }
                        var plus=(GeneralProjectHome.HomeProjectCard)home.Controls.Find("NewProjectCard",true).Single();
                        plus.SetPointerOver(true);plus.ToggleChoices();Application.DoEvents();
                        var choices=home.Controls.Find("NewProjectChoices",true).Single();
                        check(choices.Visible&&choices.BackColor.A==0,"A área das opções usa fundo transparente: "+theme);
                        using(var image=new Bitmap(plus.Width,plus.Height)){
                            plus.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                            var outside=image.GetPixel(choices.Left+choices.Width/2,choices.Top-5);
                            check(image.GetPixel(choices.Left+2,choices.Top+2).ToArgb()==outside.ToArgb()&&
                                image.GetPixel(choices.Left+choices.Width/2,choices.Top+48).ToArgb()==outside.ToArgb(),
                                "Os cantos e o espaço entre os botões mostram a mesma superfície do card: "+theme);
                        }
                        CaptureHomeGallery(home,Path.Combine(root,"choices-transparent-"+theme+".png"));
                        var inside=Message.Create(choices.Handle,0x201,IntPtr.Zero,new IntPtr((2<<16)|2));
                        check(!Application.FilterMessage(ref inside)&&choices.Visible,"Clicar dentro do card mantém as opções e não consome o clique: "+theme);
                        var outsideControl=home.Controls.Find("HomeIntro",true).Single();
                        var outsideClick=Message.Create(outsideControl.Handle,0x201,IntPtr.Zero,new IntPtr((2<<16)|2));
                        check(!Application.FilterMessage(ref outsideClick)&&!choices.Visible,
                            "Clicar fora em um texto sem foco fecha as opções e preserva o clique: "+theme);
                        plus.ToggleChoices();var gallery=home.Controls.Find("ProjectGallery",true).Single();
                        outsideClick=Message.Create(gallery.Handle,0x201,IntPtr.Zero,IntPtr.Zero);
                        Application.FilterMessage(ref outsideClick);
                        check(!choices.Visible,"Clicar no espaço vazio da galeria também fecha as opções: "+theme);
                        plus.ToggleChoices();var wheel=Message.Create(gallery.Handle,0x20a,IntPtr.Zero,IntPtr.Zero);
                        Application.FilterMessage(ref wheel);
                        check(!choices.Visible,"Rolar fecha as opções e devolve o sinal de mais: "+theme);
                        plus.SetPointerOver(false);
                        var ready=HomeCards(home,"ReadyProjects").Single();
                        check(ready.RemoveButton==null&&ready.EditButton==null,"O projeto pronto conserva sua proteção de edição e exclusão: "+theme);
                    }
                    var edited=HomeCards(home,"SavedProjects").Single();edited.SetPointerOver(true);
                    string oldCover=Safe.Hash(store.CoverPath(project));Exception failure=null;
                    using(var timer=new Timer{Interval=40}){
                        timer.Tick+=(s,e)=>{
                            var dialog=Application.OpenForms.OfType<GeneralProjectCreationDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();
                            try{
                                check(AllControls(dialog).OfType<TextBox>().Single(item=>item.Name=="ProjectDestinationInput").ReadOnly,
                                    "O atalho de edição mantém a instalação como referência.");
                                AllControls(dialog).OfType<TextBox>().Single(item=>item.Name=="ProjectNameInput").Text="Nome alterado";
                                dialog.SetCover(replacement);((Button)dialog.AcceptButton).PerformClick();
                            }catch(Exception ex){failure=ex;dialog.Complete(DialogResult.Cancel);}
                        };
                        timer.Start();edited.EditButton.PerformClick();
                    }
                    if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                    var saved=store.Read().Projects.Single();
                    check(saved.Id==project.Id&&saved.Name=="Nome alterado"&&Safe.Hash(definition)==definitionHash&&Safe.Hash(store.CoverPath(project))!=oldCover,
                        "Os três pontinhos salvam nome e capa mantendo a identidade, os mods e a instalação.");
                    string indexBefore=store.IndexFingerprint();oldCover=Safe.Hash(store.CoverPath(project));
                    edited=HomeCards(home,"SavedProjects").Single();edited.SetPointerOver(true);
                    using(var timer=new Timer{Interval=40}){
                        timer.Tick+=(s,e)=>{
                            var dialog=Application.OpenForms.OfType<GeneralProjectCreationDialog>().FirstOrDefault();if(dialog==null)return;timer.Stop();
                            AllControls(dialog).OfType<TextBox>().Single(item=>item.Name=="ProjectNameInput").Text="Não salvar";dialog.Complete(DialogResult.Cancel);
                        };
                        timer.Start();edited.EditButton.PerformClick();
                    }
                    check(store.IndexFingerprint()==indexBefore&&Safe.Hash(store.CoverPath(project))==oldCover&&Safe.Hash(definition)==definitionHash,
                        "Cancelar a edição preserva nome, capa e dados do projeto.");
                }finally{GeneralStyle.AnimationsEnabled=animations;home.Close();}
            }
        });
        Safe.WriteJson(Path.Combine(root,"resultado-project-card.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
