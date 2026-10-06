using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace AionDPS.Feedback;

/// <summary>
/// Encrypts a recording on the user's machine so that only the maintainer can read it: a random
/// AES-256-GCM key encrypts the gzip-compressed data, and that key is wrapped with the maintainer's
/// RSA public key (assets/feedback/public.pem). The private key never leaves the maintainer's
/// machine, so neither the server nor the git repository that stores the file can decrypt it.
/// Layout: "ADFB1" | u16 BE wrapped-key length | wrapped key | 12-byte nonce | 16-byte tag | ciphertext.
/// Backend/scripts/feedback-decrypt.mjs reads it back.
/// </summary>
public static class FeedbackCrypto
{
    private static readonly byte[] Magic = "ADFB1"u8.ToArray();

    public static string PublicKeyPath => Path.Combine(AppContext.BaseDirectory, "assets", "feedback", "public.pem");

    public static byte[] Encrypt(byte[] plain, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);

        byte[] compressed;
        using (var packed = new MemoryStream())
        {
            using (var gzip = new GZipStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            {
                gzip.Write(plain);
            }

            compressed = packed.ToArray();
        }

        byte[] key = RandomNumberGenerator.GetBytes(32);
        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] tag = new byte[16];
        byte[] cipher = new byte[compressed.Length];
        using (var aes = new AesGcm(key, tag.Length))
        {
            aes.Encrypt(nonce, compressed, cipher, tag);
        }

        byte[] wrapped = rsa.Encrypt(key, RSAEncryptionPadding.OaepSHA256);
        using var output = new MemoryStream();
        output.Write(Magic);
        output.WriteByte((byte)(wrapped.Length >> 8));
        output.WriteByte((byte)wrapped.Length);
        output.Write(wrapped);
        output.Write(nonce);
        output.Write(tag);
        output.Write(cipher);
        return output.ToArray();
    }
}
