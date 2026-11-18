using System;
using System.Drawing;
using System.Windows.Forms;

internal sealed class GeneralClickAway : IMessageFilter,IDisposable {
    readonly Control boundary;
    readonly Action close;
    readonly Form owner;
    bool disposed;
    internal GeneralClickAway(Control boundary,Action close){
        this.boundary=boundary;this.close=close;owner=boundary.FindForm();
        if(owner!=null)owner.Deactivate+=Deactivate;
        Application.AddMessageFilter(this);
    }
    void Deactivate(object sender,EventArgs args){if(!disposed)close();}
    internal void PointerDown(Point screen){
        if(!disposed&&!boundary.IsDisposed&&boundary.Visible&&!boundary.RectangleToScreen(boundary.ClientRectangle).Contains(screen))close();
    }
    public bool PreFilterMessage(ref Message message){
        int code=message.Msg;
        bool client=code==0x201||code==0x204||code==0x207||code==0x20b;
        bool frame=code==0xa1||code==0xa4||code==0xa7||code==0xab;
        if(client||frame){
            long packed=message.LParam.ToInt64();
            var point=new Point((short)(packed&0xffff),(short)((packed>>16)&0xffff));
            var source=client?Control.FromHandle(message.HWnd):null;
            if(source!=null)point=source.PointToScreen(point);else if(client)point=Cursor.Position;
            PointerDown(point);
        }else if(code==0x20a||code==0x20e){if(!disposed)close();}
        return false;
    }
    public void Dispose(){
        if(disposed)return;disposed=true;Application.RemoveMessageFilter(this);
        if(owner!=null)owner.Deactivate-=Deactivate;
    }
}
