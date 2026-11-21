using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    const int FolderTitleInset=56,FolderChildIndent=62;

    sealed class FolderRow {
        internal GeneralModFolder Folder;
        internal string[] Members;
        internal string Size,Category;
    }

    readonly HashSet<string> collapsedFolders=new HashSet<string>(StringComparer.Ordinal);
    sealed class FolderIndent {
        internal CategoryBranch Branch;
        internal int Offset;
    }
    readonly Dictionary<DataGridViewRow,FolderIndent> folderBranches=new Dictionary<DataGridViewRow,FolderIndent>();
    string tableSelectionAnchor;
    DataGridViewRow markStrokeRow;
    bool markStrokeValue,markStrokeMoved;
    int markStrokeLast;
    Keys markStrokeModifiers;
    int groupStrokeStart=-1,groupStrokeLast;
    bool groupStrokeFolders,groupStrokeCollapse;
    readonly HashSet<string> groupStrokeTargets=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly HashSet<string> groupStrokeVersions=new HashSet<string>(StringComparer.Ordinal);

    IEnumerable<string> RowModIds(DataGridViewRow row){
        var id=row.Tag as string;
        if(id!=null)return new[]{id};
        var folder=row.Tag as FolderRow;
        return folder==null?Enumerable.Empty<string>():folder.Members;
    }

    IEnumerable<string> TableSelectionIds(){
        return grid.Rows.Cast<DataGridViewRow>().Where(row=>row.Visible).SelectMany(RowModIds).Distinct(StringComparer.Ordinal);
    }

    void NormalizeFolderMarks(string preferred=null){
        foreach(var folder in state.List.Folders.Where(folder=>folder.SingleSelection)){
            var selected=folder.ModIds.Where(marked.Contains).ToArray();
            if(selected.Length<2)continue;
            string keeper=selected.Contains(preferred)?preferred:selected[0];
            marked.ExceptWith(selected);
            marked.Add(keeper);
        }
    }

    string FolderActiveId(FolderRow folder){
        var member=state.List.Mods.FirstOrDefault(mod=>folder.Members.Contains(mod.Id)&&VersionInstalled(ListStatus(mod)));
        return member==null?null:member.Id;
    }

    IEnumerable<string> RowMarkIds(DataGridViewRow row){
        var folder=row.Tag as FolderRow;
        if(folder==null||!folder.Folder.SingleSelection)return RowModIds(row);
        var choice=folder.Members.FirstOrDefault(marked.Contains)??FolderActiveId(folder)??folder.Members.FirstOrDefault();
        return choice==null?Enumerable.Empty<string>():new[]{choice};
    }

    void RefreshTableMarks(string preferred=null){
        NormalizeFolderMarks(preferred);
        drawing=true;
        try{
            foreach(var row in ModRows()){
                bool selected=marked.Contains((string)row.Tag);
                if(!Equals(row.Cells["Mark"].Value,selected))row.Cells["Mark"].Value=selected;
                row.DefaultCellStyle.BackColor=selected||row.Index==hoveredRow?GeneralStyle.Selection:GeneralStyle.Surface;
            }
            foreach(DataGridViewRow row in grid.Rows)if(row.Visible&&row.Tag is FolderRow)grid.InvalidateRow(row.Index);
        }finally{drawing=false;}
        UpdateCounts();
    }

    void SelectTableRow(DataGridViewRow row,Keys modifiers,bool checkbox){
        var ids=RowMarkIds(row).ToArray();
        if(ids.Length==0)return;
        bool extend=(modifiers&Keys.Shift)!=0;
        bool toggle=(modifiers&Keys.Control)!=0;
        bool clearing=(checkbox||toggle)&&ids.All(marked.Contains);
        if(extend){
            var anchors=grid.Rows.Cast<DataGridViewRow>().Where(candidate=>candidate.Visible&&
                RowModIds(candidate).Contains(tableSelectionAnchor));
            var anchor=anchors.FirstOrDefault(candidate=>candidate.Tag is string)??anchors.FirstOrDefault()??row;
            if(!toggle)marked.Clear();
            ChangeMarksBetween(anchor.Index,row.Index,true);
        }else{
            if(!checkbox&&!toggle)marked.Clear();
            if(clearing)marked.ExceptWith(ids);else marked.UnionWith(ids);
            tableSelectionAnchor=ids[0];
        }
        string id=row.Tag as string;
        updateFocusedId=id!=null&&marked.Contains(id)?id:null;
        RefreshTableMarks(id);
    }

    void ChangeMarksBetween(int first,int last,bool value){
        for(int index=Math.Min(first,last);index<=Math.Max(first,last);index++){
            var row=grid.Rows[index];
            if(!row.Visible)continue;
            if(value)marked.UnionWith(RowMarkIds(row));else marked.ExceptWith(RowModIds(row));
        }
    }

    void BeginMarkStroke(DataGridViewRow row,Keys modifiers){
        markStrokeRow=row;
        markStrokeLast=row.Index;
        markStrokeValue=!RowMarkIds(row).All(marked.Contains);
        markStrokeModifiers=modifiers;
        markStrokeMoved=false;
    }

    void MoveMarkStroke(int index){
        if(markStrokeRow==null||markStrokeRow.DataGridView!=grid||associationChoice!=null||
            index<0||index==markStrokeLast)return;
        if((markStrokeModifiers&(Keys.Control|Keys.Shift))!=0)return;
        markStrokeMoved=true;
        ChangeMarksBetween(markStrokeLast,index,markStrokeValue);
        markStrokeLast=index;
        updateFocusedId=null;
        RefreshTableMarks(grid.Rows[index].Tag as string);
    }

    void EndMarkStroke(int rowIndex){
        var row=markStrokeRow;
        markStrokeRow=null;
        if(row==null||row.DataGridView!=grid||associationChoice!=null)return;
        if(!markStrokeMoved&&row.Index==rowIndex)SelectTableRow(row,markStrokeModifiers,true);
        else if(markStrokeMoved)tableSelectionAnchor=RowModIds(row).FirstOrDefault();
    }

    void SetFolderIndent(DataGridViewRow row,CategoryBranch branch,int offset){
        folderBranches[row]=new FolderIndent{Branch=branch,Offset=offset};
        row.Cells["Name"].Style.Padding=new Padding(10+offset,0,8,0);
    }

    bool GroupArrowAt(DataGridViewRow row,int column,int offset){
        if(column<0)return false;
        if(row.Tag is FolderRow||row.Cells["Variant"].Tag is List<Work>){
            FolderIndent indent;
            int start=folderBranches.TryGetValue(row,out indent)?indent.Offset:0;
            return column==grid.Columns["Name"].Index&&offset>=start&&offset<=start+24;
        }
        var category=row.Tag as SectionRow;
        return category!=null&&category.Kind=="heading"&&CanToggleCategory&&
            grid.GetCellDisplayRectangle(column,row.Index,false).X+offset>=CategoryContentRight-CategoryToggleWidth;
    }

    void BeginGroupStroke(DataGridViewRow row){
        var folder=row.Tag as FolderRow;
        var category=row.Tag as SectionRow;
        groupStrokeFolders=category==null;
        groupStrokeCollapse=category!=null?!CategoryCollapsed(category):folder!=null
            ?!collapsedFolders.Contains(folder.Folder.Id):expandedVersions.Contains(DrawerKey(ModFromRow(row)));
        groupStrokeStart=groupStrokeLast=row.Index;
        groupStrokeTargets.Clear();
        groupStrokeVersions.Clear();
        AddGroupStrokeTarget(row);
    }

    void AddGroupStrokeTarget(DataGridViewRow row){
        if(!row.Visible)return;
        if(groupStrokeFolders){
            var folder=row.Tag as FolderRow;
            if(folder!=null)groupStrokeTargets.Add(folder.Folder.Id);
            else if(row.Cells["Variant"].Tag is List<Work>)groupStrokeVersions.Add(DrawerKey(ModFromRow(row)));
        }else{
            var category=row.Tag as SectionRow;
            if(category!=null&&category.Kind=="heading")groupStrokeTargets.Add(category.Category??"");
        }
    }

    void MoveGroupStroke(int index){
        if(groupStrokeStart<0||index<0||index==groupStrokeLast)return;
        for(int current=Math.Min(groupStrokeLast,index);current<=Math.Max(groupStrokeLast,index);current++)
            AddGroupStrokeTarget(grid.Rows[current]);
        groupStrokeLast=index;
    }

    void EndGroupStroke(int index){
        if(index<0&&groupStrokeStart==groupStrokeLast){groupStrokeStart=-1;return;}
        MoveGroupStroke(index);
        groupStrokeStart=-1;
        var collapsed=groupStrokeFolders?collapsedFolders:collapsedCategories;
        if(groupStrokeCollapse){
            collapsed.UnionWith(groupStrokeTargets);
            expandedVersions.ExceptWith(groupStrokeVersions);
        }else{
            collapsed.ExceptWith(groupStrokeTargets);
            expandedVersions.UnionWith(groupStrokeVersions);
        }
        DrawRows();
    }

    void AddFolderedRows(IEnumerable<GeneralModEntry> entries,string previous,bool picking,bool categoryCollapsed,
        bool branches,ILookup<string,GeneralModEntry> membersByFolder,Dictionary<string,GeneralModFolder> foldersByMod,
        HashSet<string> emitted){
        var items=entries.ToList();
        for(int index=0;index<items.Count;index++){
            var mod=items[index];
            GeneralModFolder folder;
            if(picking||!foldersByMod.TryGetValue(mod.Id,out folder)){
                var branch=index==0?CategoryBranch.First:CategoryBranch.Middle;
                if(index==items.Count-1)branch|=CategoryBranch.Last;
                AddModRow(mod,previous,picking,categoryCollapsed,branches?(CategoryBranch?)branch:null);
                continue;
            }
            if(!emitted.Add(folder.Id))continue;
            var members=membersByFolder[folder.Id].ToArray();
            if(branches){
                var visible=members.ToDictionary(member=>member.Id,StringComparer.Ordinal);
                members=folder.ModIds.Where(visible.ContainsKey).Select(id=>visible[id]).ToArray();
            }
            bool folded=collapsedFolders.Contains(folder.Id);
            var row=grid.Rows[grid.Rows.Add()];
            var group=new FolderRow{Folder=folder,Category=mod.Category,Members=members.Select(member=>member.Id).ToArray()};
            row.Tag=group;
            row.Cells["Name"].Value=folder.Name;
            SetFolderSpace(row,group);
            row.ReadOnly=true;
            row.Visible=!categoryCollapsed;
            for(int child=0;child<members.Length;child++){
                var member=members[child];
                var branch=child==0?CategoryBranch.First:CategoryBranch.Middle;
                if(child==members.Length-1)branch|=CategoryBranch.Last;
                AddModRow(member,previous,false,categoryCollapsed||folded,null,branch);
            }
        }
    }



    void ToggleModFolder(FolderRow folder){
        if(!collapsedFolders.Add(folder.Folder.Id))collapsedFolders.Remove(folder.Folder.Id);
        DrawRows();
        var parent=grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(row=>row.Visible&&
            row.Tag is FolderRow&&((FolderRow)row.Tag).Folder.Id==folder.Folder.Id);
        if(parent!=null)grid.CurrentCell=parent.Cells["Name"];
    }

    async void EditModFolder(GeneralModFolder folder,IEnumerable<string> selected=null,string targetId=null){
        const string popup="mod-folder";
        if(working||scanning||associationChoice!=null||GeneralDialog.FocusOpen<GeneralNameDialog>(this,popup))return;
        var openedState=state;
        var openedList=state.List;
        string presetId=state.SelectedPreset;
        var ids=(selected??TableSelectionIds().Where(marked.Contains)).Distinct(StringComparer.Ordinal).ToList();
        var target=targetId==null?null:openedList.Mods.FirstOrDefault(mod=>mod.Id==targetId);
        if(folder==null&&(ids.Count<2||targetId!=null&&target==null))return;
        string targetCategory=target==null?null:target.Category;
        string initialName=folder!=null?folder.Name:target==null?"":String.Concat(target.Name.Take(120));
        Func<string,string> findFolder=id=>openedList.Folders.Where(item=>item.ModIds.Contains(id)).Select(item=>item.Id).FirstOrDefault();
        var previousFolders=ids.Select(findFolder).ToArray();
        try{
            using(var name=new GeneralNameDialog(folder==null?T("Criar pasta","Create folder"):T("Renomear pasta","Rename folder"),
                initialName,fieldLabel:T("Nome da pasta","Folder name"),chooseSelectionMode:folder==null)){
                if(await name.ShowModelessAsync(this,popup)!=DialogResult.OK||working||scanning||
                    !PopupProjectIsCurrent(openedState,openedList,presetId))return;
                if(folder==null){
                    if(!previousFolders.SequenceEqual(ids.Select(findFolder))||
                        !ids.All(id=>openedList.Mods.Any(mod=>mod.Id==id)))return;
                    var created=new GeneralModFolder{Id="folder-"+Guid.NewGuid().ToString("N"),Name=name.ResultName,
                        ModIds=ids,SingleSelection=name.SingleSelection};
                    if(target!=null){
                        var liveTarget=openedList.Mods.SingleOrDefault(mod=>mod.Id==targetId);
                        if(liveTarget==null||liveTarget.Category!=targetCategory)return;
                        MoveRegisteredMods(ids.Where(id=>id!=targetId),targetId,true,targetCategory,newFolder:created);
                        return;
                    }
                    BeginProjectEdit();
                    AddModFolder(created);
                }else{
                    if(!openedList.Folders.Contains(folder)||folder.Name!=initialName)return;
                    BeginProjectEdit();
                    folder.Name=name.ResultName;
                }
                FinishProjectEdit();DrawRows();
            }
        }catch(Exception ex){if(!IsDisposed)ShowError(ex);}
    }

    void AddModFolder(GeneralModFolder folder){
        var members=new HashSet<string>(folder.ModIds,StringComparer.Ordinal);
        foreach(var previous in state.List.Folders)previous.ModIds.RemoveAll(members.Contains);
        state.List.Folders.Add(folder);
    }

    void DissolveModFolder(GeneralModFolder folder){
        if(folder==null||working||scanning||associationChoice!=null)return;
        BeginProjectEdit();
        state.List.Folders.Remove(folder);
        collapsedFolders.Remove(folder.Id);
        FinishProjectEdit();DrawRows();
    }

    FolderRow FocusedFolder(){return grid.CurrentRow==null?null:grid.CurrentRow.Tag as FolderRow;}

    GeneralModEntry ModFromRow(DataGridViewRow row){
        return state.List.Mods.Single(mod=>mod.Id==(string)row.Tag);
    }

    static void PaintGroupIcon(Graphics graphics,int x,int middle,bool expanded){
        LucideIcons.Draw(graphics,expanded?"down":"right",new Rectangle(x+7,middle-8,16,16),GeneralStyle.Muted);
        LucideIcons.Draw(graphics,"folder",new Rectangle(x+28,middle-10,20,20),GeneralStyle.Accent);
    }

    void PaintModFolder(DataGridViewRowPrePaintEventArgs e,FolderRow folder){
        e.Handled=true;
        var bounds=e.RowBounds;
        var viewport=Rectangle.FromLTRB(0,grid.ColumnHeadersHeight,CategoryContentRight,grid.ClientSize.Height);
        var paint=Rectangle.Intersect(Rectangle.Intersect(bounds,viewport),e.ClipBounds);
        if(paint.Width<=0||paint.Height<=0)return;
        var saved=e.Graphics.Save();
        try{
            e.Graphics.IntersectClip(paint);
            using(var fill=new SolidBrush(e.RowIndex==hoveredRow||grid.CurrentRow==grid.Rows[e.RowIndex]?GeneralStyle.Selection:GeneralStyle.Surface))
                e.Graphics.FillRectangle(fill,bounds);
            var mark=grid.GetCellDisplayRectangle(grid.Columns["Mark"].Index,e.RowIndex,false);
            var name=grid.GetCellDisplayRectangle(grid.Columns["Name"].Index,e.RowIndex,false);
            int selected=folder.Members.Count(marked.Contains);
            var checkState=selected==0?System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal:
                selected==folder.Members.Length||folder.Folder.SingleSelection?System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal:
                System.Windows.Forms.VisualStyles.CheckBoxState.MixedNormal;
            var checkSize=CheckBoxRenderer.GetGlyphSize(e.Graphics,checkState);
            CheckBoxRenderer.DrawCheckBox(e.Graphics,new Point(mark.X+(mark.Width-checkSize.Width)/2,bounds.Y+(bounds.Height-checkSize.Height)/2),checkState);
            int middle=bounds.Y+bounds.Height/2;
            PaintGroupIcon(e.Graphics,name.X,middle,!collapsedFolders.Contains(folder.Folder.Id));
            var size=grid.GetCellDisplayRectangle(grid.Columns["Size"].Index,e.RowIndex,false);
            const TextFormatFlags flags=TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsClipping;
            int textWidth=Math.Max(1,name.Width-FolderTitleInset-8);
            int left=name.X+FolderTitleInset;
            string activeId=FolderActiveId(folder);
            string active=activeId==null?null:state.List.Mods.Single(mod=>mod.Id==activeId).Name;
            int titleHeight=active==null?bounds.Height:bounds.Height/2+2;
            TextRenderer.DrawText(e.Graphics,folder.Folder.Name,optionSelectedFont,
                new Rectangle(left,bounds.Y,textWidth,titleHeight),GeneralStyle.Foreground,flags);
            if(active!=null)TextRenderer.DrawText(e.Graphics,active,groupIndicatorFont,
                new Rectangle(left,bounds.Y+titleHeight,textWidth,bounds.Height-titleHeight),GeneralStyle.Accent,flags);
            size.Inflate(-8,0);
            TextRenderer.DrawText(e.Graphics,folder.Size,grid.DefaultCellStyle.Font,size,GeneralStyle.Muted,flags|TextFormatFlags.Right);
        }finally{e.Graphics.Restore(saved);}
    }
}
