using System;
using System.IO;

// Archive identities read from each exact Nexus file card; these grant download/cache only, never installation.
internal static class FacialSourcePins {
    internal static FixedAsset For(FacialDownload item){
        switch(item.ModId+"/"+item.FileId){
            case "16930/99414":return Asset(item,"VTK body user - Unique eyes compatible-16930-1-06-1737836016.zip","9f449f6138344ebbcb4d2a06111106f78fc1c2cc04b1c4c6efc66fc5bef35c06",59984179L,"zip");
            case "18594/95893":return Asset(item,"Unique Eyes - Core V3.0 - Default-18594-1-00-1735121380.zip","8da72a61a4d30b409e6901d64148541837ddb8a746a342de2b9f7f3c01fdc0c5",480973L,"zip");
            case "2422/30004":return Asset(item,"KALA EYES HANAKO VERSION-2422-1-2-1665003574.rar","b1bf4490d14790e4918e7878e34a255ecb87e2273eda8f4b0f84206e66a9226c",2588383L,"tar");
            case "1873/56673":return Asset(item,"Microdetails2.0-1873-2-0-1699319165.7z","0daa9585a685d9670610c014452518a36fcddb406e0e4050e84093e90eebcd17",0L,"tar");
            case "1873/12061":return Asset(item,"Ava Complexions All in one-1873-1-7-1619142695.7z","5ab4da802491d7aac49c226c4581db711d1df8de4bf3668b4b71005c47ad6bb3",0L,"tar");
            case "7341/160715":return Asset(item,"Alternative Skin Material","98ae7dde8af6a31532fb09c1321870d12ecb350859c2514794b32cfaa79eac87",0L,"raw");
            case "7474/39563":return Asset(item,"Realistic Facial Details - Feminine V Main-7474-1-1676845605.zip","8891d23b6a3dd18b74c0ef663e21592fd115b4ad1d6857f88a94ad970611106c",0L,"zip");
            case "7474/39566":return Asset(item,"Realistic Facial Details - Feminine V Shine - Vanilla HD-7474-1-1676845829.zip","f8cb0348d27b0b0badf9947c16c2e7a719e1842b109e3878c8039bda6445c1ec",0L,"zip");
            case "7290/94454":return Asset(item,"Classics-7290-1-1-1733914850.zip","f341d1978786a3fa548002ddf9389202a9b40549ffdb273098c03561a2498767",0L,"zip");
            case "11033/58122":return Asset(item,"Alternate Eyeliners Collection-11033-2-02-1700300331.7z","1f0cfda29346db7d1ae16fefb559cfc85adc5fd2d6292e7193abd0cb4d14bba9",0L,"tar");
            case "6788/35570":return Asset(item,"NLD_CCFix_Complete-6788-1-0-1671150718.zip","a8e7165cdabb2577c4c3ec2f2edc60b343687b01fd046bf9a204684a4a1436ac",0L,"zip");
            case "4131/58415":return Asset(item,"Darker Eyebrows-4131-1-1700445540.7z","aaded8cf9a42c77a66783e436005ff859dab5e8df4433fdfd75db70fea433b17",0L,"tar");
            case "1975/8720":return Asset(item,"Eyebrows no.5 - edit 02 - slightly improved original v2-1975-1-0-1615657733.zip","75bfd1537bee180dd4d6458adf5206562b1d3afef25eadfe201b8fef20aa609c",0L,"zip");
            case "1915/8374":return Asset(item,"Edit no.1 - improved original makeup-1915-1-0-1615139312.zip","585fd50fa745af947dc62c3cf9d5beddbb7f4e2056f8be13cc0d111a7433adcc",0L,"zip");
            default:throw new IOException("Arquivo facial sem identidade oficial fixada.");
        }
    }
    static FixedAsset Asset(FacialDownload item,string name,string sha,long size,string format){
        return new FixedAsset{Id=item.Id,Name=name,ModId=item.ModId,FileId=item.FileId,Url=item.Url,Sha256=sha,Size=size,Format=format};
    }
}

