using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralMenuHost : IMessageFilter, IDisposable {
    GeneralDialog current;
    Form owner;
    Control anchor;
    bool disposed,filtering,anchorPressed;
    internal GeneralDialog Current {get{return current;}}

    internal void Toggle(Form owner,Control anchor,Func<GeneralDialog> create,Action<GeneralDialog> accepted){
        if(disposed||owner==null||owner.IsDisposed||anchor==null||anchor.IsDisposed)return;
        if(current!=null&&Object.ReferenceEquals(this.anchor,anchor)){current.Complete(DialogResult.Cancel);return;}
        CloseImmediately();
        var menu=create();if(menu==null)return;
        current=menu;this.owner=owner;this.anchor=anchor;anchorPressed=false;
        menu.ShowInTaskbar=false;menu.ShowCloseButton(false);menu.AnchorTo(anchor);
        bool finalized=false;
        Action finish=null;
        FormClosedEventHandler closed=(s,e)=>finish();
        EventHandler deleted=(s,e)=>finish();
        EventHandler deactivated=(s,e)=>{
            if(!Object.ReferenceEquals(current,menu))return;
            if(anchorPressed||AnchorContains(Cursor.Position)&&(Control.MouseButtons&MouseButtons.Left)!=0)return;
            menu.Complete(DialogResult.Cancel);
        };
        EventHandler ownerDeleted=(s,e)=>Dispose();
        finish=()=>{
            if(finalized)return;finalized=true;
            bool apply=!disposed&&!owner.IsDisposed&&menu.DialogResult==DialogResult.OK;
            menu.FormClosed-=closed;menu.Disposed-=deleted;menu.Deactivate-=deactivated;owner.Disposed-=ownerDeleted;
            if(Object.ReferenceEquals(current,menu))ClearCurrent();
            if(!menu.IsDisposed)menu.Dispose();
            if(apply&&accepted!=null)accepted(menu);
        };
        menu.FormClosed+=closed;menu.Disposed+=deleted;menu.Deactivate+=deactivated;owner.Disposed+=ownerDeleted;
        Application.AddMessageFilter(this);filtering=true;
        try{menu.Show(owner);}catch{CloseImmediately();throw;}
    }

    bool AnchorContains(Point screen){return anchor!=null&&!anchor.IsDisposed&&anchor.RectangleToScreen(anchor.ClientRectangle).Contains(screen);}
    internal void PointerDown(Point screen,bool leftButton=true){
        if(current==null||current.IsDisposed||!current.Visible)return;
        anchorPressed=leftButton&&AnchorContains(screen);
        if(!anchorPressed&&!current.Bounds.Contains(screen))current.Complete(DialogResult.Cancel);
    }
    internal void PointerUp(){
        if(!anchorPressed||owner==null||owner.IsDisposed||!owner.IsHandleCreated)return;
        var menu=current;var window=owner;
        try{window.BeginInvoke((Action)(()=>{
            if(disposed||window.IsDisposed||!Object.ReferenceEquals(current,menu))return;
            anchorPressed=false;
            if(menu!=null&&!menu.IsDisposed&&!menu.ContainsFocus)menu.Complete(DialogResult.Cancel);
        }));}catch(InvalidOperationException){}
    }
    public bool PreFilterMessage(ref Message message){
        int code=message.Msg;
        if(code==0x0201||code==0x0204||code==0x0207||code==0x020B)PointerDown(Cursor.Position,code==0x0201);
        else if(code==0x0202)PointerUp();
        return false;
    }
    void ClearCurrent(){
        current=null;owner=null;anchor=null;anchorPressed=false;
        if(filtering){Application.RemoveMessageFilter(this);filtering=false;}
    }
    void CloseImmediately(){
        var previous=current;if(previous==null)return;
        previous.DialogResult=DialogResult.Cancel;
        ClearCurrent();
        if(!previous.IsDisposed){previous.Hide();previous.Dispose();}
    }
    internal void Close(){CloseImmediately();}
    public void Dispose(){if(disposed)return;disposed=true;CloseImmediately();}
}
