using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

internal sealed partial class GeneralProjectHome {
    void EditSavedProject(GeneralProject project,HomeProjectCard card){
        var current=store.Load(project);
        using(var dialog=new GeneralProjectCreationDialog(project.Name,null,current.Destination,
            System.IO.File.Exists(store.CoverPath(project))?store.CoverPath(project):null,editing:true)){
            using(var preview=card.CopyCover())if(!dialog.HasCover&&preview!=null)dialog.ShowCoverPreview(preview);
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;
            store.Edit(project,dialog.ResultName,dialog.SaveCover);
        }
    }

    internal sealed partial class HomeProjectCard {
        readonly Timer hoverAnimation=new Timer{Interval=16};
        float interaction;
        internal float InteractionAmount {get{return interaction;}}
        internal Image CopyCover(){return cover==null?null:(Image)cover.Clone();}
        bool InteractionActive {get{return Enabled&&(hover||Focused&&ShowFocusCues||
            RemoveButton!=null&&RemoveButton.Focused||EditButton!=null&&EditButton.Focused);}}
        void InitializeInteraction(){
            hoverAnimation.Tick+=(s,e)=>{
                float target=InteractionActive?1:0;
                interaction+=(target-interaction)*.24f;
                if(Math.Abs(target-interaction)<.005){interaction=target;hoverAnimation.Stop();}
                Invalidate(true);
            };
            EnabledChanged+=(s,e)=>SyncInteraction();
            VisibleChanged+=(s,e)=>{
                if(Disposing||IsDisposed)return;
                if(!Visible){hoverAnimation.Stop();hover=false;interaction=0;}else SyncInteraction();
            };
        }
        internal void SetPointerOver(bool value){hover=value;SyncInteraction();}
        void SchedulePointerLeave(){
            if(!IsHandleCreated||IsDisposed||Disposing)return;
            BeginInvoke((Action)(()=>{
                if(IsDisposed||Disposing)return;
                SetPointerOver(RectangleToScreen(ClientRectangle).Contains(Cursor.Position));
            }));
        }
        void SyncInteraction(){
            if(IsDisposed||Disposing)return;
            bool active=InteractionActive;
            if(RemoveButton!=null)RemoveButton.Visible=active;
            if(EditButton!=null)EditButton.Visible=active;
            if(GeneralStyle.AnimationsEnabled&&Visible)hoverAnimation.Start();
            else{hoverAnimation.Stop();interaction=active?1:0;Invalidate(true);}
        }
        internal void ConfigureActions(Button remove,Button edit){
            RemoveButton=remove;EditButton=edit;
            foreach(var button in new[]{remove,edit}){
                button.AutoSize=false;button.MinimumSize=new Size(44,44);button.Size=new Size(44,44);
                button.MouseEnter+=(s,e)=>SetPointerOver(true);button.MouseLeave+=(s,e)=>SchedulePointerLeave();
                button.Enter+=(s,e)=>SyncInteraction();button.Leave+=(s,e)=>SchedulePointerLeave();
                button.Visible=false;Controls.Add(button);button.BringToFront();
            }
            PlaceActions();SyncInteraction();
        }
        internal void PlaceActions(){
            if(RemoveButton==null)return;
            int dpi=DeviceDpi,size=44*dpi/96,inset=5*dpi/96;
            RemoveButton.SetBounds(inset,inset,size,size);
            int art=Math.Min(Width*3/2,Height-CaptionHeight(dpi));
            int line=Math.Max(Font.Height+2*dpi/96,25*dpi/96);
            int titleWidth=Math.Max(1,Width-48*dpi/96);
            int titleHeight=TextRenderer.MeasureText(Text,Font).Width>titleWidth?line*2:line;
            EditButton.SetBounds(Width-size,art+5*dpi/96+(titleHeight-size)/2,size,size);
        }
    }

    internal sealed class HomeCardAction : Button {
        readonly bool remove;
        bool over;
        internal HomeCardAction(bool remove){
            this.remove=remove;
            SetStyle(ControlStyles.UserPaint|ControlStyles.SupportsTransparentBackColor|ControlStyles.OptimizedDoubleBuffer|ControlStyles.ResizeRedraw,true);
            BackColor=Color.Transparent;Cursor=Cursors.Hand;Text="";AccessibleRole=AccessibleRole.PushButton;
        }
        protected override void OnMouseEnter(EventArgs e){over=true;Invalidate();base.OnMouseEnter(e);}
        protected override void OnMouseLeave(EventArgs e){over=false;Invalidate();base.OnMouseLeave(e);}
        protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}
        protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
        protected override void OnPaintBackground(PaintEventArgs e){
            if(Parent==null){base.OnPaintBackground(e);return;}
            var state=e.Graphics.Save();
            try{
                e.Graphics.TranslateTransform(-Left,-Top);
                var args=new PaintEventArgs(e.Graphics,new Rectangle(Left,Top,Width,Height));
                InvokePaintBackground(Parent,args);InvokePaint(Parent,args);
            }finally{e.Graphics.Restore(state);}
        }
        protected override void OnPaint(PaintEventArgs e){
            var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
            int size=(remove?26:30)*DeviceDpi/96;
            var circle=new Rectangle((Width-size)/2,(Height-size)/2,size,size);
            Color ink=Enabled?(remove?Color.White:GeneralStyle.Muted):GeneralStyle.Muted;
            if(remove||over&&Enabled||Focused){
                using(var fill=new SolidBrush(remove?Color.FromArgb(over&&Enabled?210:155,Color.Black):GeneralStyle.Selection))g.FillEllipse(fill,circle);
            }
            if(Focused&&ShowFocusCues)using(var border=new Pen(remove?Color.White:GeneralStyle.Accent,1.5f))g.DrawEllipse(border,circle);
            int icon=(remove?15:20)*DeviceDpi/96;
            LucideIcons.Draw(g,remove?"close":"ellipsis-vertical",new Rectangle((Width-icon)/2,(Height-icon)/2,icon,icon),ink);
        }
    }
}
