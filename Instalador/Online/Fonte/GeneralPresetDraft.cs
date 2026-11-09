using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    Button deletePreset;
    GeneralModState projectDraft;
    Func<DialogResult> testProjectSaveChoice;

    void BeginProjectEdit(){
        if(projectDraft!=null||working)return;
        projectDraft=CopyProjectState();
        if(p2p!=null)p2p.SetPreparationState(projectDraft);
    }
    GeneralModState CopyProjectState(){
        var copy=Safe.Json.Deserialize<GeneralModState>(Safe.Json.Serialize(state));
        copy.TemplateId=state.TemplateId;copy.TemplateSubset=state.TemplateSubset;
        return copy;
    }
    GeneralModState CommittedProject(){
        if(projectDraft==null)return state;
        var saved=CopyProjectState();
        saved.List=projectDraft.List;
        saved.ChosenVariants=new Dictionary<string,string>(projectDraft.ChosenVariants);
        if(!saved.List.Presets.Any(preset=>preset.Id==saved.SelectedPreset))
            saved.SelectedPreset=projectDraft.SelectedPreset;
        foreach(var receipt in projectDraft.SnapshotReceipts)
            if(!saved.SnapshotReceipts.ContainsKey(receipt.Key))saved.SnapshotReceipts.Add(receipt.Key,receipt.Value);
        return saved;
    }
    void FinishProjectEdit(){
        if(projectDraft==null){Persist();return;}
        GeneralModLibrary.ValidateList(state.List,true);
        if(Safe.Json.Serialize(projectDraft.List)==Safe.Json.Serialize(state.List)&&
            Safe.Json.Serialize(projectDraft.ChosenVariants)==Safe.Json.Serialize(state.ChosenVariants))
            EndProjectEdit();
        UpdatePresetCommands();
    }
    void EndProjectEdit(){
        projectDraft=null;
        if(p2p!=null)p2p.SetPreparationState(state);
    }
    void SaveProjectEdits(){
        library.Save(state);startupIssue=null;
        EndProjectEdit();
        UpdatePresetCommands();
    }
    void DiscardProjectEdits(){
        if(projectDraft==null)return;
        var saved=CommittedProject();
        state.List=saved.List;state.ChosenVariants=saved.ChosenVariants;state.SelectedPreset=saved.SelectedPreset;
        state.SnapshotReceipts=saved.SnapshotReceipts;
        marked.IntersectWith(state.List.Mods.Select(mod=>mod.Id));
        statuses.Clear();statusDetails.Clear();progress.Clear();ResetColumnFilters();
        EndProjectEdit();
        RebuildPresets();
    }
    DialogResult AskProjectSave(){
        if(testMode)return testProjectSaveChoice==null?DialogResult.Yes:testProjectSaveChoice();
        return GeneralDialog.Message(this,T("Deseja salvar as alterações deste projeto?","Save the changes to this project?"),
            T("Alterações não salvas","Unsaved changes"),MessageBoxButtons.YesNoCancel,MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button1,null,null,T("Salvar","Save"),T("Descartar","Discard"));
    }
    bool ConfirmProjectNavigation(){
        if(projectDraft==null&&!DraftNameChanged)return true;
        switch(AskProjectSave()){
            case DialogResult.Yes:
                try{
                    if(DraftNameChanged)SavePresetName(false);
                    else SaveProjectEdits();
                    return true;
                }catch(Exception ex){ShowError(ex);return false;}
            case DialogResult.No:
                DiscardProjectEdits();CapturePresetDraft();UpdateTableTitle();
                return true;
            default:return false;
        }
    }
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
        presetSettings.Enabled=available&&valid&&(projectDraft!=null||(ActivePreset==null?marked.Any():DraftNameChanged||DraftSelectionChanged));
        if(deletePreset!=null)deletePreset.Enabled=available&&ActivePreset!=null&&!ProtectedPreset;
    }
    bool ConfirmPresetNavigation(){
        return !DraftNameChanged||ConfirmProjectNavigation();
    }
    void SaveCurrentPreset(){
        if(working||scanning)return;
        var selected=ActivePreset;
        if(projectDraft!=null&&!DraftNameChanged){
            SaveProjectEdits();
            detail.Text=T("Projeto salvo.","Project saved.");
            return;
        }
        if(selected==null||ProtectedPreset){if(marked.Any()&&(selected==null||DraftSelectionChanged))SavePresetSelection(true);return;}
        SavePresetName(true);
    }
    void SavePresetName(bool saveSelection){
        var selected=ActivePreset;
        string name=(draftName??"").Trim();
        if(name.Length==0||name.Length>120||name.Any(Char.IsControl))throw new IOException(T("Informe um nome válido para o preset.","Enter a valid preset name."));
        string previous=selected.Name;var members=selected.ModIds;
        selected.Name=name;if(saveSelection&&DraftSelectionChanged)selected.ModIds=PresetCheckedIds();
        try{SaveProjectEdits();}catch{selected.Name=previous;selected.ModIds=members;throw;}
        CapturePresetDraft();RebuildPresets();detail.Text=T("Preset salvo.","Preset saved.");
    }
}
