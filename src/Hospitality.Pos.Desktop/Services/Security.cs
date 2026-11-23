using System.Security.Cryptography;
using System.Text;

namespace Hospitality.Pos.Desktop.Services;

public static class Security
{
    private const string Prefix = "restrunt::";

    public static string HashPin(string pin)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(Prefix + pin));
        return Convert.ToHexString(bytes);
    }

    public static bool VerifyPin(string pin, string hash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HashPin(pin)),
            Encoding.UTF8.GetBytes(hash));
}
