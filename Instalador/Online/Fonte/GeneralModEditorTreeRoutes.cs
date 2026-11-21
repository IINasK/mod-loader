using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;

internal sealed partial class GeneralArchiveDestinations {
    readonly HashSet<string> packageFolders=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<TreeNode,string> virtualFolders=new Dictionary<TreeNode,string>();
    readonly TreeNode virtualRoot=new TreeNode(T("Destinos escolhidos","Chosen destinations")){Tag="%GAME%"};

    bool IsVirtualNode(TreeNode node){return node==virtualRoot||node!=null&&virtualFolders.ContainsKey(node);}
    void ClearVirtualFolders() {
        folded.RemoveWhere(IsVirtualNode);virtualRoot.Nodes.Clear();virtualFolders.Clear();
        virtualFolders[virtualRoot]="%GAME%";
    }
    void CapturePackageFolders() {
        packageFolders.Clear();packageFolders.Add("");
        foreach(var pair in nodes.Where(pair=>pair.Value.Nodes.Count>0))packageFolders.Add(pair.Key);
    }
    string CanonicalFolder(string value) {return ReadRoutes(game,new Dictionary<string,string>{{"",value}}).Single().Destination;}
    static string FolderParent(string token) {int slash=token.LastIndexOf('/');return slash<0?token:token.Substring(0,slash);}
    static string FolderName(string token) {int slash=token.LastIndexOf('/');return slash<0?token:token.Substring(slash+1);}
    static bool DescendantOf(TreeNode node,TreeNode ancestor) {
        for(var current=node;current!=null;current=current.Parent)if(ReferenceEquals(current,ancestor))return true;
        return false;
    }
    TreeNode DestinationParent(string folder,TreeNode source) {
        foreach(var candidate in nodes.Values.Where(node=>FolderNode(node)&&node!=source)) {
            if(DescendantOf(candidate,source))continue;
            try {if(String.Equals(CanonicalFolder(NodeFolder(candidate)),folder,StringComparison.OrdinalIgnoreCase))return candidate;}
            catch(IOException){}catch(ArgumentException){}catch(NotSupportedException){}
        }
        return VirtualFolder(folder);
    }
    TreeNode VirtualFolder(string token) {
        var existing=virtualFolders.FirstOrDefault(pair=>pair.Key!=virtualRoot&&String.Equals(pair.Value,token,StringComparison.OrdinalIgnoreCase));
        if(existing.Key!=null)return existing.Key;
        var parts=token.Split('/');TreeNode parent=virtualRoot;string path="";
        foreach(string part in parts) {
            path=path.Length==0?part:path+"/"+part;
            string wanted=path;
            var found=virtualFolders.FirstOrDefault(pair=>pair.Key!=virtualRoot&&String.Equals(pair.Value,wanted,StringComparison.OrdinalIgnoreCase));
            if(found.Key!=null){parent=found.Key;continue;}
            var node=parent.Nodes.Add(part);node.Tag=path;virtualFolders[node]=path;parent=node;
        }
        return parent;
    }

    // Source tags remain immutable package-relative identities. Only the
    // displayed parents follow the pending destination routes.
    void ApplyVisualPlacement() {
        var sourceNodes=nodes.Where(pair=>pair.Key.Length>0).ToArray();
        foreach(var pair in sourceNodes)pair.Value.Remove();
        root.Nodes.Clear();ClearVirtualFolders();
        foreach(var pair in sourceNodes.OrderBy(pair=>pair.Key.Count(ch=>ch=='/')).ThenBy(pair=>pair.Key,StringComparer.OrdinalIgnoreCase)) {
            TreeNode parent;nodes.TryGetValue(ParentPath(pair.Key),out parent);
            (parent??root).Nodes.Add(pair.Value);pair.Value.Text=Path.GetFileName(pair.Key);
        }
        root.Text=T("Raiz do pacote","Archive root");
        List<GeneralInstallRoute> routes;
        try {routes=ReadRoutes(game,drafts);}
        catch(IOException){return;}catch(ArgumentException){return;}catch(NotSupportedException){return;}
        var rootRoute=routes.FirstOrDefault(route=>route.Folder.Length==0);
        if(rootRoute!=null&&rootRoute.Destination!="%GAME%")root.Text+=" → "+rootRoute.Destination;
        foreach(var route in routes.Where(route=>route.Folder.Length>0).OrderBy(route=>route.Folder.Count(ch=>ch=='/'))) {
            TreeNode source;if(!nodes.TryGetValue(route.Folder,out source))continue;
            bool folder=packageFolders.Contains(route.Folder);
            string wanted=folder?FolderParent(route.Destination):route.Destination;
            var parent=DestinationParent(wanted,source);
            if(parent==source||DescendantOf(parent,source))continue;
            source.Remove();parent.Nodes.Add(source);folded.Remove(parent);
            if(folder&&!String.Equals(FolderName(route.Destination),Path.GetFileName(route.Folder),StringComparison.OrdinalIgnoreCase))
                source.Text=Path.GetFileName(route.Folder)+" → "+FolderName(route.Destination);
        }
    }
    void RefreshVisualPlacement(TreeNode selected) {
        ApplyVisualPlacement();RefreshTree(selected??root);
    }
}
