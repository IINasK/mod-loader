using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    readonly Dictionary<GeneralButton,DataGridViewRow> categoryDeleteButtons=new Dictionary<GeneralButton,DataGridViewRow>();

    void RebuildCategoryDeleteButtons(){
        foreach(var button in categoryDeleteButtons.Keys.ToArray()){grid.Controls.Remove(button);button.Dispose();}
        categoryDeleteButtons.Clear();
        foreach(DataGridViewRow row in grid.Rows){
            var section=row.Tag as SectionRow;
            if(section==null||section.Kind!="heading"||section.Category==null)continue;
            string category=section.Category;
            var button=(GeneralButton)GeneralStyle.Button("",()=>RequestDeleteCategory(category));
            button.Name="DeleteCategory";button.Tag=category;button.Bare=true;button.IconKind="trash";button.Destructive=true;button.CompositeParentSurface=true;
            button.AutoSize=false;button.MinimumSize=new Size(44,44);button.Size=new Size(44,44);button.Margin=Padding.Empty;
            button.AccessibleName=T("Excluir categoria ","Delete category ")+category;
            tips.SetToolTip(button,button.AccessibleName);
            button.MouseEnter+=(s,e)=>SetHover(row.Index);
            button.MouseLeave+=(s,e)=>{
                var point=grid.PointToClient(Control.MousePosition);
                SetHover(grid.ClientRectangle.Contains(point)?grid.HitTest(point.X,point.Y).RowIndex:-1);
            };
            categoryDeleteButtons.Add(button,row);grid.Controls.Add(button);button.BringToFront();
        }
        UpdateCategoryDeleteButtons();
    }

    void UpdateCategoryDeleteButtons(){
        if(grid==null||grid.IsDisposed)return;
        int right=Math.Min(CategoryContentRight,grid.ClientSize.Width);
        int top=grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0;
        foreach(var pair in categoryDeleteButtons){
            var row=pair.Value;var button=pair.Key;
            if(row.DataGridView!=grid||!row.Visible){button.Visible=false;continue;}
            var rect=grid.GetRowDisplayRectangle(row.Index,false);
            button.Bounds=new Rectangle(right-CategoryToggleWidth-48,rect.Top+(rect.Height-44)/2,44,44);
            button.Visible=associationChoice==null&&rect.Top>=top&&rect.Bottom<=grid.ClientSize.Height;
            button.Enabled=!working&&!scanning&&associationChoice==null;
        }
    }

    async void RequestDeleteCategory(string category){
        if(working||scanning||associationChoice!=null||category==null||GeneralDialog.FocusOpen<GeneralCategoryRemovalDialog>(this))return;
        var openedState=state;
        var openedList=state.List;
        string presetId=state.SelectedPreset,destination=state.Destination;
        Func<GeneralModEntry[]> categoryMods=()=>openedList.Mods
            .Where(mod=>String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase)).ToArray();
        try{
            var members=categoryMods();
            var ids=members.Select(mod=>mod.Id).ToArray();
            Func<bool> stillCurrent=()=>PopupProjectIsCurrent(openedState,openedList,presetId)&&!working&&!scanning&&
                state.Destination==destination&&openedList.Categories.Contains(category,StringComparer.OrdinalIgnoreCase)&&
                ids.SequenceEqual(categoryMods().Select(mod=>mod.Id));
            using(var dialog=new GeneralCategoryRemovalDialog(category,members.Length)){
                var answer=await dialog.ShowModelessAsync(this);
                if((answer!=DialogResult.Yes&&answer!=DialogResult.No)||!stillCurrent())return;
                if(answer==DialogResult.Yes&&members.Length>0){
                    if((projectDraft!=null&&!ConfirmProjectNavigation())||!stillCurrent())return;
                    members=categoryMods();
                    string root=Root();
                    uninstalling=working=true;Busy(true);
                    try{
                        var jobs=await Task.Run(()=>members.SelectMany(VersionWorks).GroupBy(work=>work.Key).Select(group=>group.First())
                            .Where(work=>{
                                string status=Status(work,root);
                                return GeneralStatusPresentation.Kind(status)=="installed"||status==GeneralStatusPresentation.Disabled||
                                    status==GeneralModInstaller.Modified||status==GeneralModInstaller.Partial;
                            }).ToList());
                        await Task.Run(()=>{
                            var store=DisabledAt(root);
                            var disabled=new HashSet<string>(store.Read().Keys,StringComparer.Ordinal);
                            foreach(var work in jobs.Where(work=>disabled.Contains(PresetRecipeKey(work))))disabled.Add(work.Key);
                            RemoveManagedWorks(jobs,root,disabled,ReportUninstall,removeAttachments:true);
                            inventoryLinks=null;
                            store.Discard(jobs.Select(work=>PresetStoredKey(work,root)));
                            if(state.AppliedPreset!=null)new GeneralPresetDeployment(root,data,()=>EnsureGameClosed(root))
                                .RecordRemoval(jobs.Select(work=>PresetRecipeKey(work)));
                        });
                        Persist();
                    }finally{uninstalling=working=false;Busy(false);}
                }
                DeleteCategoryMetadata(category);
                if(answer==DialogResult.Yes)await Scan();
            }
        }catch(Exception ex){ShowError(ex);}
    }

    void DeleteCategoryMetadata(string category){
        BeginProjectEdit();
        GeneralModLibrary.DeleteCategory(state.List,category);
        collapsedCategories.Remove(category);
        ResetColumnFilters();FinishProjectEdit();DrawRows();
    }

    string[] OrganizationSelection(DataGridViewRow row){
        if(row==null)return new string[0];
        var version=row.Tag as Work;
        var ids=version==null?RowModIds(row).ToArray():new[]{version.Entry.Id};
        if(ids.Length==1&&marked.Contains(ids[0]))
            return state.List.Mods.Where(mod=>marked.Contains(mod.Id)).Select(mod=>mod.Id).ToArray();
        return ids;
    }

    void CategorizeMods(IEnumerable<string> ids,string category,bool redraw=true){
        if(working||scanning||associationChoice!=null)return;
        var selected=new HashSet<string>(ids,StringComparer.Ordinal);
        if(selected.Count==0)return;
        if(category!=null&&!state.List.Categories.Contains(category,StringComparer.OrdinalIgnoreCase))
            throw new IOException(T("A categoria não pertence ao projeto.","The category does not belong to this project."));
        BeginProjectEdit();
        foreach(var entry in state.List.Mods.Where(mod=>selected.Contains(mod.Id)).ToArray()){
            var updated=GeneralPresetProfiles.Copy(entry);
            updated.Category=category;
            foreach(var variant in updated.Variants.SelectMany(option=>option.WithNaturalOption()))variant.Category=null;
            GeneralPresetProfiles.Edit(state,entry,updated,Selected(entry).Id,false);
            entry.Category=category;
            foreach(var variant in entry.Variants.SelectMany(option=>option.WithNaturalOption()))variant.Category=null;
        }
        foreach(var folder in state.List.Folders.Where(folder=>folder.ModIds.Any(selected.Contains)&&!folder.ModIds.All(selected.Contains)))
            folder.ModIds.RemoveAll(selected.Contains);
        if(category!=null&&ActivePreset!=null&&!ActivePreset.Categories.Contains(category,StringComparer.OrdinalIgnoreCase))
            ActivePreset.Categories.Add(category);
        collapsedCategories.Remove(category??"");
        FinishProjectEdit();if(redraw)DrawRows();
    }

    const string OrganizationDragFormat="ModLoader.RegisteredMods";
    string[] organizationDragIds;
    string organizationDragToken,organizationDragFolderId,organizationTargetId,organizationTargetCategory,organizationTargetFolder;
    bool organizationAfter,organizationDragging;
    DataGridViewRow organizationGap,organizationFolderTarget;
    Point organizationFolderPoint;
    Bitmap organizationGhost;

    DataGridViewRow OrganizationDragRowAt(Point point){
        var hit=grid.HitTest(point.X,point.Y);
        if(hit.RowIndex<0||hit.ColumnIndex!=grid.Columns["Name"].Index)return null;
        var row=grid.Rows[hit.RowIndex];
        if(!(row.Tag is string)&&!(row.Tag is Work)&&!(row.Tag is FolderRow))return null;
        int offset=point.X-grid.GetCellDisplayRectangle(hit.ColumnIndex,hit.RowIndex,false).X;
        return GroupArrowAt(row,hit.ColumnIndex,offset)||UpdateHistoryBadgeAt(row,hit.ColumnIndex,offset)?null:row;
    }

    string[] OrganizationDragSelection(DataGridViewRow row){
        var folder=row.Tag as FolderRow;
        return folder==null?OrganizationSelection(row):folder.Folder.ModIds.ToArray();
    }

    void EnableOrganizationDrag(){
        Point down=Point.Empty;
        DataGridViewRow pressed=null;
        var timer=new Timer{Interval=30};
        int scrollTicks=0;
        grid.MouseDown+=(s,e)=>{
            pressed=null;
            if(e.Button!=MouseButtons.Left||working||scanning||associationChoice!=null||
                (Control.ModifierKeys&(Keys.Control|Keys.Shift))!=0)return;
            pressed=OrganizationDragRowAt(e.Location);
            down=e.Location;
        };
        grid.MouseUp+=(s,e)=>pressed=null;
        grid.MouseMove+=(s,e)=>{
            if(pressed==null||e.Button!=MouseButtons.Left||organizationDragging)return;
            var size=SystemInformation.DragSize;
            if(new Rectangle(down.X-size.Width/2,down.Y-size.Height/2,size.Width,size.Height).Contains(e.Location))return;
            var row=pressed;pressed=null;
            if(row.DataGridView!=grid)return;
            organizationDragIds=OrganizationDragSelection(row);
            if(organizationDragIds.Length==0)return;
            var sourceFolder=row.Tag as FolderRow;
            organizationDragFolderId=sourceFolder==null?null:sourceFolder.Folder.Id;
            var bounds=Rectangle.Intersect(grid.GetRowDisplayRectangle(row.Index,false),grid.ClientRectangle);
            if(bounds.Width>0&&bounds.Height>0){
                using(var image=new Bitmap(grid.Width,grid.Height)){
                    grid.DrawToBitmap(image,grid.ClientRectangle);
                    organizationGhost=image.Clone(bounds,PixelFormat.Format32bppArgb);
                }
            }
            organizationDragging=true;organizationDragToken=Guid.NewGuid().ToString("N");
            grid.CurrentCell=null;
            var moving=new HashSet<string>(organizationDragIds,StringComparer.Ordinal);
            foreach(DataGridViewRow candidate in grid.Rows){
                var group=candidate.Tag as FolderRow;
                if(group!=null&&group.Folder.Id==organizationDragFolderId||moving.Contains(OrganizationModId(candidate)))
                    candidate.Visible=false;
            }
            timer.Start();
            try{grid.DoDragDrop(new DataObject(OrganizationDragFormat,organizationDragToken),DragDropEffects.Move);}
            finally{
                timer.Stop();ClearOrganizationPreview();
                if(organizationGhost!=null){organizationGhost.Dispose();organizationGhost=null;}
                organizationDragIds=null;organizationDragToken=null;organizationDragFolderId=null;
                DrawRows();
                if(IsHandleCreated&&!IsDisposed)BeginInvoke((Action)(()=>organizationDragging=false));
                else organizationDragging=false;
            }
        };
        timer.Tick+=(s,e)=>{
            if(organizationDragIds==null){timer.Stop();return;}
            if(++scrollTicks%2==0){
                int first=grid.FirstDisplayedScrollingRowIndex;
                ScrollInventoryDrag(grid);
                if(first!=grid.FirstDisplayedScrollingRowIndex)ClearOrganizationFolderTarget();
            }
            if(organizationGap!=null&&organizationGap.DataGridView==grid)
                organizationGap.Height=Math.Min(44,organizationGap.Height+6);
            grid.Invalidate();
        };
        grid.Paint+=(s,e)=>{
            if(!organizationDragging)return;
            var point=grid.PointToClient(Cursor.Position);
            if(grid.ClientRectangle.Contains(point)&&organizationGhost!=null){
                int width=Math.Min(organizationGhost.Width,grid.ClientSize.Width-24);
                var ghostBounds=new Rectangle(12,point.Y-organizationGhost.Height/2,width,organizationGhost.Height);
                using(var attributes=new ImageAttributes()){
                    var matrix=new ColorMatrix();matrix.Matrix33=.82f;attributes.SetColorMatrix(matrix);
                    e.Graphics.DrawImage(organizationGhost,ghostBounds,0,0,organizationGhost.Width,organizationGhost.Height,GraphicsUnit.Pixel,attributes);
                }
            }
            if(organizationFolderTarget==null||organizationFolderTarget.DataGridView!=grid)return;
            var target=grid.GetRowDisplayRectangle(organizationFolderTarget.Index,true);
            var viewport=Rectangle.FromLTRB(0,grid.ColumnHeadersHeight,CategoryContentRight,grid.ClientSize.Height);
            target=Rectangle.Intersect(target,viewport);
            target.Inflate(-3,-2);
            if(target.Width<16||target.Height<16)return;
            var smoothing=e.Graphics.SmoothingMode;
            try{
                e.Graphics.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using(var outline=GeneralStyle.Round(target,8))
                using(var pen=new Pen(GeneralStyle.Accent,2))e.Graphics.DrawPath(pen,outline);
            }finally{e.Graphics.SmoothingMode=smoothing;}
        };
        grid.DragLeave+=(s,e)=>ClearOrganizationPreview();
        grid.Disposed+=(s,e)=>{timer.Dispose();if(organizationGhost!=null){organizationGhost.Dispose();organizationGhost=null;}};
    }

    static string OrganizationModId(DataGridViewRow row){
        var work=row.Tag as Work;
        return work==null?row.Tag as string:work.Entry.Id;
    }

    bool UpdateOrganizationDrop(Control target,DragEventArgs args){
        if(target!=grid||organizationDragIds==null||working||scanning||associationChoice!=null||
            !String.Equals(args.Data.GetData(OrganizationDragFormat) as string,organizationDragToken,StringComparison.Ordinal))return false;
        var point=grid.PointToClient(new Point(args.X,args.Y));
        if(organizationFolderTarget!=null&&organizationFolderTarget.DataGridView==grid&&organizationFolderPoint==point)return true;
        var hit=grid.HitTest(point.X,point.Y);
        if(hit.RowIndex<0){ClearOrganizationPreview();return false;}
        var row=grid.Rows[hit.RowIndex];
        if(row==organizationGap)return true;
        var section=row.Tag as SectionRow;
        string category,id=null,folder=null;
        bool after=false;
        DataGridViewRow folderBoundary=null;
        if(section!=null&&(section.Kind=="heading"||section.Kind=="add"||section.Kind=="subheading")){
            category=section.Kind=="subheading"?null:section.Category;
            if(section.Kind!="add"){
                var first=state.List.Mods.FirstOrDefault(mod=>!organizationDragIds.Contains(mod.Id)&&
                    String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase));
                id=first==null?null:first.Id;
            }
        }else{
            var group=row.Tag as FolderRow;
            id=group==null?OrganizationModId(row):group.Folder.ModIds.FirstOrDefault(member=>!organizationDragIds.Contains(member));
            var entry=state.List.Mods.FirstOrDefault(mod=>mod.Id==id);
            if(entry==null||organizationDragIds.Contains(id)){ClearOrganizationPreview();return false;}
            var bounds=grid.GetRowDisplayRectangle(row.Index,false);
            int edge=Math.Min(row.Height/3,10*grid.DeviceDpi/96);
            bool center=point.Y>=bounds.Top+edge&&point.Y<bounds.Bottom-edge;
            category=group==null?entry.Category:group.Category;
            if(organizationDragFolderId==null&&center){
                if(group!=null){
                    organizationTargetId=group.Folder.ModIds.Last(member=>!organizationDragIds.Contains(member));
                    organizationTargetCategory=category;
                    organizationTargetFolder=group.Folder.Id;
                    organizationAfter=true;
                    return HighlightOrganizationTarget(row,point);
                }
                if(row.Tag is string)return HighlightOrganizationTarget(row,point);
            }
            after=point.Y>=bounds.Top+row.Height/2;
            var parent=group==null?state.List.Folders.FirstOrDefault(item=>item.ModIds.Contains(id)):group.Folder;
            if(parent!=null&&(group!=null||organizationDragFolderId!=null)){
                var header=group!=null?row:grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(candidate=>candidate.Visible&&
                    candidate.Tag is FolderRow&&((FolderRow)candidate.Tag).Folder.Id==parent.Id);
                if(header==null){ClearOrganizationPreview();return false;}
                category=((FolderRow)header.Tag).Category;
                var members=new HashSet<string>(parent.ModIds,StringComparer.Ordinal);
                var ordered=state.List.Mods.Where(mod=>members.Contains(mod.Id)&&!organizationDragIds.Contains(mod.Id)&&
                    String.Equals(mod.Category??"",category??"",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(ordered.Length>0)id=(after?ordered.Last():ordered.First()).Id;
                folderBoundary=after?grid.Rows.Cast<DataGridViewRow>()
                    .Last(candidate=>candidate.Visible&&(candidate==header||members.Contains(OrganizationModId(candidate)))):header;
            }else folder=parent==null?null:parent.Id;
        }
        ClearOrganizationFolderTarget();
        if(organizationGap!=null&&organizationTargetId==id&&organizationTargetCategory==category&&
            organizationTargetFolder==folder&&organizationAfter==after)return true;
        ClearOrganizationGap();
        organizationTargetId=id;organizationTargetCategory=category;organizationTargetFolder=folder;organizationAfter=after;
        int index=row.Index;
        if(folderBoundary!=null)index=folderBoundary.Index+(after?1:0);
        else if(section!=null&&(section.Kind=="heading"||section.Kind=="subheading"))index++;
        else if(id!=null){
            var related=grid.Rows.Cast<DataGridViewRow>().Where(candidate=>candidate.Visible&&
                OrganizationModId(candidate)==id).ToArray();
            if(related.Length>0)index=after?related.Last().Index+1:related.First().Index;
        }
        var gap=new DataGridViewRow{Height=2,MinimumHeight=2,ReadOnly=true,Tag=new SectionRow("dropgap",null)};
        gap.CreateCells(grid);
        int inserted=Math.Min(index,grid.RowCount);
        grid.Rows.Insert(inserted,gap);
        organizationGap=grid.Rows[inserted];
        UpdateCategoryDeleteButtons();
        return true;
    }

    bool HighlightOrganizationTarget(DataGridViewRow row,Point point){
        ClearOrganizationGap();
        organizationFolderPoint=point;
        if(organizationFolderTarget!=row){
            organizationFolderTarget=row;
            grid.Invalidate();
        }
        return true;
    }

    void ClearOrganizationGap(){
        if(organizationGap!=null&&organizationGap.DataGridView==grid)grid.Rows.Remove(organizationGap);
        organizationGap=null;
    }

    void ClearOrganizationFolderTarget(){
        if(organizationFolderTarget==null)return;
        organizationFolderTarget=null;
        grid.Invalidate();
    }

    void ClearOrganizationPreview(){
        ClearOrganizationGap();
        ClearOrganizationFolderTarget();
    }

    void DropOrganizedMods(){
        if(organizationDragIds==null)return;
        var ids=organizationDragIds.ToArray();
        if(organizationFolderTarget!=null&&organizationFolderTarget.Tag is string){
            string targetId=(string)organizationFolderTarget.Tag;
            var projectState=state;
            ClearOrganizationPreview();
            BeginInvoke((Action)(()=>Guard(()=>{
                if(Object.ReferenceEquals(state,projectState))EditModFolder(null,new[]{targetId}.Concat(ids),targetId);
            })));
            return;
        }
        if(organizationGap==null&&organizationFolderTarget==null)return;
        string target=organizationTargetId,category=organizationTargetCategory,folder=organizationTargetFolder;
        bool after=organizationAfter;
        ClearOrganizationPreview();
        MoveRegisteredMods(ids,target,after,category,folder,movingFolder:organizationDragFolderId!=null);
    }

    void MoveRegisteredMods(IEnumerable<string> ids,string targetId,bool after,string category,string folderId=null,GeneralModFolder newFolder=null,bool movingFolder=false){
        if(working||scanning||associationChoice!=null)return;
        var moving=new HashSet<string>(ids,StringComparer.Ordinal);
        if(moving.Count==0||targetId!=null&&moving.Contains(targetId))return;
        var entries=state.List.Mods.Where(mod=>moving.Contains(mod.Id)).ToList();
        if(entries.Count==0||targetId!=null&&!state.List.Mods.Any(mod=>mod.Id==targetId))return;
        if(entries.Any(mod=>!String.Equals(mod.Category??"",category??"",StringComparison.OrdinalIgnoreCase)))
            CategorizeMods(moving,category,redraw:false);
        BeginProjectEdit();
        entries=state.List.Mods.Where(mod=>moving.Contains(mod.Id)).ToList();
        state.List.Mods.RemoveAll(mod=>moving.Contains(mod.Id));
        int index=targetId==null?state.List.Mods.FindLastIndex(mod=>String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase))+1:
            state.List.Mods.FindIndex(mod=>mod.Id==targetId)+(after?1:0);
        state.List.Mods.InsertRange(Math.Max(0,index),entries);
        var folder=state.List.Folders.FirstOrDefault(item=>item.Id==folderId);
        if(newFolder!=null)AddModFolder(newFolder);
        else if(!movingFolder){
            foreach(var other in state.List.Folders)other.ModIds.RemoveAll(moving.Contains);
            if(folder!=null){
                int at=folder.ModIds.IndexOf(targetId)+(after?1:0);
                folder.ModIds.InsertRange(Math.Max(0,at),entries.Select(mod=>mod.Id));
            }
        }
        orderColumn=null;reverseOrder=false;
        collapsedCategories.Remove(category??"");
        FinishProjectEdit();DrawRows();
    }

    internal static void RunOrganizationTests(string parent){
        string root=OnlineTests.NestedRoot(parent,"organization-");
        Directory.CreateDirectory(root);
        string game=Path.Combine(root,"g"),data=Path.Combine(root,"d");
        Directory.CreateDirectory(game);File.WriteAllText(Safe.In(game,".fixture-only"),"fixture");
        var catalog=new FixedCatalog{Packages=new List<FixedMod>()};
        var clothes=NewEntry("Clothes one",TestZip(root,"one.zip","mods/one/main.bin","first"));
        clothes.Category="Roupas";
        var second=NewEntry("Clothes two",TestZip(root,"two.zip","mods/two/main.bin","second"));
        second.Category="Roupas";
        second.Variants.Add(new GeneralModVariant{Id="alternative",Name="Alternative",Sources=new List<string>{second.Variants[0].Sources[0]}});
        var outside=NewEntry("Other mod",TestZip(root,"three.zip","mods/three/main.bin","other"));
        outside.Category="Outros";
        var uncategorized=NewEntry("No category","installed://local/default");
        var state=new GeneralModState{DownloadFolder=root,Language="pt"};
        state.List.Categories.AddRange(new[]{"Roupas","Outros"});
        state.List.Mods.AddRange(new[]{clothes,second,outside,uncategorized});
        var checks=new List<string>();
        Action<bool,string> check=(valid,message)=>{if(!valid)throw new IOException(message);checks.Add(message);};
        TestForm(catalog,state,data,root,async form=>{
            form.SetDestination(game);
            foreach(var entry in new[]{clothes,second,outside}){
                var work=form.MakeWork(entry);
                form.Generic(game).Install(work.Variant.Sources[0],work.Receipt(0),null);
            }
            await form.Scan();
            form.gameInventory=form.gameInventory.Concat(new[]{new P2PSyncDifference{Kind="found",Path="mods/unknown/module.bin",Mod="Unknown"}}).ToArray();
            form.DrawRows();
            var rows=form.grid.Rows.Cast<DataGridViewRow>().ToArray();
            int parentRow=Array.FindIndex(rows,row=>row.Tag is SectionRow&&((SectionRow)row.Tag).Kind=="heading"&&((SectionRow)row.Tag).Category==null);
            int unknown=Array.FindIndex(rows,row=>row.Tag is P2PSyncDifference);
            int uncat=Array.FindIndex(rows,row=>Equals(row.Tag,uncategorized.Id));
            check(parentRow>=0&&parentRow<unknown&&unknown<uncat,"Os mods desconhecidos e sem categoria devem ser filhos do mesmo grupo fixo, nessa ordem.");
            check(form.categoryDeleteButtons.Count==2&&form.categoryDeleteButtons.Keys.All(button=>button.Tag!=null),
                "A lixeira aparece nas categorias criadas, mantendo o grupo fixo do sistema.");
            check(Equals(form.ModRows().Single(row=>Equals(row.Tag,uncategorized.Id)).Cells["Origin"].Value,"Mod Loader"),
                "Os itens sem categoria mantêm sua identificação no Mod Loader.");
            CaptureUi(form,Path.Combine(root,"uncategorized-sections.png"));
            var rootHeading=form.grid.Rows[parentRow];
            var rootToggle=new Point(form.CategoryContentRight-28,form.grid.GetRowDisplayRectangle(rootHeading.Index,false).Top+22);
            ClickModCell(form.grid,rootToggle);
            check(form.grid.Rows.Cast<DataGridViewRow>().Where(row=>row.Tag is P2PSyncDifference||Equals(row.Tag,uncategorized.Id)).All(row=>!row.Visible),
                "Recolher o grupo fixo esconde os dois blocos juntos.");
            ClickModCell(form.grid,rootToggle);
            form.scanning=true;form.UpdateCategoryDeleteButtons();
            check(form.categoryDeleteButtons.Keys.All(button=>!button.Enabled),"Excluir categorias fica indisponível durante o scan.");
            form.scanning=false;form.UpdateCategoryDeleteButtons();
            form.grid.CurrentCell=form.ModRows().Single(row=>Equals(row.Tag,clothes.Id)).Cells["Name"];
            var menu=form.grid.ContextMenuStrip;
            menu.Show(form,new Point(20,20));
            var categoryMenu=menu.Items.OfType<ToolStripMenuItem>().Single(item=>item.Name=="CategorizeMods");
            categoryMenu.ShowDropDown();
            check(categoryMenu.DropDown.Items.Count==3,"O submenu oferece todas as categorias e o grupo fixo.");
            categoryMenu.DropDown.Items.Cast<ToolStripItem>().Single(item=>item.Text=="Outros").PerformClick();
            menu.Close();
            check(clothes.Category=="Outros"&&second.Category=="Roupas"&&
                File.ReadAllText(Safe.In(game,"mods/one/main.bin"))=="first",
                "Categorizar altera a organização sem mover os arquivos instalados.");
            form.MoveRegisteredMods(new[]{second.Id},clothes.Id,false,"Outros");
            check(state.List.Mods.FindIndex(mod=>mod.Id==second.Id)<state.List.Mods.FindIndex(mod=>mod.Id==clothes.Id)&&
                state.List.Mods.Single(mod=>mod.Id==second.Id).Category=="Outros"&&state.List.Mods.Single(mod=>mod.Id==second.Id).Variants.Count==2,"Arrastar move o mod com todas as suas variantes para a categoria e posição escolhidas.");
            form.SaveProjectEdits();
            var saved=form.library.Load();
            check(saved.List.Mods.FindIndex(mod=>mod.Id==second.Id)<saved.List.Mods.FindIndex(mod=>mod.Id==clothes.Id),
                "A ordem reorganizada sobrevive à gravação do projeto.");
            check(DragScrollSpeed(44,40,600,96)<DragScrollSpeed(96,40,600,96)&&
                DragScrollSpeed(599,40,600,96)>DragScrollSpeed(545,40,600,96)&&DragScrollSpeed(320,40,600,96)==0,
                "A rolagem aumenta nas bordas e para no centro.");
            // The search debounce can redraw every row while layout messages are being processed.
            Func<Func<DataGridViewRow>,DataGridViewRow> scrollToRow=find=>{
                form.grid.FirstDisplayedScrollingRowIndex=find().Index;
                Application.DoEvents();
                return find();
            };
            var target=scrollToRow(()=>form.ModRows().Single(row=>Equals(row.Tag,clothes.Id)));
            var rect=form.grid.GetRowDisplayRectangle(target.Index,false);
            var point=form.grid.PointToScreen(new Point(rect.Left+120,rect.Top+3));
            form.organizationDragIds=new[]{outside.Id};form.organizationDragToken="fixture";
            var drag=new DragEventArgs(new DataObject(OrganizationDragFormat,"fixture"),0,point.X,point.Y,DragDropEffects.Move,DragDropEffects.None);
            check(form.UpdateOrganizationDrop(form.grid,drag)&&form.organizationGap!=null,
                "O arraste abre um espaço de inserção antes da linha.");
            form.organizationGap.Height=44;
            CaptureUi(form,Path.Combine(root,"mod-reorder-gap.png"));
            form.DropOrganizedMods();form.organizationDragIds=null;form.organizationDragToken=null;
            check(state.List.Mods.FindIndex(mod=>mod.Id==outside.Id)<state.List.Mods.FindIndex(mod=>mod.Id==clothes.Id)&&state.List.Folders.Count==0,
                "Soltar entre linhas reordena sem criar uma pasta.");
            check(form.projectDraft!=null,"Reordenar dentro da mesma categoria mantém a alteração pendente para Salvar projeto.");
            foreach(string destination in new string[]{null,"Outros"}){
                var heading=scrollToRow(()=>form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is SectionRow&&
                    ((SectionRow)row.Tag).Kind=="heading"&&((SectionRow)row.Tag).Category==destination));
                var box=form.grid.GetRowDisplayRectangle(heading.Index,false);
                var cursor=form.grid.PointToScreen(new Point(box.Left+120,box.Top+12));
                form.organizationDragIds=new[]{outside.Id};form.organizationDragToken="fixture-heading";
                var headingDrag=new DragEventArgs(new DataObject(OrganizationDragFormat,"fixture-heading"),0,cursor.X,cursor.Y,DragDropEffects.Move,DragDropEffects.None);
                check(form.UpdateOrganizationDrop(form.grid,headingDrag),"O cabeçalho aceita o mod sem depender de uma linha já cadastrada.");
                form.DropOrganizedMods();form.organizationDragIds=null;form.organizationDragToken=null;
                check(state.List.Mods.First(mod=>mod.Category==destination).Id==outside.Id,
                    "Soltar no cabeçalho insere no início da categoria, na posição indicada pela prévia.");
            }
            form.CategorizeMods(new[]{outside.Id},null);
            form.SaveProjectEdits();
            foreach(bool accept in new[]{false,true}){
                var destination=scrollToRow(()=>form.ModRows().Single(row=>Equals(row.Tag,clothes.Id)));
                var rowBounds=form.grid.GetRowDisplayRectangle(destination.Index,false);
                var cursor=form.grid.PointToScreen(new Point(rowBounds.Left+120,rowBounds.Top+rowBounds.Height/2));
                form.organizationDragging=true;
                form.organizationDragIds=new[]{outside.Id};form.organizationDragToken="fixture-folder";
                var folderDrag=new DragEventArgs(new DataObject(OrganizationDragFormat,"fixture-folder"),0,cursor.X,cursor.Y,DragDropEffects.Move,DragDropEffects.None);
                check(form.UpdateOrganizationDrop(form.grid,folderDrag)&&form.organizationFolderTarget==destination&&form.organizationGap==null,
                    "O centro da linha destaca o destino da pasta.");
                if(accept)CaptureUi(form,Path.Combine(root,"mod-folder-drop-target.png"));
                var oldOrder=state.List.Mods.Select(mod=>mod.Id).ToArray();
                string oldCategory=state.List.Mods.Single(mod=>mod.Id==outside.Id).Category;
                bool answered=false;
                using(var timer=new Timer{Interval=30}){
                    timer.Tick+=(s,e)=>{
                        var dialog=Application.OpenForms.OfType<GeneralNameDialog>().FirstOrDefault(item=>item.Visible);
                        if(dialog==null)return;timer.Stop();
                        answered=true;
                        if(accept){
                            AllControls(dialog).OfType<TextBox>().Single(input=>input.Name=="NameInput").Text="Agrupados";
                            AllControls(dialog).OfType<GeneralPicker>().Single(picker=>picker.Name=="FolderSelectionMode").SelectedIndex=1;
                        }
                        AllControls(dialog).OfType<Button>().Single(button=>button.Name==(accept?"SaveName":"CancelName")).PerformClick();
                    };
                    timer.Start();
                    typeof(Control).GetMethod("OnDragDrop",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
                        .Invoke(form.grid,new object[]{folderDrag});
                    form.organizationDragging=false;form.organizationDragIds=null;form.organizationDragToken=null;
                    await Until(()=>answered&&!form.OwnedForms.OfType<GeneralNameDialog>().Any(item=>item.Visible)&&
                        (!accept||state.List.Folders.Count==1));
                }
                if(!accept)check(state.List.Folders.Count==0&&oldOrder.SequenceEqual(state.List.Mods.Select(mod=>mod.Id))&&
                    state.List.Mods.Single(mod=>mod.Id==outside.Id).Category==oldCategory,
                    "Cancelar a pasta preserva a ordem e a categoria.");
            }
            var folder=state.List.Folders.Single();
            check(folder.Name=="Agrupados"&&folder.SingleSelection&&folder.ModIds.SequenceEqual(new[]{clothes.Id,outside.Id})&&
                state.List.Mods.Single(mod=>mod.Id==outside.Id).Category==state.List.Mods.Single(mod=>mod.Id==clothes.Id).Category&&
                File.Exists(Safe.In(game,"mods/three/main.bin")),
                "Soltar sobre um mod cria uma pasta com os dois na posição e categoria do destino.");
            form.SaveProjectEdits();
            check(form.library.Load().List.Folders.Single().ModIds.SequenceEqual(folder.ModIds),"O vínculo à pasta é salvo com o projeto.");

            Func<GeneralModFolder,DataGridViewRow> folderRow=item=>form.grid.Rows.Cast<DataGridViewRow>().Single(row=>
                row.Tag is FolderRow&&((FolderRow)row.Tag).Folder.Id==item.Id);
            var originalMembers=folder.ModIds.ToArray();
            form.collapsedFolders.Add(folder.Id);
            form.SetAssociationSearch(clothes.Name);
            var filtered=scrollToRow(()=>folderRow(folder));
            var cell=form.grid.GetCellDisplayRectangle(form.grid.Columns["Name"].Index,filtered.Index,false);
            var fromName=new Point(cell.Left+FolderTitleInset+5,cell.Top+cell.Height/2);
            var fromArrow=new Point(cell.Left+10,cell.Top+cell.Height/2);
            check(Object.ReferenceEquals(form.OrganizationDragRowAt(fromName),filtered)&&form.OrganizationDragRowAt(fromArrow)==null&&
                ((FolderRow)filtered.Tag).Members.Length==1&&form.OrganizationDragSelection(filtered).SequenceEqual(originalMembers),
                "O nome da pasta recolhida arrasta todos os membros, inclusive os ocultos pelo filtro.");
            form.SetAssociationSearch("");
            form.CategorizeMods(new[]{uncategorized.Id},"Outros");
            var neighborFolder=new GeneralModFolder{Id="folder-neighbor",Name="Vizinha",ModIds=new List<string>{second.Id,uncategorized.Id}};
            form.BeginProjectEdit();form.AddModFolder(neighborFolder);form.FinishProjectEdit();form.DrawRows();

            Action<Func<DataGridViewRow>,int> moveFolder=(findDestination,yOffset)=>{
                var destination=scrollToRow(findDestination);
                var rowBounds=form.grid.GetRowDisplayRectangle(destination.Index,false);
                var cursor=form.grid.PointToScreen(new Point(rowBounds.Left+120,rowBounds.Top+yOffset));
                form.organizationDragIds=form.OrganizationDragSelection(folderRow(folder));
                form.organizationDragToken="fixture-whole-folder";form.organizationDragFolderId=folder.Id;
                var drop=new DragEventArgs(new DataObject(OrganizationDragFormat,form.organizationDragToken),0,cursor.X,cursor.Y,
                    DragDropEffects.Move,DragDropEffects.None);
                check(form.UpdateOrganizationDrop(form.grid,drop)&&form.organizationGap!=null&&form.organizationFolderTarget==null&&
                    form.organizationTargetFolder==null,"A pasta inteira mostra uma posição de inserção.");
                form.DropOrganizedMods();
                form.organizationDragIds=null;form.organizationDragToken=null;form.organizationDragFolderId=null;
            };
            moveFolder(()=>folderRow(neighborFolder),folderRow(neighborFolder).Height-4);
            check(folderRow(folder).Index>folderRow(neighborFolder).Index&&state.List.Folders.Count==2&&
                neighborFolder.ModIds.SequenceEqual(new[]{second.Id,uncategorized.Id}),
                "Soltar perto de outra pasta reordena as duas como unidades independentes.");
            moveFolder(()=>form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is SectionRow&&
                ((SectionRow)row.Tag).Kind=="heading"&&((SectionRow)row.Tag).Category=="Roupas"),12);
            check(((FolderRow)folderRow(folder).Tag).Category=="Roupas"&&
                state.List.Mods.Where(mod=>originalMembers.Contains(mod.Id)).All(mod=>mod.Category=="Roupas")&&
                folder.ModIds.SequenceEqual(originalMembers)&&folder.SingleSelection&&folder.Name=="Agrupados"&&form.collapsedFolders.Contains(folder.Id),
                "Mudar de categoria preserva a pasta, a ordem interna, a seleção única e o estado recolhido.");
            form.SaveProjectEdits();
            check(form.library.Load().List.Folders.Single(item=>item.Id==folder.Id).ModIds.SequenceEqual(originalMembers),
                "A mudança da pasta inteira sobrevive à gravação do projeto.");
            form.CategorizeMods(originalMembers,"Outros");
            form.DissolveModFolder(neighborFolder);

            Func<DataGridViewRow> movingMod=()=>form.ModRows().Single(row=>Equals(row.Tag,uncategorized.Id));
            Action<Func<DataGridViewRow>,int,string> dropMod=(findDestination,yOffset,expectedFolder)=>{
                var destination=scrollToRow(findDestination);
                var rowBounds=form.grid.GetRowDisplayRectangle(destination.Index,false);
                var cursor=form.grid.PointToScreen(new Point(rowBounds.Left+120,rowBounds.Top+yOffset));
                form.organizationDragIds=form.OrganizationDragSelection(movingMod());
                form.organizationDragToken="fixture-mod-folder";
                var drop=new DragEventArgs(new DataObject(OrganizationDragFormat,form.organizationDragToken),0,cursor.X,cursor.Y,
                    DragDropEffects.Move,DragDropEffects.None);
                check(form.UpdateOrganizationDrop(form.grid,drop)&&form.organizationTargetFolder==expectedFolder&&
                    (expectedFolder==null?form.organizationGap!=null:form.organizationFolderTarget==destination),
                    "A prévia diferencia soltar dentro da pasta ou na lista.");
                form.DropOrganizedMods();
                form.organizationDragIds=null;form.organizationDragToken=null;
            };
            dropMod(()=>folderRow(folder),folderRow(folder).Height/2,folder.Id);
            check(folder.ModIds.SequenceEqual(originalMembers.Concat(new[]{uncategorized.Id}))&&!movingMod().Visible&&
                form.collapsedFolders.Contains(folder.Id),
                "Soltar no nome da pasta recolhida insere o mod sem abrir a pasta.");
            form.ToggleModFolder((FolderRow)folderRow(folder).Tag);
            check(movingMod().Visible,"Ao expandir, o novo mod aparece dentro da pasta.");
            dropMod(()=>form.ModRows().Single(row=>Equals(row.Tag,second.Id)),3,null);
            check(folder.ModIds.SequenceEqual(originalMembers)&&movingMod().Visible&&!form.folderBranches.ContainsKey(movingMod())&&
                state.List.Mods.Single(mod=>mod.Id==uncategorized.Id).Category=="Outros",
                "Arrastar de volta para a lista retira o mod da pasta na mesma categoria.");
            var single=new GeneralModFolder{Id="folder-single",Name="Último mod",ModIds=new List<string>{uncategorized.Id}};
            form.BeginProjectEdit();form.AddModFolder(single);form.FinishProjectEdit();form.DrawRows();
            dropMod(()=>folderRow(folder),3,null);
            check(!state.List.Folders.Contains(single)&&folder.ModIds.SequenceEqual(originalMembers)&&
                !form.folderBranches.ContainsKey(movingMod())&&movingMod().Index<folderRow(folder).Index,
                "Tirar o último mod limpa a pasta vazia e a borda da pasta vizinha mantém o mod na lista.");
            form.SaveProjectEdits();
            check(form.library.Load().List.Folders.Single().ModIds.SequenceEqual(originalMembers),
                "Entrar e sair da pasta salva a organização correta do projeto.");
            form.CategorizeMods(new[]{uncategorized.Id},null);
            form.SaveProjectEdits();

            Action<string> revealCategory=category=>{
                scrollToRow(()=>form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is SectionRow&&
                    ((SectionRow)row.Tag).Kind=="heading"&&((SectionRow)row.Tag).Category==category));
                form.UpdateCategoryDeleteButtons();
            };
            form.RequestDeleteCategory("Outros");
            var stale=form.OwnedForms.OfType<GeneralCategoryRemovalDialog>().Single(item=>item.Visible);
            form.CategorizeMods(new[]{uncategorized.Id},"Outros");
            AllControls(stale).OfType<Button>().Single(button=>button.Name=="DeleteCategoryKeep").PerformClick();
            await Until(()=>stale.IsDisposed);
            check(form.Enabled&&state.List.Categories.Contains("Outros")&&
                state.List.Mods.Single(mod=>mod.Id==uncategorized.Id).Category=="Outros",
                "A janela fica aberta em paralelo e não exclui uma categoria cuja lista mudou.");
            form.CategorizeMods(new[]{uncategorized.Id},null);
            form.SaveProjectEdits();

            revealCategory("Outros");
            using(var timer=new Timer{Interval=30}){
                timer.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralCategoryRemovalDialog>().FirstOrDefault(item=>item.Visible);
                    if(dialog==null)return;timer.Stop();
                    AllControls(dialog).OfType<Button>().Single(button=>button.Name=="DeleteCategoryKeep").PerformClick();
                };
                timer.Start();form.categoryDeleteButtons.Keys.Single(button=>Equals(button.Tag,"Outros")).PerformClick();
                await Until(()=>!state.List.Categories.Contains("Outros"));
            }
            check(!state.List.Categories.Contains("Outros")&&state.List.Mods.Where(mod=>new[]{clothes.Id,second.Id,outside.Id}.Contains(mod.Id)).All(mod=>mod.Category==null)&&
                File.Exists(Safe.In(game,"mods/one/main.bin"))&&File.Exists(Safe.In(game,"mods/two/main.bin")),
                "Excluir e manter leva todos os itens para Sem categoria preservando os arquivos.");
            form.CategorizeMods(new[]{clothes.Id,second.Id},"Roupas");form.SaveProjectEdits();
            Exception failure=null;
            revealCategory("Roupas");
            using(var timer=new Timer{Interval=30}){
                timer.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralCategoryRemovalDialog>().FirstOrDefault(item=>item.Visible);
                    if(dialog==null)return;timer.Stop();
                    try{AllControls(dialog).OfType<Button>().Single(button=>button.Name=="DeleteCategoryUninstall").PerformClick();}
                    catch(Exception ex){failure=ex;dialog.Close();}
                };
                timer.Start();form.categoryDeleteButtons.Keys.Single(button=>Equals(button.Tag,"Roupas")).PerformClick();
                await Until(()=>failure!=null||!state.List.Categories.Contains("Roupas")&&!form.working&&!form.scanning);
            }
            if(failure!=null)throw failure;
            check(!state.List.Categories.Contains("Roupas")&&!File.Exists(Safe.In(game,"mods/one/main.bin"))&&
                !File.Exists(Safe.In(game,"mods/two/main.bin"))&&File.ReadAllText(Safe.In(game,"mods/three/main.bin"))=="other",
                "Excluir e desinstalar remove os arquivos gerenciados da categoria e preserva os arquivos de outras categorias.");
        });
        Safe.WriteJson(Path.Combine(root,"resultado-organization.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }

    void CategorizeInventoryRow(P2PSyncDifference found,string category){
        if(!CanEditInventory||category!=null&&!state.List.Categories.Contains(category))return;
        var files=InventoryRow(InventoryLinkStore.Key(found));
        if(!files.Any(file=>InventoryPaths.Equals(file.Path,found.Path)))return;
        var entry=DetectedInventoryEntry(found,files);
        if(entry==null)return;
        var existingPaths=new HashSet<string>(state.List.Mods.SelectMany(mod=>mod.DetectedPaths),InventoryPaths);
        entry.DetectedPaths.RemoveAll(existingPaths.Contains);
        if(entry.DetectedPaths.Count==0)return;
        BeginProjectEdit();
        ApplyEdited(entry,entry,entry.DefaultVariant,false,finish:false);
        statuses[Key(entry,entry.Variants[0])]=GeneralStatusPresentation.DetectedFiles;
        CategorizeMods(new[]{entry.Id},category);
    }

    void AddCategorizeMenu(ContextMenuStrip menu){
        var item=(ToolStripMenuItem)GeneralMenuRenderer.Add(menu,T("Categorizar","Categorize"),()=>{});
        item.Name="CategorizeMods";
        var choices=GeneralMenuRenderer.Create();
        item.DropDown=choices;
        menu.Opening+=(s,e)=>{
            var selected=OrganizationSelection(grid.CurrentRow);
            var found=FocusedInventoryFile();
            string inventoryKey=found==null?null:InventoryLinkStore.Key(found);
            bool canRegister=found!=null&&InventoryRow(inventoryKey).Any(file=>FixedInstaller.InventoryRegistrationPath(file.Path));
            item.Visible=selected.Length>0||canRegister;
            item.Enabled=!working&&!scanning&&associationChoice==null&&(found==null||CanEditInventory);
            foreach(ToolStripItem previous in choices.Items.Cast<ToolStripItem>().ToArray())previous.Dispose();
            choices.Items.Clear();
            if(selected.Length==0&&!canRegister)return;
            var preset=state.SelectedPreset;
            var destination=state.Destination;
            var projectData=data;
            Action<string> categorize=category=>{
                if(working||scanning||associationChoice!=null||preset!=state.SelectedPreset||
                    !InventoryPaths.Equals(destination,state.Destination)||!InventoryPaths.Equals(projectData,data))return;
                if(found==null)CategorizeMods(selected,category);
                else CategorizeInventoryRow(found,category);
            };
            GeneralMenuRenderer.Add(choices,UncatName(),()=>Guard(()=>categorize(null)));
            foreach(string category in state.List.Categories){
                string target=category;
                GeneralMenuRenderer.Add(choices,target,()=>Guard(()=>categorize(target)));
            }
        };
    }
}

internal sealed class GeneralCategoryRemovalDialog : GeneralDialog {
    internal GeneralCategoryRemovalDialog(string category,int count):base(GeneralStyle.T("Excluir categoria","Delete category")){
        ClientSize=new Size(760,265);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=2,Padding=new Padding(20,8,20,20)};
        layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label{Text=category+" · "+count+" mods\n\n"+GeneralStyle.T(
            "Ao manter, os mods vão para Sem categoria. Ao desinstalar, os arquivos gerenciados dos itens desta categoria serão removidos.",
            "Keep moves the mods to Uncategorized. Uninstall removes the managed files of the items in this category."),
            Dock=DockStyle.Fill,Font=GeneralStyle.Font(12)},0,0);
        var commands=GeneralTableDialogLayout.Commands();
        var keep=GeneralStyle.Button(GeneralStyle.T("Excluir e manter","Delete and keep"),()=>Complete(DialogResult.No),true);
        keep.Name="DeleteCategoryKeep";
        var uninstall=GeneralStyle.Button(GeneralStyle.T("Excluir e desinstalar","Delete and uninstall"),()=>Complete(DialogResult.Yes));
        uninstall.Name="DeleteCategoryUninstall";((GeneralButton)uninstall).Destructive=true;
        var cancel=GeneralStyle.Button(GeneralStyle.T("Cancelar","Cancel"),()=>Complete(DialogResult.Cancel));
        commands.Controls.AddRange(new Control[]{keep,uninstall,cancel});
        layout.Controls.Add(commands,0,1);Content.Controls.Add(layout);CancelButton=cancel;
        GeneralStyle.Apply(this,GeneralStyle.Dark);
    }
}
