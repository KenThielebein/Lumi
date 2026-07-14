using System;
using System.Security.Cryptography;
using System.Text;

namespace Lumi.Config
{
    public static class DPAPIHelper
    {
        public static string Encrypt(string plaintext)
        {
            if (string.IsNullOrEmpty(plaintext)) return "";
            var bytes     = Encoding.UTF8.GetBytes(plaintext);
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
            return "dpapi:" + Convert.ToBase64String(encrypted);
        }

        public static string Decrypt(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (!value.StartsWith("dpapi:")) return value; // Plaintext-Migration

            try
            {
                var bytes     = Convert.FromBase64String(value["dpapi:".Length..]);
                var decrypted = ProtectedData.Unprotect(bytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(decrypted);
            }
            catch
            {
                return "";
            }
        }
    }
}
