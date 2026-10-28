using FluentTlsTunnel.Core;
using System.Text;
var dir = Path.Combine(Path.GetTempPath(), "tls-credentials-test-" + Guid.NewGuid().ToString("N"));
try
{
    string password = "Test-only-secret-عربي-$!";
    SecurePasswordFile.Save(dir, "example.invalid:443", "test-user", password);
    if (SecurePasswordFile.Load(dir, "example.invalid:443", "test-user") != password) throw new Exception("DPAPI roundtrip failed");
    var bytes = File.ReadAllBytes(Directory.GetFiles(dir).Single());
    if (Encoding.UTF8.GetString(bytes).Contains(password)) throw new Exception("Plaintext secret on disk");
    if (SecurePasswordFile.Load(dir, "example.invalid:443", "other-user") != "") throw new Exception("Username isolation failed");
    if (SecurePasswordFile.Load(dir, "other.invalid:443", "test-user") != "") throw new Exception("Endpoint isolation failed");
    SecurePasswordFile.Save(dir, "example.invalid:443", "test-user", "replacement");
    if (SecurePasswordFile.Load(dir, "example.invalid:443", "test-user") != "replacement") throw new Exception("Overwrite failed");
    SecurePasswordFile.Save(dir, "example.invalid:443", "test-user", "");
    if (SecurePasswordFile.Load(dir, "example.invalid:443", "test-user") != "") throw new Exception("Removal failed");
    File.WriteAllText(dir + "-blocked", "not a directory");
    try { SecurePasswordFile.Save(dir + "-blocked", "example.invalid:443", "test-user", password); throw new Exception("Write failure hidden"); }
    catch (IOException) { }
    Console.WriteLine("PASS: current-user DPAPI, no plaintext, identity isolation, replacement, removal, storage failure");
}
finally { if (Directory.Exists(dir)) Directory.Delete(dir,true); if (File.Exists(dir+"-blocked")) File.Delete(dir+"-blocked"); }
