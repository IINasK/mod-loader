using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

internal static class GeneralP2PDiagnosticTests {
    internal static void Run(string root,string certificateFile){
        Directory.CreateDirectory(root);
        string settings=Safe.In(root,"diagnostic-preferences.json");
        var report=new P2PDiagnosticReport{Schema=1,Id="0123456789abcdef",Side="receiver",Phase="peer_unreachable"};
        var listener=new TcpListener(IPAddress.Loopback,0);
        var original=ServicePointManager.ServerCertificateValidationCallback;
        using(var certificate=new X509Certificate2(certificateFile)){
            listener.Start();
            try{
                int port=((IPEndPoint)listener.LocalEndpoint).Port;
                string endpoint="https://127.0.0.1:"+port+"/report";
                Check(P2PDiagnosticPreferences.HttpsEndpoint("http://127.0.0.1/report")==null&&
                    P2PDiagnosticPreferences.HttpsEndpoint("https://user:password@example.test/report")==null,
                    "Only HTTPS without embedded credentials is allowed.");
                Check(!P2PDiagnosticPreferences.Load(settings).Enabled,"Reports default to local only.");
                Safe.WriteJson(settings,new P2PDiagnosticPreferences{Endpoint=endpoint,Enabled=false});
                P2PDiagnosticPreferences.SendIfEnabled(settings,report);
                Check(!listener.Pending(),"A saved endpoint without opt-in never connects.");

                // Trust the fixture's leaf only inside this self-test process.
                ServicePointManager.ServerCertificateValidationCallback=(source,peer,chain,errors)=>
                    peer!=null&&peer.GetCertHashString()==certificate.Thumbprint;
                Safe.WriteJson(settings,new P2PDiagnosticPreferences{Endpoint=endpoint,Enabled=true});
                var received=Task.Run(()=>Receive(listener,certificate));
                P2PDiagnosticPreferences.SendIfEnabled(settings,report);
                Check(received.Wait(10000)&&received.Result==Safe.Json.Serialize(report),"The HTTPS receiver got the exact opt-in report.");
                Safe.WriteJson(settings,new P2PDiagnosticPreferences{Endpoint=endpoint,Enabled=false});
                P2PDiagnosticPreferences.SendIfEnabled(settings,report);
                Check(!listener.Pending(),"Disabling the preference stops further connections.");
                Safe.WriteJson(Safe.In(root,"resultado-p2p-diagnostics.json"),new{Passed=true,LoopbackOnly=true,Tests=5});
            }finally{
                ServicePointManager.ServerCertificateValidationCallback=original;
                listener.Stop();
            }
        }
    }
    static string Receive(TcpListener listener,X509Certificate2 certificate){
        using(var client=listener.AcceptTcpClient())
        using(var transport=new SslStream(client.GetStream())){
            client.ReceiveTimeout=8000;client.SendTimeout=8000;
            transport.AuthenticateAsServer(certificate,false,SslProtocols.Tls12,false);
            using(var reader=new StreamReader(transport,Encoding.UTF8,false,1024,true)){
                Check(reader.ReadLine()=="POST /report HTTP/1.1","The receiver gets a POST.");
                int length=0;bool json=false,expect=false;
                string line;
                while(!String.IsNullOrEmpty(line=reader.ReadLine())){
                    if(line.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase))
                        length=Int32.Parse(line.Substring(15),CultureInfo.InvariantCulture);
                    if(line.Equals("Content-Type: application/json",StringComparison.OrdinalIgnoreCase))json=true;
                    if(line.Equals("Expect: 100-continue",StringComparison.OrdinalIgnoreCase))expect=true;
                }
                Check(json&&length>0&&length<4096,"The request carries bounded JSON.");
                if(expect)Write(transport,"HTTP/1.1 100 Continue\r\n\r\n");
                var content=new char[length];
                Check(reader.ReadBlock(content,0,length)==length,"The full report was received.");
                Write(transport,"HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                return new string(content);
            }
        }
    }
    static void Write(Stream stream,string value){
        var bytes=Encoding.ASCII.GetBytes(value);
        stream.Write(bytes,0,bytes.Length);stream.Flush();
    }
    static void Check(bool passed,string message){if(!passed)throw new IOException(message);}
}
