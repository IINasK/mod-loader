using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class GeneralBrandTests {
    internal static void Run(string parent){
        string root=Path.Combine(parent,"brand-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var checks=new List<string>();Action<bool,string> check=(yes,message)=>{if(!yes)throw new IOException(message);checks.Add(message);};
        var assembly=Assembly.GetExecutingAssembly();string svg;
        check(assembly.GetName().Version.ToString()==GeneralUpdateChecker.CurrentVersion+".0","Versão nativa do executável corresponde à versão do aplicativo.");
        using(var reader=new StreamReader(assembly.GetManifestResourceStream("ModLoader.Logo.svg")))svg=reader.ReadToEnd();
        check(svg.Contains("<path")&&!svg.Contains("<image")&&!svg.Contains("<rect")&&!svg.Contains("<script")&&!svg.Contains("data:")&&!svg.Contains("href="),"Logo vetorial não contém fundo sólido, raster ou recursos externos.");
        foreach(int size in new[]{16,24,32,48,64,128,256}){
            using(var bitmap=new Bitmap(size,size)){
                using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Transparent);ModLoaderBrand.Draw(graphics,new Rectangle(0,0,size,size));}
                int navy=0,cyan=0;
                for(int y=0;y<size;y++)for(int x=0;x<size;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.ToArgb()==ColorTranslator.FromHtml("#194660").ToArgb())navy++;if(pixel.ToArgb()==ColorTranslator.FromHtml("#76C5E3").ToArgb())cyan++;}
                check(navy>0&&cyan>0,"Logo mantém azul petróleo e azul claro ao renderizar a "+size+"px.");
                check(bitmap.GetPixel(size/2,0).A==0&&bitmap.GetPixel(0,size-1).A==0,"Logo mantém transparência real a "+size+"px.");
                if(size==256)bitmap.Save(Path.Combine(root,"logo-256.png"),System.Drawing.Imaging.ImageFormat.Png);
            }
            using(var stream=assembly.GetManifestResourceStream("ModLoader.Logo.ico")){
                if(size==256){
                    // Framework Icon selects 128px for this request; verify the PNG frame itself instead.
                    using(var reader=new BinaryReader(stream)){
                        check(reader.ReadUInt16()==0&&reader.ReadUInt16()==1&&reader.ReadUInt16()==7,"ICO declara sete quadros de imagem.");
                        stream.Position=6+16*6;int w=reader.ReadByte(),h=reader.ReadByte();stream.Position+=6;
                        int length=reader.ReadInt32(),offset=reader.ReadInt32();stream.Position=offset;
                        using(var png=new MemoryStream(reader.ReadBytes(length)))using(var bitmap=new Bitmap(png))
                            check(w==0&&h==0&&bitmap.Width==256&&bitmap.Height==256&&bitmap.GetPixel(0,0).A==0,"ICO contém PNG256 transparente e cabeçalho Windows correto.");
                    }
                }else using(var icon=new Icon(stream,new Size(size,size)))check(icon.Width==size&&icon.Height==size,"ICO oferece quadro nativo "+size+"px.");
            }
        }
        using(var icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)){
            check(icon!=null,"O executável tem ícone nativo reconhecido pelo Windows.");
            using(var bitmap=icon.ToBitmap()){
                bool red=false;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.B>pixel.R+30&&pixel.G>pixel.R)red=true;}
                check(red,"Ícone nativo do EXE usa a logo do projeto.");
                check(bitmap.GetPixel(0,0).A==0,"Ícone nativo do Windows tem fundo transparente.");
            }
        }
        foreach(string theme in new[]{"light","dark","light-red"}){
            GeneralStyle.UseTheme(theme);using(var bitmap=new Bitmap(128,128))using(var graphics=Graphics.FromImage(bitmap)){
                graphics.Clear(Color.Transparent);ModLoaderBrand.Draw(graphics,new Rectangle(0,0,128,128),true);
                bool accent=false,foreground=false;for(int y=0;y<128;y++)for(int x=0;x<128;x++){var pixel=bitmap.GetPixel(x,y);accent|=pixel.ToArgb()==GeneralStyle.Accent.ToArgb();foreground|=pixel.ToArgb()==GeneralStyle.Foreground.ToArgb();}
                check(accent&&foreground&&bitmap.GetPixel(0,0).A==0,"Logo acompanha a paleta e mantém transparência no tema "+theme);
                bitmap.Save(Path.Combine(root,"logo-"+theme+".png"),System.Drawing.Imaging.ImageFormat.Png);
            }
            using(var window=new Form())using(var panel=new Panel())using(var logo=new ModLoaderLogo()){
                panel.Controls.Add(logo);window.Controls.Add(panel);
                GeneralStyle.Apply(window,GeneralStyle.Dark);
                check(logo.BackColor==Color.Transparent,"Controle da logo preserva fundo transparente no tema "+theme);
                check(Object.ReferenceEquals(window.Icon,ModLoaderBrand.AppIcon),"Icone da janela acompanha o tema "+theme);
                using(var icon=window.Icon.ToBitmap()){
                    bool accent=false,foreground=false;
                    for(int y=0;y<icon.Height;y++)for(int x=0;x<icon.Width;x++){var c=icon.GetPixel(x,y);accent|=c.ToArgb()==GeneralStyle.Accent.ToArgb();foreground|=c.ToArgb()==GeneralStyle.Foreground.ToArgb();}
                    check(accent&&foreground&&icon.GetPixel(0,0).A==0,"Icone usa as duas cores e transparencia no tema "+theme);
                }
                panel.Size=new Size(128,128);panel.BackColor=Color.Magenta;logo.Size=new Size(64,64);window.CreateControl();panel.CreateControl();logo.CreateControl();
                using(var rendered=new Bitmap(64,64)){panel.DrawToBitmap(rendered,new Rectangle(0,0,64,64));check(rendered.GetPixel(0,0).ToArgb()==Color.Magenta.ToArgb(),"Logo deixa o fundo real do controle pai aparecer: "+theme+" / "+rendered.GetPixel(0,0).ToArgb());}
                GeneralStyle.UseTheme(theme=="dark"?"light":"dark");GeneralStyle.Apply(window,GeneralStyle.Dark);
                check(Object.ReferenceEquals(window.Icon,ModLoaderBrand.AppIcon)&&logo.BackColor==Color.Transparent,"Troca de tema atualiza icone e preserva alpha sem recriar janela: "+theme);
            }
        }
        GeneralStyle.UseTheme("light");
        Safe.WriteJson(Path.Combine(root,"resultado-brand.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
