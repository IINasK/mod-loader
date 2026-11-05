using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    Button discardPreset,deletePreset;
    bool hasPresetDraft,settingDraftName;
    string draftPresetId,draftName;
    HashSet<string> draftChecked=new HashSet<string>();
    bool ProtectedPreset {get{return project!=null&&project.TemplateId=="cyberpunk-2077"&&ActivePreset!=null&&seed.Presets.Any(item=>item.Id==ActivePreset.Id);}}
    bool DraftNameChanged {get{return !ProtectedPreset&&hasPresetDraft&&ActivePreset!=null&&draftPresetId==ActivePreset.Id&&draftName!=ActivePreset.Name;}}
    bool DraftSelectionChanged {get{return hasPresetDraft&&!draftChecked.SetEquals(marked);}}
    List<string> PresetCheckedIds(){return state.List.Mods.Where(mod=>marked.Contains(mod.Id)).Select(mod=>mod.Id).ToList();}
    void CapturePresetDraft(){hasPresetDraft=true;draftPresetId=state.SelectedPreset;draftName=ActivePreset==null?null:ActivePreset.Name;draftChecked=new HashSet<string>(marked);}
    void DraftTitleChanged(){if(settingDraftName||tableName==null)return;draftName=tableName.Text;UpdatePresetCommands();}
    void UpdatePresetCommands(){
        if(presetSettings==null)return;bool available=!working&&!scanning&&(p2p==null||!p2p.Installing);
        bool valid=ActivePreset==null||!String.IsNullOrWhiteSpace(draftName)&&draftName.Length<=120&&!draftName.Any(Char.IsControl);
        presetSettings.Enabled=available&&valid&&(ActivePreset==null?marked.Any():DraftNameChanged||DraftSelectionChanged);
        if(discardPreset!=null)discardPreset.Enabled=available&&(DraftNameChanged||DraftSelectionChanged);
        if(deletePreset!=null)deletePreset.Enabled=available&&ActivePreset!=null&&!ProtectedPreset;
    }
    bool ConfirmPresetNavigation(){
        if(!DraftNameChanged||testMode)return true;
        return GeneralDialog.Message(this,T("Descartar o nome do preset que ainda não foi salvo? Use o disquete para salvar ou o X para descartar.","Discard the unsaved preset name? Use the save icon to save or X to discard."),T("Alterações não salvas","Unsaved changes"),MessageBoxButtons.OKCancel)==DialogResult.OK;
    }
    void SaveCurrentPreset(){
        if(working||scanning)return;
        var selected=ActivePreset;
        if(selected==null||ProtectedPreset){if(marked.Any()&&(selected==null||DraftSelectionChanged))SavePresetSelection(true);return;}
        string name=(draftName??"").Trim();
        if(name.Length==0||name.Length>120||name.Any(Char.IsControl))throw new IOException(T("Informe um nome válido para o preset.","Enter a valid preset name."));
        string previous=selected.Name;var members=selected.ModIds;
        selected.Name=name;if(DraftSelectionChanged)selected.ModIds=PresetCheckedIds();
        try{Persist();}catch{selected.Name=previous;selected.ModIds=members;throw;}
        CapturePresetDraft();RebuildPresets();detail.Text=T("Preset salvo.","Preset saved.");
    }
    void DiscardPresetDraft(){
        if(working||scanning)return;
        marked.Clear();foreach(string id in draftChecked.Where(id=>state.List.Mods.Any(mod=>mod.Id==id)))marked.Add(id);
        CapturePresetDraft();UpdateTableTitle();DrawRows();detail.Text=T("Alterações do preset descartadas.","Preset changes discarded.");
    }
}
