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
            button.Name="DeleteCategory";button.Tag=category;button.Bare=true;button.IconKind="trash";button.Destructive=true;
            button.AutoSize=false;button.MinimumSize=new Size(44,44);button.Size=new Size(44,44);button.Margin=Padding.Empty;
            button.AccessibleName=T("Excluir categoria ","Delete category ")+category;
            tips.SetToolTip(button,button.AccessibleName);
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
        if(working||scanning||associationChoice!=null||category==null)return;
        try{
            var members=state.List.Mods.Where(mod=>String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase)).ToArray();
            using(var dialog=new GeneralCategoryRemovalDialog(category,members.Length)){
                var answer=dialog.ShowDialog(this);
                if(answer!=DialogResult.Yes&&answer!=DialogResult.No)return;
                if(answer==DialogResult.Yes&&members.Length>0){
                    if(projectDraft!=null&&!ConfirmProjectNavigation())return;
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
                            RemoveManagedWorks(jobs,root,disabled,ReportUninstall);
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
    string organizationDragToken,organizationTargetId,organizationTargetCategory,organizationTargetFolder;
    bool organizationAfter,organizationDragging;
    DataGridViewRow organizationGap;
    Bitmap organizationGhost;

    void EnableOrganizationDrag(){
        Point down=Point.Empty;
        DataGridViewRow pressed=null;
        var timer=new Timer{Interval=30};
        int scrollTicks=0;
        grid.MouseDown+=(s,e)=>{
            pressed=null;
            if(e.Button!=MouseButtons.Left||working||scanning||associationChoice!=null||
                (Control.ModifierKeys&(Keys.Control|Keys.Shift))!=0)return;
            var hit=grid.HitTest(e.X,e.Y);
            if(hit.RowIndex<0||hit.ColumnIndex!=grid.Columns["Name"].Index)return;
            var row=grid.Rows[hit.RowIndex];
            if(!(row.Tag is string)&&!(row.Tag is Work)||GroupArrowAt(row,hit.ColumnIndex,e.X-grid.GetCellDisplayRectangle(hit.ColumnIndex,hit.RowIndex,false).X))return;
            pressed=row;down=e.Location;
        };
        grid.MouseUp+=(s,e)=>pressed=null;
        grid.MouseMove+=(s,e)=>{
            if(pressed==null||e.Button!=MouseButtons.Left||organizationDragging)return;
            var size=SystemInformation.DragSize;
            if(new Rectangle(down.X-size.Width/2,down.Y-size.Height/2,size.Width,size.Height).Contains(e.Location))return;
            var row=pressed;pressed=null;
            organizationDragIds=OrganizationSelection(row);
            if(organizationDragIds.Length==0)return;
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
                var work=candidate.Tag as Work;
                if(candidate.Tag is string&&moving.Contains((string)candidate.Tag)||work!=null&&moving.Contains(work.Entry.Id))
                    candidate.Visible=false;
            }
            timer.Start();
            try{grid.DoDragDrop(new DataObject(OrganizationDragFormat,organizationDragToken),DragDropEffects.Move);}
            finally{
                timer.Stop();ClearOrganizationGap();
                if(organizationGhost!=null){organizationGhost.Dispose();organizationGhost=null;}
                organizationDragIds=null;organizationDragToken=null;
                DrawRows();
                if(IsHandleCreated&&!IsDisposed)BeginInvoke((Action)(()=>organizationDragging=false));
                else organizationDragging=false;
            }
        };
        timer.Tick+=(s,e)=>{
            if(organizationDragIds==null){timer.Stop();return;}
            if(++scrollTicks%2==0)ScrollInventoryDrag(grid);
            if(organizationGap!=null&&organizationGap.DataGridView==grid)
                organizationGap.Height=Math.Min(44,organizationGap.Height+6);
            grid.Invalidate();
        };
        grid.Paint+=(s,e)=>{
            if(!organizationDragging||organizationGhost==null)return;
            var point=grid.PointToClient(Cursor.Position);
            if(!grid.ClientRectangle.Contains(point))return;
            int width=Math.Min(organizationGhost.Width,grid.ClientSize.Width-24);
            var target=new Rectangle(12,point.Y-organizationGhost.Height/2,width,organizationGhost.Height);
            using(var attributes=new ImageAttributes()){
                var matrix=new ColorMatrix();matrix.Matrix33=.82f;attributes.SetColorMatrix(matrix);
                e.Graphics.DrawImage(organizationGhost,target,0,0,organizationGhost.Width,organizationGhost.Height,GraphicsUnit.Pixel,attributes);
            }
        };
        grid.DragLeave+=(s,e)=>ClearOrganizationGap();
        grid.Disposed+=(s,e)=>{timer.Dispose();if(organizationGhost!=null){organizationGhost.Dispose();organizationGhost=null;}};
    }

    bool UpdateOrganizationDrop(Control target,DragEventArgs args){
        if(target!=grid||organizationDragIds==null||working||scanning||associationChoice!=null||
            !String.Equals(args.Data.GetData(OrganizationDragFormat) as string,organizationDragToken,StringComparison.Ordinal))return false;
        var point=grid.PointToClient(new Point(args.X,args.Y));
        var hit=grid.HitTest(point.X,point.Y);
        if(hit.RowIndex<0)return false;
        var row=grid.Rows[hit.RowIndex];
        if(row==organizationGap)return true;
        var section=row.Tag as SectionRow;
        string category,id=null,folder=null;
        bool after=false;
        if(section!=null&&(section.Kind=="heading"||section.Kind=="add"||section.Kind=="subheading")){
            category=section.Kind=="subheading"?null:section.Category;
            if(section.Kind!="add"){
                var first=state.List.Mods.FirstOrDefault(mod=>!organizationDragIds.Contains(mod.Id)&&
                    String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase));
                id=first==null?null:first.Id;
            }
        }else{
            var work=row.Tag as Work;
            var group=row.Tag as FolderRow;
            id=row.Tag as string??(work==null?group==null?null:group.Members.FirstOrDefault():work.Entry.Id);
            var entry=state.List.Mods.FirstOrDefault(mod=>mod.Id==id);
            if(entry==null||organizationDragIds.Contains(id))return false;
            category=entry.Category;
            after=point.Y>=grid.GetRowDisplayRectangle(row.Index,false).Top+row.Height/2;
            var parent=state.List.Folders.FirstOrDefault(item=>item.ModIds.Contains(id));
            folder=parent==null?null:parent.Id;
        }
        if(organizationGap!=null&&organizationTargetId==id&&organizationTargetCategory==category&&
            organizationTargetFolder==folder&&organizationAfter==after)return true;
        ClearOrganizationGap();
        organizationTargetId=id;organizationTargetCategory=category;organizationTargetFolder=folder;organizationAfter=after;
        int index=row.Index;
        if(section!=null&&(section.Kind=="heading"||section.Kind=="subheading"))index++;
        else if(id!=null){
            var related=grid.Rows.Cast<DataGridViewRow>().Where(candidate=>candidate.Visible&&
                (Equals(candidate.Tag,id)||candidate.Tag is Work&&((Work)candidate.Tag).Entry.Id==id)).ToArray();
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

    void ClearOrganizationGap(){
        if(organizationGap!=null&&organizationGap.DataGridView==grid)grid.Rows.Remove(organizationGap);
        organizationGap=null;
    }

    void DropOrganizedMods(){
        if(organizationGap==null||organizationDragIds==null)return;
        var ids=organizationDragIds.ToArray();
        string target=organizationTargetId,category=organizationTargetCategory,folder=organizationTargetFolder;
        bool after=organizationAfter;
        ClearOrganizationGap();
        MoveRegisteredMods(ids,target,after,category,folder);
    }

    void MoveRegisteredMods(IEnumerable<string> ids,string targetId,bool after,string category,string folderId=null){
        if(working||scanning||associationChoice!=null)return;
        var moving=new HashSet<string>(ids,StringComparer.Ordinal);
        if(moving.Count==0||targetId!=null&&moving.Contains(targetId))return;
        var entries=state.List.Mods.Where(mod=>moving.Contains(mod.Id)).ToList();
        if(entries.Count==0||targetId!=null&&!state.List.Mods.Any(mod=>mod.Id==targetId))return;
        CategorizeMods(moving,category,redraw:false);
        BeginProjectEdit();
        entries=state.List.Mods.Where(mod=>moving.Contains(mod.Id)).ToList();
        state.List.Mods.RemoveAll(mod=>moving.Contains(mod.Id));
        int index=targetId==null?state.List.Mods.FindLastIndex(mod=>String.Equals(mod.Category,category,StringComparison.OrdinalIgnoreCase))+1:
            state.List.Mods.FindIndex(mod=>mod.Id==targetId)+(after?1:0);
        state.List.Mods.InsertRange(Math.Max(0,index),entries);
        var folder=state.List.Folders.FirstOrDefault(item=>item.Id==folderId);
        if(folder!=null){
            foreach(var other in state.List.Folders)other.ModIds.RemoveAll(moving.Contains);
            int at=folder.ModIds.IndexOf(targetId)+(after?1:0);
            folder.ModIds.InsertRange(Math.Max(0,at),entries.Select(mod=>mod.Id));
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
            var target=form.ModRows().Single(row=>Equals(row.Tag,clothes.Id));
            form.grid.FirstDisplayedScrollingRowIndex=target.Index;
            Application.DoEvents();
            var rect=form.grid.GetRowDisplayRectangle(target.Index,false);
            var point=form.grid.PointToScreen(new Point(rect.Left+120,rect.Top+3));
            form.organizationDragIds=new[]{outside.Id};form.organizationDragToken="fixture";
            var drag=new DragEventArgs(new DataObject(OrganizationDragFormat,"fixture"),0,point.X,point.Y,DragDropEffects.Move,DragDropEffects.None);
            check(form.UpdateOrganizationDrop(form.grid,drag)&&form.organizationGap!=null,
                "O arraste abre um espaço de inserção antes da linha.");
            form.organizationGap.Height=44;
            CaptureUi(form,Path.Combine(root,"mod-reorder-gap.png"));
            form.DropOrganizedMods();form.organizationDragIds=null;form.organizationDragToken=null;
            check(state.List.Mods.FindIndex(mod=>mod.Id==outside.Id)<state.List.Mods.FindIndex(mod=>mod.Id==clothes.Id),
                "Soltar aplica o destino mostrado pelo espaço de inserção.");
            check(form.projectDraft!=null,"Reordenar dentro da mesma categoria mantém a alteração pendente para Salvar projeto.");
            foreach(string destination in new string[]{null,"Outros"}){
                var heading=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is SectionRow&&
                    ((SectionRow)row.Tag).Kind=="heading"&&((SectionRow)row.Tag).Category==destination);
                form.grid.FirstDisplayedScrollingRowIndex=heading.Index;Application.DoEvents();
                var box=form.grid.GetRowDisplayRectangle(heading.Index,false);
                var cursor=form.grid.PointToScreen(new Point(box.Left+120,box.Top+12));
                form.organizationDragIds=new[]{outside.Id};form.organizationDragToken="fixture-heading";
                var headingDrag=new DragEventArgs(new DataObject(OrganizationDragFormat,"fixture-heading"),0,cursor.X,cursor.Y,DragDropEffects.Move,DragDropEffects.None);
                check(form.UpdateOrganizationDrop(form.grid,headingDrag),"O cabeçalho aceita o mod sem depender de uma linha já cadastrada.");
                form.DropOrganizedMods();form.organizationDragIds=null;form.organizationDragToken=null;
                check(state.List.Mods.First(mod=>mod.Category==destination).Id==outside.Id,
                    "Soltar no cabeçalho insere no início da categoria, na posição indicada pela prévia.");
            }
            form.SaveProjectEdits();
            Action<string> revealCategory=category=>{
                var heading=form.grid.Rows.Cast<DataGridViewRow>().Single(row=>row.Tag is SectionRow&&
                    ((SectionRow)row.Tag).Kind=="heading"&&((SectionRow)row.Tag).Category==category);
                form.grid.FirstDisplayedScrollingRowIndex=heading.Index;Application.DoEvents();form.UpdateCategoryDeleteButtons();
            };
            revealCategory("Outros");
            using(var timer=new Timer{Interval=30}){
                timer.Tick+=(s,e)=>{
                    var dialog=Application.OpenForms.OfType<GeneralCategoryRemovalDialog>().FirstOrDefault(item=>item.Visible);
                    if(dialog==null)return;timer.Stop();
                    AllControls(dialog).OfType<Button>().Single(button=>button.Name=="DeleteCategoryKeep").PerformClick();
                };
                timer.Start();form.categoryDeleteButtons.Keys.Single(button=>Equals(button.Tag,"Outros")).PerformClick();
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
                await Until(()=>!timer.Enabled&&!form.working&&!form.scanning);
            }
            if(failure!=null)throw failure;
            check(!state.List.Categories.Contains("Roupas")&&!File.Exists(Safe.In(game,"mods/one/main.bin"))&&
                !File.Exists(Safe.In(game,"mods/two/main.bin"))&&File.ReadAllText(Safe.In(game,"mods/three/main.bin"))=="other",
                "Excluir e desinstalar remove os arquivos gerenciados da categoria e preserva os arquivos de outras categorias.");
        });
        Safe.WriteJson(Path.Combine(root,"resultado-organization.json"),new{Passed=true,Tests=checks,RealGameTouched=false,NetworkUsed=false});
    }

    void AddCategorizeMenu(ContextMenuStrip menu){
        var item=(ToolStripMenuItem)GeneralMenuRenderer.Add(menu,T("Categorizar","Categorize"),()=>{});
        item.Name="CategorizeMods";
        var choices=GeneralMenuRenderer.Create();
        item.DropDown=choices;
        menu.Opening+=(s,e)=>{
            item.Visible=OrganizationSelection(grid.CurrentRow).Length>0;
            item.Enabled=!working&&!scanning&&associationChoice==null;
        };
        item.DropDownOpening+=(s,e)=>{
            foreach(ToolStripItem previous in choices.Items.Cast<ToolStripItem>().ToArray())previous.Dispose();
            choices.Items.Clear();
            var selected=OrganizationSelection(grid.CurrentRow);
            GeneralMenuRenderer.Add(choices,UncatName(),()=>Guard(()=>CategorizeMods(selected,null)));
            foreach(string category in state.List.Categories){
                string target=category;
                GeneralMenuRenderer.Add(choices,target,()=>Guard(()=>CategorizeMods(selected,target)));
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
