using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

public static class GameEdition {
    public const string Marker="archive/pc/ep1/ep1.addcont_keystone";
    public static string Status(FixedMod mod){return String.IsNullOrEmpty(mod.PhantomLibertyStatus)?(mod.RequiresPL?"required":"not-required"):mod.PhantomLibertyStatus;}
    public static bool NeedsPL(FixedMod mod){return mod.RequiresPL||Status(mod)!="not-required";}
    public static bool HasPL(string root){if(String.IsNullOrWhiteSpace(root))return false;string path=Safe.In(root,Marker);Safe.NoLinks(path);return File.Exists(path);}
    public static string Reason(FixedCatalog catalog,IEnumerable<string> ids){
        var issue=FixedData.Expand(catalog,ids).FirstOrDefault(NeedsPL);
        if(issue==null)return null;
        return issue.Name+(Status(issue)=="unverified"?": uso sem Phantom Liberty ainda não foi documentado.":": exige Phantom Liberty.")+" A seleção também considera suas dependências.";
    }
    public static void RequireAllowed(FixedCatalog catalog,IEnumerable<string> ids,bool withPL){if(withPL)return;string reason=Reason(catalog,ids);if(reason!=null)throw new IOException(reason);}
    public static string Category(FixedMod mod){return NeedsPL(mod)?Status(mod)=="unverified"?"Sem Phantom Liberty: compatibilidade não documentada":"Requer Phantom Liberty":"Com e sem Phantom Liberty";}
    public static void ValidateGame(string root,IEnumerable<FixedMod> mods){if(!HasPL(root)){var issue=mods.FirstOrDefault(NeedsPL);if(issue!=null)throw new IOException(issue.Name+": Phantom Liberty não foi encontrado. "+(Status(issue)=="unverified"?"A compatibilidade sem a expansão não foi documentada.":"Este componente exige a expansão."));}}
}

public sealed partial class OnlineWindow {
    readonly ComboBox gameEdition=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=220,AccessibleName="Edição do jogo",Name="GameEdition"};
    readonly Label editionNotice=new Label{AutoSize=true,MaximumSize=new System.Drawing.Size(1080,0),Margin=new Padding(0,4,0,4)};
    bool editionReady;
    volatile bool withPhantomLiberty=true;
    void AddEditionControls(FlowLayoutPanel tools){
        tools.Controls.Add(new Label{Text="Versão do jogo",AutoSize=true,Margin=new Padding(0,10,6,0)});
        gameEdition.Items.AddRange(new object[]{"Com Phantom Liberty","Sem Phantom Liberty"});gameEdition.SelectedIndex=0;
        tools.Controls.Add(gameEdition);actions.Add(gameEdition);
        gameEdition.SelectedIndexChanged+=(s,e)=>{withPhantomLiberty=gameEdition.SelectedIndex==0;if(editionReady)ApplyGameEdition();};
    }
    bool EditionEligible(string id){try{return withPhantomLiberty||catalog.Packages.Any(m=>m.Id==id)&&GameEdition.Reason(catalog,new[]{id})==null;}catch(IOException){return false;}}
    bool EditionGuard(IEnumerable<string> ids){try{RequireEdition(ids);return true;}catch(IOException ex){status.Text=ex.Message;return false;}}
    void RequireEdition(IEnumerable<string> ids){
        var selected=ids.ToList();if(!withPhantomLiberty&&selected.Any(id=>FacialById(id)!=null))throw new IOException("Esta preparação antiga ainda não tem classificação para o perfil sem Phantom Liberty.");
        GameEdition.RequireAllowed(catalog,selected.Where(id=>catalog.Packages.Any(m=>m.Id==id)),withPhantomLiberty);
    }
    void AutoDetectEdition(){if(preview||!editionReady)return;try{if(String.IsNullOrWhiteSpace(game.Text)||!File.Exists(Safe.In(game.Text,"bin/x64/Cyberpunk2077.exe")))return;gameEdition.SelectedIndex=GameEdition.HasPL(game.Text)?0:1;}catch(IOException ex){status.Text=ex.Message;}catch(ArgumentException ex){status.Text=ex.Message;}}
    void ApplyGameEdition(){
        ClearAutomaticQueue();grid.EndEdit();grid.CurrentCell=null;
        foreach(DataGridViewRow row in grid.Rows){bool known=catalog.Packages.Any(m=>m.Id==Convert.ToString(row.Tag))||FacialById(Convert.ToString(row.Tag))!=null;bool visible=!known||EditionEligible(Convert.ToString(row.Tag));if(!visible)row.Cells["Select"].Value=false;row.Visible=visible;}
        editionNotice.Text=withPhantomLiberty?"":"Sem Phantom Liberty: saves feitos com a expansão não abrem sem ela. Trocar este perfil não remove mods já instalados.";
        UpdateSelectAll();UpdateSummary();status.Text="Lista atualizada para "+gameEdition.Text+". A fila anterior foi cancelada; nenhum mod instalado foi removido.";
        OnlineDiagnostics.Record("ui.edition.changed",null,"Perfil de expansão alterado; fila cancelada e lista filtrada.",new{WithPhantomLiberty=withPhantomLiberty});
    }
}
