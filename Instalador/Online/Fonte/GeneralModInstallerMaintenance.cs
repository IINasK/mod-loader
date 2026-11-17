using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

public sealed partial class GeneralModInstaller {
    // Called under the operation lock, only after these backups were restored and
    // the removed receipt was committed. This is not a sweep of old user data.
    internal void ReleaseUnreferencedBackups(IEnumerable<string> restored) {
        try {
            string journal=Safe.In(records,"pending-install.journal");
            Safe.NoLinks(journal);
            if(File.Exists(journal)||Directory.Exists(journal))return;
            var candidates=new HashSet<string>(restored.Where(FixedData.HashValid),StringComparer.Ordinal);
            if(candidates.Count==0)return;
            foreach(string receipt in Directory.EnumerateFiles(records,"*.json")){
                var record=ReadRecord(receipt,true);
                if(record==null)continue;
                foreach(var file in record.Files)if(file.Before!=null)candidates.Remove(file.Before);
            }
            foreach(string hash in candidates){
                string path=BackupPath(hash);
                try {
                    Safe.NoLinks(path);
                    if(!File.Exists(path))continue;
                    using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Delete))
                    using(var algorithm=SHA256.Create()){
                        if(Safe.Hex(algorithm.ComputeHash(stream))!=hash)continue;
                        File.Delete(path);
                    }
                }catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }catch(IOException){}catch(UnauthorizedAccessException){}
        catch(ArgumentException){}catch(InvalidOperationException){}
    }
}
