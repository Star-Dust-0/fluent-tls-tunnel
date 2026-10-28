using Windows.Security.Credentials;
using FluentTlsTunnel.Core;
namespace FluentTlsTunnel;
static class PasswordStore
{
    static string DirectoryPath => Path.Combine(Profile.DataDir, "credentials");
    static string Resource(Profile p) => "FluentTlsTunnel:" + p.Endpoint;
    public static string Load(Profile p)
    {
        try
        {
            var saved = SecurePasswordFile.Load(DirectoryPath, p.Endpoint, p.Username);
            if (saved.Length > 0) return saved;
            // Read previously saved credentials when available. Never depend on WinRT Vault writes.
            var old = new PasswordVault().Retrieve(Resource(p), p.Username);
            old.RetrievePassword();
            SecurePasswordFile.Save(DirectoryPath, p.Endpoint, p.Username, old.Password);
            return old.Password;
        }
        catch { return ""; }
    }
    public static void Save(Profile p, string password)
    {
        SecurePasswordFile.Save(DirectoryPath, p.Endpoint, p.Username, password);
        // Best effort removal only of this app/profile's legacy copy after a successful secure write.
        try { var vault = new PasswordVault(); vault.Remove(vault.Retrieve(Resource(p), p.Username)); } catch { }
    }
}
