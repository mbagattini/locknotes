using System.Security.Cryptography;
using System.Text;

namespace LockNotes;

public static class CryptoService
{
    public static byte[] Encrypt(string plaintext, string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] iv = RandomNumberGenerator.GetBytes(16);

        byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 64);
        byte[] aesKey = keyMaterial[..32];
        byte[] hmacKey = keyMaterial[32..];
        CryptographicOperations.ZeroMemory(keyMaterial);

        byte[] ciphertext;
        using (var aes = Aes.Create())
        {
            aes.Key = aesKey;
            aes.IV = iv;
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
                cs.Write(bytes, 0, bytes.Length);
                cs.FlushFinalBlock();
            }
            ciphertext = ms.ToArray();
        }

        // HMAC su salt || iv || ciphertext (Encrypt-then-MAC)
        byte[] macInput = new byte[salt.Length + iv.Length + ciphertext.Length];
        Buffer.BlockCopy(salt, 0, macInput, 0, 16);
        Buffer.BlockCopy(iv, 0, macInput, 16, 16);
        Buffer.BlockCopy(ciphertext, 0, macInput, 32, ciphertext.Length);

        byte[] tag;
        using (var hmac = new HMACSHA256(hmacKey))
            tag = hmac.ComputeHash(macInput);

        // Layout: salt(16) || iv(16) || ciphertext(N) || hmac(32)
        byte[] result = new byte[macInput.Length + tag.Length];
        Buffer.BlockCopy(macInput, 0, result, 0, macInput.Length);
        Buffer.BlockCopy(tag, 0, result, macInput.Length, tag.Length);

        CryptographicOperations.ZeroMemory(aesKey);
        CryptographicOperations.ZeroMemory(hmacKey);

        return result;
    }

    public static string Decrypt(byte[] data, string password)
    {
        if (data.Length < 16 + 16 + 16 + 32) // salt + iv + min_block + tag
            throw new CryptographicException("File non valido.");

        byte[] salt = new byte[16];
        byte[] iv = new byte[16];
        byte[] tag = new byte[32];
        int ciphertextLength = data.Length - 16 - 16 - 32;
        byte[] ciphertext = new byte[ciphertextLength];

        Buffer.BlockCopy(data, 0, salt, 0, 16);
        Buffer.BlockCopy(data, 16, iv, 0, 16);
        Buffer.BlockCopy(data, 32, ciphertext, 0, ciphertextLength);
        Buffer.BlockCopy(data, 32 + ciphertextLength, tag, 0, 32);

        byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 64);
        byte[] aesKey = keyMaterial[..32];
        byte[] hmacKey = keyMaterial[32..];
        CryptographicOperations.ZeroMemory(keyMaterial);

        byte[] macInput = new byte[16 + 16 + ciphertextLength];
        Buffer.BlockCopy(data, 0, macInput, 0, 32 + ciphertextLength);

        byte[] computedTag;
        using (var hmac = new HMACSHA256(hmacKey))
            computedTag = hmac.ComputeHash(macInput);

        if (!CryptographicOperations.FixedTimeEquals(computedTag, tag))
        {
            CryptographicOperations.ZeroMemory(aesKey);
            CryptographicOperations.ZeroMemory(hmacKey);
            throw new CryptographicException("Password errata o file corrotto.");
        }

        string plaintext;
        using (var aes = Aes.Create())
        {
            aes.Key = aesKey;
            aes.IV = iv;
            using var ms = new MemoryStream(ciphertext);
            using var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);
            plaintext = sr.ReadToEnd();
        }

        CryptographicOperations.ZeroMemory(aesKey);
        CryptographicOperations.ZeroMemory(hmacKey);

        return plaintext;
    }
}
