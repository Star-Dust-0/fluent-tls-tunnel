using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentTlsTunnel;
using FluentTlsTunnel.Core;
if(!OperatingSystem.IsWindows())return;
if(args.Contains("--owner")){await Task.Delay(Timeout.Infinite);return;}
Environment.SetEnvironmentVariable("FLUENT_TLS_SKIP_NAME_PROBE","1");
{
 var aaaa=new byte[]{0x12,0x34,1,0,0,1,0,0,0,0,0,0,7,101,120,97,109,112,108,101,3,99,111,109,0,0,28,0,1};
 var empty=WindowsRouting.EmptyAaaaAnswer(aaaa);
 Check(empty!=null&&empty[0]==0x12&&empty[1]==0x34&&(empty[2]&0x80)!=0&&(empty[3]&15)==0&&empty[6]==0&&empty[7]==0&&empty.Length==aaaa.Length,"AAAA query gets an empty NOERROR answer with the original ID");
 var a=(byte[])aaaa.Clone();a[26]=1;
 Check(WindowsRouting.EmptyAaaaAnswer(a)==null,"A query is not filtered");
}
using var rsa=RSA.Create(2048);
var certRequest=new CertificateRequest("CN=1.1.1.1",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(IPAddress.Parse("1.1.1.1"));certRequest.CertificateExtensions.Add(san.Build());
certRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true,false,0,true));
using var generated=certRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
using var cert=new X509Certificate2(generated.Export(X509ContentType.Pfx),(string?)null,X509KeyStorageFlags.Exportable);
var socks=new TcpListener(IPAddress.Loopback,19080);socks.Start();int ipv4=0,ipv6=0,dns=0;var dnsTargets=new List<string>();bool denyDns=false;int doh=0;
_=Task.Run(async()=>{while(true){var c=await socks.AcceptTcpClientAsync();_=Task.Run(async()=>{using(c)try{
var s=c.GetStream();var greet=new byte[3];await s.ReadExactlyAsync(greet);await s.WriteAsync(new byte[]{5,0});
var h=new byte[4];await s.ReadExactlyAsync(h);int n=h[3]==1?4:h[3]==4?16:await Byte(s);var target=new byte[n+2];await s.ReadExactlyAsync(target);int port=(target[^2]<<8)|target[^1];
if(port==53&&denyDns){await s.WriteAsync(new byte[]{5,2,0,1,127,0,0,1,0,0});return;}
await s.WriteAsync(new byte[]{5,0,0,1,127,0,0,1,0,0});
if(port==443){
 using var tls=new SslStream(s);await tls.AuthenticateAsServerAsync(cert);
 var header=new List<byte>();while(header.Count<16384){int v=await Byte(tls);header.Add((byte)v);if(header.Count>=4&&Encoding.ASCII.GetString(header.TakeLast(4).ToArray())=="\r\n\r\n")break;}
 var text=Encoding.ASCII.GetString(header.ToArray());Check(text.StartsWith("POST /dns-query"),"DoH uses POST wireformat");
 int size=int.Parse(text.Split("\r\n").Single(x=>x.StartsWith("Content-Length:",StringComparison.OrdinalIgnoreCase)).Split(':')[1].Trim());
 var q=new byte[size];await tls.ReadExactlyAsync(q);q[0]=0;q[1]=0;q[2]=0x81;q[3]=0x80;bool isA=q[^4]==0&&q[^3]==1;q[7]=(byte)(isA?1:0);
 var answer=isA?q.Concat(new byte[]{0xc0,0x0c,0,1,0,1,0,0,0,30,0,4,203,0,113,10}).ToArray():q;
 await tls.WriteAsync(Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Type: application/dns-message\r\nContent-Length: {answer.Length}\r\nConnection: close\r\n\r\n"));await tls.WriteAsync(answer);Interlocked.Increment(ref doh);
}
else if(port==53){lock(dnsTargets)dnsTargets.Add(new IPAddress(target[..^2]).ToString());Interlocked.Increment(ref dns);var size=new byte[2];await s.ReadExactlyAsync(size);var q=new byte[(size[0]<<8)|size[1]];await s.ReadExactlyAsync(q);q[2]=0x81;q[3]=0x80;q[6]=0;bool isA=q[^4]==0&&q[^3]==1;q[7]=(byte)(isA?1:0);var answer=isA?q.Concat(new byte[]{0xc0,0x0c,0,1,0,1,0,0,0,30,0,4,203,0,113,10}).ToArray():q;await s.WriteAsync(new byte[]{(byte)(answer.Length>>8),(byte)answer.Length});await s.WriteAsync(answer);}
else{if(h[3]==4)Interlocked.Increment(ref ipv6);else Interlocked.Increment(ref ipv4);var b=new byte[4096];await s.ReadAsync(b);await s.WriteAsync(System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: 12\r\nConnection: close\r\n\r\nTUN-TEST-OK!"));}
}catch(Exception e){Console.WriteLine("Mock: "+e.Message);}});}});
var diagnostics=new List<string>();
await using var route=new WindowsRouting();
await route.StartAsync("1.1.1.1",19080,CancellationToken.None,log:line=>{lock(diagnostics)diagnostics.Add(line);Console.WriteLine(line);},dnsServers:new[]{"9.9.9.9"});
using var client=new HttpClient(new SocketsHttpHandler{UseProxy=false}){Timeout=TimeSpan.FromSeconds(15)};
Check(await client.GetStringAsync("http://203.0.113.10/")=="TUN-TEST-OK!","IPv4 TCP from non-proxy-aware client through TUN");
Check(await client.GetStringAsync("http://[2001:db8::10]/")=="TUN-TEST-OK!","IPv6 TCP from non-proxy-aware client through TUN");
var addresses=await Dns.GetHostAddressesAsync("fluent-routing-test.invalid");
Check(addresses.Any(a=>a.ToString()=="203.0.113.10"),"Windows DNS policy uses local SSH DNS bridge");
Check(ipv4>0&&ipv6>0&&dns>0,"SOCKS TCP paths observed");
Check(dnsTargets.Contains("9.9.9.9"),"Configured DNS is used instead of hardcoded Cloudflare");
Check(diagnostics.Any(x=>x.Contains("probe SOCKS TCP PASS"))&&diagnostics.Any(x=>x.Contains("probe remote DNS PASS"))&&diagnostics.Any(x=>x.Contains("probe Windows TUN TCP PASS")),"Staged SOCKS DNS and TUN probes observed");
await route.DisposeAsync();
denyDns=true;
await using(var blocked=new WindowsRouting()){
 try{await blocked.StartAsync("1.1.1.1",19080,CancellationToken.None,log:Console.WriteLine,dnsServers:new[]{"9.9.9.9"});throw new Exception("Blocked DNS was incorrectly reported ready");}
 catch(IOException e) when(e.Message.Contains("configured DNS")){Check(!blocked.Healthy,"Blocked remote DNS cannot activate Windows routes");}
}
using(var store=new X509Store(StoreName.Root,StoreLocation.LocalMachine)){
 store.Open(OpenFlags.ReadWrite);store.Add(cert);
 try{
  await using var fallback=new WindowsRouting();
  var fallbackLog=new List<string>();
  await fallback.StartAsync("1.1.1.1",19080,CancellationToken.None,log:line=>{fallbackLog.Add(line);Console.WriteLine(line);});
  Check(doh>0&&fallbackLog.Any(x=>x.Contains("DNS HTTPS fallback PASS")),"Blocked TCP DNS falls back to HTTPS through SOCKS");
  Check((await Dns.GetHostAddressesAsync("fluent-https-test.invalid")).Any(x=>x.ToString()=="203.0.113.10"),"Windows DNS receives HTTPS answer with original ID");
  await fallback.DisposeAsync();
 }finally{store.Remove(cert);}
}
Console.WriteLine("PASS normal routing restore; route/NRPT comparison is in PowerShell");
static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
static async Task<int> Byte(Stream s){var b=new byte[1];await s.ReadExactlyAsync(b);return b[0];}
