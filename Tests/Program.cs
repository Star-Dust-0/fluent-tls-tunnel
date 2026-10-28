using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentTlsTunnel.Core;
static void Check(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
Check(Profile.ParseDns("94.140.14.14, 94.140.15.15").Length==2,"DNS IP parsing");
try { Profile.ParseDns("1.1.1.1; Stop-Process"); throw new Exception("injection accepted"); } catch(ArgumentException) { Console.WriteLine("PASS DNS injection rejected"); }
var profile=new Profile {Host="127.0.0.1",Port=443,Sni="test.example",Username="test",VerifyTls=false}; profile.Validate();
Check(!System.Text.Json.JsonSerializer.Serialize(profile).Contains("Password"),"No password serialization");
using var rsa=RSA.Create(2048);
var request=new CertificateRequest("CN=test.example",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
using var cert=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
profile.Port=((IPEndPoint)listener.LocalEndpoint).Port;
var sni="";
var server=Task.Run(async ()=>
{
 using var peer=await listener.AcceptTcpClientAsync();
 using var tls=new SslStream(peer.GetStream());
 await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificateSelectionCallback=(_,name)=>{sni=name; return cert;} });
 var data=new byte[4]; await tls.ReadExactlyAsync(data); await tls.WriteAsync(data);
});
await using(var bridge=new TlsBridge())
{
 bridge.Start(profile,Console.WriteLine);
 using var local=new TcpClient(); await local.ConnectAsync(IPAddress.Loopback,bridge.Port);
 await local.GetStream().WriteAsync(new byte[]{1,2,3,4});
 var reply=new byte[4]; using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(10)); await local.GetStream().ReadExactlyAsync(reply,timeout.Token);
 Check(reply.SequenceEqual(new byte[]{1,2,3,4}),"Bidirectional TLS bridge");
 await server; Check(sni=="test.example","SNI reaches TLS server");
}
listener.Stop(); Console.WriteLine("All tests passed.");