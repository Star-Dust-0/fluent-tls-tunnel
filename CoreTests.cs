using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentTlsTunnel.Core;
static void Check(bool value,string label) { if(!value) throw new Exception(label); Console.WriteLine("PASS " + label); }
Check(TlsBridge.NormalizeAddress(IPAddress.Parse("::ffff:141.98.233.74")).ToString()=="141.98.233.74","IPv4-mapped TLS endpoint is IPv4 for routing");
Check(TlsBridge.NormalizeAddress(IPAddress.Parse("2001:db8::1")).ToString()=="2001:db8::1","Native IPv6 TLS endpoint remains IPv6");
Check(Profile.ParseDns("94.140.14.14, 94.140.15.15").Length==2,"DNS IP parsing");
try { Profile.ParseDns("1.1.1.1; Stop-Process"); throw new Exception("injection accepted"); } catch(ArgumentException) { Console.WriteLine("PASS DNS injection rejected"); }
var profile=new Profile {Host="127.0.0.1",Port=443,Sni="test.example",Username="test",VerifyTls=false}; profile.Validate();
Check(!System.Text.Json.JsonSerializer.Serialize(profile).Contains("Password"),"No password serialization");
using var rsa=RSA.Create(2048);
var request=new CertificateRequest("CN=test.example",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
using var generated=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
// Schannel requires an imported key container, unlike Linux OpenSSL.
using var cert=new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
var listener=new TcpListener(IPAddress.Loopback,0); listener.Start();
profile.Port=((IPEndPoint)listener.LocalEndpoint).Port;
var sni="";
var server=Task.Run(async ()=>
{
 try
 {
 using var peer=await listener.AcceptTcpClientAsync();
 using var tls=new SslStream(peer.GetStream());
 await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificateSelectionCallback=(_,name)=>{sni=name; return cert;} });
 var data=new byte[4]; await tls.ReadExactlyAsync(data); await tls.WriteAsync(data);
 } catch(Exception error) {Console.Error.WriteLine("TLS test server: " + error); throw;}
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

var encrypted=ConfigCrypto.Encode(profile,"test-password");
Check(ConfigCrypto.Encrypted(encrypted),"Encrypted envelope");
Check(ConfigCrypto.Decode(encrypted,"test-password").Sni==profile.Sni,"AES-GCM config roundtrip");
try { ConfigCrypto.Decode(encrypted,"wrong-password"); throw new Exception("Wrong password accepted"); } catch(System.Security.Cryptography.CryptographicException) { Console.WriteLine("PASS wrong password rejected"); }

var tampered=System.Text.Json.JsonSerializer.Deserialize<ConfigCrypto.Envelope>(encrypted)!;
var data=Convert.FromBase64String(tampered.Data);data[0]^=1;
var changed=System.Text.Json.JsonSerializer.Serialize(tampered with {Data=Convert.ToBase64String(data)});
try { ConfigCrypto.Decode(changed,"test-password"); throw new Exception("Tampering accepted"); } catch(System.Security.Cryptography.CryptographicException) { Console.WriteLine("PASS tampered export rejected"); }
