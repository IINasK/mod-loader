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
        check(svg.Contains("<polygon")&&!svg.Contains("<image")&&!svg.Contains("<script")&&!svg.Contains("data:")&&!svg.Contains("href="),"Logo usa somente geometria vetorial embutida, sem raster ou recursos externos.");
        foreach(int size in new[]{16,24,32,48,64,128,256}){
            using(var bitmap=new Bitmap(size,size)){
                using(var graphics=Graphics.FromImage(bitmap)){graphics.Clear(Color.Magenta);ModLoaderBrand.Draw(graphics,new Rectangle(0,0,size,size));}
                int red=0,white=0;
                for(int y=0;y<size;y++)for(int x=0;x<size;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.R>200&&pixel.G<120&&pixel.B<150)red++;if(pixel.R>230&&pixel.G>230&&pixel.B>230)white++;}
                check(red>8&&white>0,"Logo mantém vermelho e branco ao renderizar a "+size+"px.");
                check(bitmap.GetPixel(size/2,0).ToArgb()!=Color.Magenta.ToArgb(),"Fundo vetorial foi renderizado sem corte a "+size+"px.");
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
                            check(w==0&&h==0&&bitmap.Width==256&&bitmap.Height==256,"ICO contém PNG256 íntegro e cabeçalho Windows correto.");
                    }
                }else using(var icon=new Icon(stream,new Size(size,size)))check(icon.Width==size&&icon.Height==size,"ICO oferece quadro nativo "+size+"px.");
            }
        }
        using(var icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath)){
            check(icon!=null,"O executável tem ícone nativo reconhecido pelo Windows.");
            using(var bitmap=icon.ToBitmap()){
                bool red=false;for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++){var pixel=bitmap.GetPixel(x,y);if(pixel.R>200&&pixel.G<120&&pixel.B<150)red=true;}
                check(red,"Ícone nativo do EXE usa a logo do projeto.");
            }
        }
        Safe.WriteJson(Path.Combine(root,"resultado-brand.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }
}
