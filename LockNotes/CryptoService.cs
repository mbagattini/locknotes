using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace LockNotes;

// Formato file .stxt:
//   magic "LKN2" (4) | version (1) | lenR (int32 LE, 4) | blobRecovery | lenP (4) | blobPassword | body
// dove blobRecovery, blobPassword e body sono container cifrati autonomi (EncryptBlock):
//   salt(16) | iv(16) | ciphertext AES-256-CBC | HMAC-SHA256(32) su salt|iv|ciphertext
// - body         = contenuto cifrato con la password
// - blobRecovery = codice di recupero cifrato con la password (per mostrarlo a chi conosce la password)
// - blobPassword = password cifrata con il codice di recupero (per lo sblocco d'emergenza)
public static class CryptoService
{
    static readonly byte[] Magic = "LKN2"u8.ToArray();
    const byte Version = 2;
    const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    // ---- API ----

    public static byte[] Encrypt(string plaintext, string password, string recoveryCode)
    {
        byte[] blobRecovery = EncryptBlock(recoveryCode, password);
        byte[] blobPassword = EncryptBlock(password, NormalizeRecoveryCode(recoveryCode));
        byte[] body = EncryptBlock(plaintext, password);

        using var ms = new MemoryStream();
        ms.Write(Magic);
        ms.WriteByte(Version);
        WriteBlock(ms, blobRecovery);
        WriteBlock(ms, blobPassword);
        ms.Write(body);
        return ms.ToArray();
    }

    public static (string Text, string RecoveryCode) Decrypt(byte[] data, string password)
    {
        var (blobRecovery, _, body) = Parse(data);
        return (DecryptBlock(body, password), DecryptBlock(blobRecovery, password));
    }

    // Sblocco d'emergenza: il codice di recupero decifra la password, la password il contenuto
    public static (string Text, string Password) DecryptWithRecoveryCode(byte[] data, string recoveryCode)
    {
        var (_, blobPassword, body) = Parse(data);
        string password = DecryptBlock(blobPassword, NormalizeRecoveryCode(recoveryCode));
        return (DecryptBlock(body, password), password);
    }

    // 15 byte casuali (120 bit) -> Base32 -> 24 caratteri in 6 gruppi da 4 (XXXX-XXXX-...)
    public static string GenerateRecoveryCode()
    {
        byte[] bytes = RandomNumberGenerator.GetBytes(15);
        var raw = new StringBuilder(24);
        int buffer = 0, bits = 0;
        foreach (byte b in bytes)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                bits -= 5;
                raw.Append(Base32Alphabet[(buffer >> bits) & 31]);
            }
        }
        return string.Join("-", raw.ToString().Chunk(4).Select(chunk => new string(chunk)));
    }

    // L'utente puo' trascrivere il codice con o senza trattini/spazi e in minuscolo
    public static string NormalizeRecoveryCode(string input) =>
        new string(input.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    // ---- Layout file ----

    static (byte[] BlobRecovery, byte[] BlobPassword, byte[] Body) Parse(byte[] data)
    {
        if (data.Length < Magic.Length + 1 || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic) || data[Magic.Length] != Version)
            throw new CryptographicException("File non valido.");

        int pos = Magic.Length + 1;
        byte[] blobRecovery = ReadBlock(data, ref pos);
        byte[] blobPassword = ReadBlock(data, ref pos);
        if (pos >= data.Length)
            throw new CryptographicException("File non valido.");
        return (blobRecovery, blobPassword, data[pos..]);
    }

    static void WriteBlock(MemoryStream ms, byte[] block)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(len, block.Length);
        ms.Write(len);
        ms.Write(block);
    }

    static byte[] ReadBlock(byte[] data, ref int pos)
    {
        if (pos + 4 > data.Length)
            throw new CryptographicException("File non valido.");
        int len = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(pos, 4));
        pos += 4;
        if (len < 0 || pos + len > data.Length)
            throw new CryptographicException("File non valido.");
        byte[] block = data[pos..(pos + len)];
        pos += len;
        return block;
    }

    // ---- Container cifrato ----

    static byte[] EncryptBlock(string plaintext, string passphrase)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(16);
        byte[] iv = RandomNumberGenerator.GetBytes(16);

        byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 100_000, HashAlgorithmName.SHA256, 64);
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

    static string DecryptBlock(byte[] data, string passphrase)
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

        byte[] keyMaterial = Rfc2898DeriveBytes.Pbkdf2(passphrase, salt, 100_000, HashAlgorithmName.SHA256, 64);
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
