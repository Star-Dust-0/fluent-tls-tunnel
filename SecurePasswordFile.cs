using System.Security.Cryptography;
using System.Text;
namespace FluentTlsTunnel.Core;
public static class SecurePasswordFile
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FluentTlsTunnel.Password.v1");
    static string FileName(string directory, string endpoint, string username) => Path.Combine(directory,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endpoint + "\\0" + username))) + ".bin");
    public static string Load(string directory, string endpoint, string username)
    {
        var path = FileName(directory, endpoint, username);
        if (!File.Exists(path)) return "";
        var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
        try { return Encoding.UTF8.GetString(plain); }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public static void Save(string directory, string endpoint, string username, string password)
    {
        var path = FileName(directory, endpoint, username);
        if (password.Length == 0) { if (File.Exists(path)) File.Delete(path); return; }
        var plain = Encoding.UTF8.GetBytes(password);
        byte[] cipher;
        try { cipher = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        Directory.CreateDirectory(directory);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllBytes(temp, cipher); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}