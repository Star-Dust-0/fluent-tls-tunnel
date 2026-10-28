namespace FluentTlsTunnel.Core;
public static class ConfigCrypto
{
    const int Iterations=600000;
    public record Envelope(string Format,int Version,int Iterations,string Salt,string Nonce,string Data,string Tag);
    public static string Encode(Profile p,string? password)
    {
        var json=System.Text.Json.JsonSerializer.Serialize(p,new System.Text.Json.JsonSerializerOptions {WriteIndented=true});
        if(string.IsNullOrEmpty(password)) return json;
        if(password.Length<8) throw new ArgumentException("Use an export password of at least 8 characters.");
        var salt=System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        var nonce=System.Security.Cryptography.RandomNumberGenerator.GetBytes(12);
        var key=System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password,salt,Iterations,System.Security.Cryptography.HashAlgorithmName.SHA256,32);
        try
        {
            var plain=System.Text.Encoding.UTF8.GetBytes(json); var cipher=new byte[plain.Length];var tag=new byte[16];
            using var aes=new System.Security.Cryptography.AesGcm(key,16);
            aes.Encrypt(nonce,plain,cipher,tag,"FTLSC1"u8);
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);
            return System.Text.Json.JsonSerializer.Serialize(new Envelope("ftlsc-encrypted",1,Iterations,Convert.ToBase64String(salt),Convert.ToBase64String(nonce),Convert.ToBase64String(cipher),Convert.ToBase64String(tag)));
        }
        finally {System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);}
    }
    public static bool Encrypted(string text)
    {
        using var doc=System.Text.Json.JsonDocument.Parse(text);
        return doc.RootElement.TryGetProperty("Format",out var format)&&format.GetString()=="ftlsc-encrypted";
    }
    public static Profile Decode(string text,string? password)
    {
        if(!Encrypted(text)) return System.Text.Json.JsonSerializer.Deserialize<Profile>(text)??throw new ArgumentException("Invalid config.");
        var e=System.Text.Json.JsonSerializer.Deserialize<Envelope>(text)??throw new ArgumentException("Invalid encrypted config.");
        if(e.Version!=1 || e.Iterations!=Iterations || string.IsNullOrEmpty(password)) throw new ArgumentException("Unsupported config format or missing password.");
        var salt=Convert.FromBase64String(e.Salt);var nonce=Convert.FromBase64String(e.Nonce);var tag=Convert.FromBase64String(e.Tag);var cipher=Convert.FromBase64String(e.Data);
        if(salt.Length!=16||nonce.Length!=12||tag.Length!=16||cipher.Length>65536) throw new ArgumentException("Invalid encrypted config.");
        var key=System.Security.Cryptography.Rfc2898DeriveBytes.Pbkdf2(password,salt,e.Iterations,System.Security.Cryptography.HashAlgorithmName.SHA256,32);
        var plain=new byte[cipher.Length];
        try {using var aes=new System.Security.Cryptography.AesGcm(key,16);aes.Decrypt(nonce,cipher,tag,plain,"FTLSC1"u8);return System.Text.Json.JsonSerializer.Deserialize<Profile>(plain)??throw new ArgumentException("Invalid config.");}
        finally {System.Security.Cryptography.CryptographicOperations.ZeroMemory(key);System.Security.Cryptography.CryptographicOperations.ZeroMemory(plain);}
    }
}
