using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

internal sealed partial class GeneralModWindow {
    internal static void RunTableSurfaceTests(string parent) {
        string root=OnlineTests.NestedRoot(parent,"table-surface-");Directory.CreateDirectory(root);
        var checks=new List<string>();
        Action<bool,string> check=(ok,label)=>{if(!ok)throw new IOException(label);checks.Add(label);};
        foreach(string theme in new[]{"light","dark","black"}){
            var first=NewEntry("First mod","https://example.test/first.zip");first.Category="Clothes";
            var second=NewEntry("Second mod","https://example.test/second.zip");second.Category="Clothes";
            var third=NewEntry("Third mod","https://example.test/third.zip");third.Category="Vehicles";
            var state=new GeneralModState{Theme=theme,Language="pt",List=new GeneralModList{
                Categories=new List<string>{"Clothes","Vehicles"},Mods=new List<GeneralModEntry>{first,second,third}}};
            TestForm(FixedData.Load(),state,Safe.In(root,theme+"-data"),root,async form=>{
                form.ClientSize=new Size(1240,1050);form.collapsedCategories.Add("");form.DrawRows();
                form.PerformLayout();await Task.Delay(40);form.grid.ClearSelection();form.SetHover(-1);
                form.UpdateCategoryDeleteButtons();
                var visible=form.grid.Columns.Cast<DataGridViewColumn>().Where(column=>column.Visible).OrderBy(column=>column.DisplayIndex).Select(column=>column.Name).ToArray();
                check(visible.SequenceEqual(new[]{"CategoryIndent","Mark","Name","Size","Status","Origin"}),
                    "The visible table omits the Group column while keeping origin immediately after status: "+theme);
                check(form.grid.Columns["Group"].Index>=0&&!form.grid.Columns["Group"].Visible,
                    "Hidden group metadata preserves physical row indexes without a user-facing column: "+theme);
                var button=form.categoryDeleteButtons.Keys.Single(control=>Equals(control.Tag,"Clothes"));
                var heading=form.categoryDeleteButtons[button];
                check(button.Visible&&button.CompositeParentSurface&&button.Width==44&&button.Height==44,
                    "The category delete action reuses the composed parent surface and its existing hit target: "+theme);
                using(var idle=TableSurfaceCapture(form.grid))TableSurfaceButtonCorners(idle,button,GeneralStyle.Surface,check,"Resting category action has no opaque square: "+theme);
                form.SetHover(heading.Index);form.grid.Refresh();
                using(var hover=TableSurfaceCapture(form.grid)){
                    TableSurfaceButtonCorners(hover,button,GeneralStyle.Selection,check,"Hovered heading and trash corners share one surface: "+theme);
                    hover.Save(Safe.In(root,"grid-hover-"+theme+".png"),ImageFormat.Png);
                    var crop=Rectangle.Intersect(new Rectangle(button.Left-14,button.Top-6,button.Width+28,button.Height+12),form.grid.ClientRectangle);
                    using(var detail=hover.Clone(crop,PixelFormat.Format32bppArgb))detail.Save(Safe.In(root,"trash-hover-"+theme+".png"),ImageFormat.Png);
                }
                typeof(GeneralButton).GetMethod("OnMouseEnter",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(button,new object[]{EventArgs.Empty});
                check(form.hoveredRow==heading.Index,"Moving from the category into its trash action preserves heading hover: "+theme);
                await Task.Delay(180);form.grid.Refresh();
                using(var hoveredButton=TableSurfaceCapture(form.grid))
                    TableSurfaceButtonCorners(hoveredButton,button,GeneralStyle.Selection,check,"Button hover glow leaves its outer corners on the heading surface: "+theme);
                typeof(GeneralButton).GetMethod("OnMouseLeave",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(button,new object[]{EventArgs.Empty});
                await Task.Delay(180);form.grid.ClearSelection();int selectionInvalidations=0;
                button.Invalidated+=(s,e)=>selectionInvalidations++;
                heading.Selected=true;
                check(selectionInvalidations>0,"Selecting a heading invalidates the composed child surface: "+theme);
                form.SetHover(-1);form.grid.Refresh();
                using(var selected=TableSurfaceCapture(form.grid))
                    TableSurfaceButtonCorners(selected,button,GeneralStyle.Selection,check,"Selected heading and category action remain visually continuous: "+theme);
                form.grid.ClearSelection();form.SetHover(-1);form.grid.Refresh();
                using(var table=TableSurfaceCapture(form.grid)){
                    var row=form.ModRows().First(candidate=>Equals(candidate.Tag,first.Id));
                    var cell=form.grid.GetCellDisplayRectangle(form.grid.Columns["Name"].Index,row.Index,false);
                    var bounds=form.grid.GetRowDisplayRectangle(row.Index,false);
                    int middle=bounds.Top+bounds.Height/2;
                    int headerBottom=form.grid.ColumnHeadersHeight-1;
                    int surface=GeneralStyle.Surface.ToArgb();
                    check(table.GetPixel(cell.Right-30,bounds.Bottom-1).ToArgb()==surface&&
                        table.GetPixel(cell.Right-1,middle).ToArgb()==surface&&
                        table.GetPixel(cell.Right-1,headerBottom/2).ToArgb()==surface&&
                        table.GetPixel(cell.Right-30,headerBottom).ToArgb()==surface,
                        "Rows and headers have no horizontal or vertical grid lines: "+theme);
                    var additions=form.grid.Rows.Cast<DataGridViewRow>().Where(candidate=>candidate.Visible&&candidate.Tag is SectionRow&&
                            (((SectionRow)candidate.Tag).Kind=="add"||((SectionRow)candidate.Tag).Kind=="divider"))
                        .Select(candidate=>form.grid.GetRowDisplayRectangle(candidate.Index,false)).Where(rect=>rect.Top>=form.grid.ColumnHeadersHeight&&rect.Bottom<form.grid.ClientSize.Height).ToArray();
                    check(additions.Length>0&&additions.All(rect=>new[]{rect.Bottom-1,rect.Bottom}.All(y=>Enumerable.Range(0,8)
                        .All(index=>table.GetPixel(form.CategoryContentRight/2+index*(form.CategoryContentRight/2-24)/8,y).ToArgb()==GeneralStyle.Surface.ToArgb()))),
                        "The complete category boundary remains a continuous surface without the former separator-row borders: "+theme+"; pixels="+
                        String.Join(",",additions.Select(rect=>rect.Bottom+":"+table.GetPixel(form.CategoryContentRight/2,rect.Bottom-1).ToArgb()+"/"+table.GetPixel(form.CategoryContentRight/2,rect.Bottom).ToArgb()))+"; expected="+GeneralStyle.Surface.ToArgb());
                }
                var size=form.ClientSize;
                var clippedRow=form.ModRows().First(candidate=>Equals(candidate.Tag,third.Id));
                var clippedBounds=form.grid.GetRowDisplayRectangle(clippedRow.Index,false);
                form.ClientSize=new Size(size.Width,size.Height-(form.grid.ClientSize.Height-clippedBounds.Top-22));
                form.PerformLayout();await Task.Delay(40);form.grid.Refresh();
                clippedBounds=form.grid.GetRowDisplayRectangle(clippedRow.Index,false);
                int bottom=form.grid.ClientSize.Height;
                check(clippedBounds.Top<bottom-1&&clippedBounds.Bottom>bottom,
                    "The viewport cuts through a mod row: "+theme);
                using(var clipped=TableSurfaceCapture(form.grid)){
                    var dividers=new[]{"Name","Size","Status"}.Select(name=>
                        form.grid.GetCellDisplayRectangle(form.grid.Columns[name].Index,clippedRow.Index,false).Right-1);
                    check(dividers.All(x=>Enumerable.Range(clippedBounds.Top,bottom-clippedBounds.Top)
                        .All(y=>clipped.GetPixel(x,y).ToArgb()!=GeneralStyle.Line.ToArgb())),
                        "The clipped row remains free of column dividers: "+theme);
                    var nameCell=form.grid.GetCellDisplayRectangle(form.grid.Columns["Name"].Index,clippedRow.Index,false);
                    int inside=nameCell.Right-30;
                    check(clipped.GetPixel(inside,bottom-1).ToArgb()==clipped.GetPixel(inside,bottom-3).ToArgb()&&
                        clipped.GetPixel(inside,bottom-1).ToArgb()!=GeneralStyle.Line.ToArgb(),
                        "The viewport edge does not gain an extra horizontal line: "+theme);
                    clipped.Save(Safe.In(root,"grid-clipped-"+theme+".png"),ImageFormat.Png);
                }
                form.ClientSize=size;
                form.PerformLayout();await Task.Delay(40);
                var title=TableSurfaceControls(form).Single(control=>control.Name=="PresetHeading");
                using(var image=TableSurfaceCapture(title))
                    check(image.GetPixel(40,title.Height-6).ToArgb()==GeneralStyle.Surface.ToArgb(),
                        "The preset title and search area have no dividing rule: "+theme);
                form.SetHover(heading.Index);form.grid.Refresh();
                using(var full=TableSurfaceCapture(form))full.Save(Safe.In(root,"window-hover-"+theme+".png"),ImageFormat.Png);
                form.ToggleCategory(heading,(SectionRow)heading.Tag);form.grid.ClearSelection();form.SetHover(-1);form.grid.Refresh();
                using(var full=TableSurfaceCapture(form))full.Save(Safe.In(root,"window-collapsed-"+theme+".png"),ImageFormat.Png);
            });
        }
        Safe.WriteJson(Safe.In(root,"result.json"),new{Passed=true,Checks=checks,Themes=new[]{"light","dark","black"},Window="1240x1050",Runtime="Windows Forms",RealGameTouched=false});
    }

    static Bitmap TableSurfaceCapture(Control control) {
        var image=new Bitmap(control.Width,control.Height);control.DrawToBitmap(image,new Rectangle(Point.Empty,control.Size));return image;
    }

    static void TableSurfaceButtonCorners(Bitmap image,Control button,Color expected,Action<bool,string> check,string label) {
        var points=new[]{new Point(button.Left+1,button.Top+1),new Point(button.Right-2,button.Top+1),new Point(button.Left+1,button.Bottom-2),new Point(button.Right-2,button.Bottom-2)};
        check(points.All(point=>image.GetPixel(point.X,point.Y).ToArgb()==expected.ToArgb()),label+"; actual="+
            String.Join(",",points.Select(point=>image.GetPixel(point.X,point.Y).ToArgb()))+" expected="+expected.ToArgb());
    }

    static IEnumerable<Control> TableSurfaceControls(Control parent) {
        foreach(Control control in parent.Controls){yield return control;foreach(var child in TableSurfaceControls(control))yield return child;}
    }
}
