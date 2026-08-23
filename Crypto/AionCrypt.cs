using System.Text;

namespace AionSniffer.Crypto;

/// <summary>
/// Reimplementation of the Aion game-server &lt;-&gt; client stream cipher, derived from the
/// Aion-Lightning open-source emulator (client protocol ~4.5/4.6). The emulator's "encrypt"
/// (server -> client) routine is a self-synchronizing XOR stream cipher: each byte is XORed
/// with a static 64-byte table, a rotating 8-byte per-session key, and the *previous ciphertext
/// byte* (ciphertext feedback). That makes it fully invertible from captured traffic alone,
/// given only the session's initial key (delivered in cleartext in the very first server packet,
/// SM_KEY, obfuscated by <see cref="DecodeBaseKey"/>).
///
/// IMPORTANT: the constants below (StaticKey, StaticServerPacketCode, the SM_KEY obfuscation
/// formula, EncodeOpcode) are only confirmed correct for the Aion-Lightning 4.5-era build.
/// They must be verified against a real capture from the target server (OriginAion, client 4.6)
/// before trusting decoded opcodes/values -- see CalibrationMode in Program.cs.
/// </summary>
public static class AionCrypt
{
    /// <summary>64-byte XOR table mixed into every byte position (index &amp; 63).</summary>
    public static readonly byte[] StaticKey = Encoding.ASCII.GetBytes(
        "nKO/WctQ0AVLbpzfBkS6NevDYT8ourG5CRlmdjyJ72aswx4EPq1UgZhFMXH?3iI9");

    /// <summary>Expected value of byte[2] of every server packet payload (post length-prefix).</summary>
    public const byte StaticServerPacketCode = 0x43;

    /// <summary>Fixed suffix appended to the 4-byte base key to form the 8-byte rotating key.</summary>
    public static readonly byte[] KeySuffix = { 0xa1, 0x6c, 0x54, 0x87 };

    /// <summary>
    /// The SM_KEY packet body carries "falseKey", not the real base key, obfuscated as:
    /// falseKey = (baseKey ^ 0xCD92E4DD) + 0x3FF2CCCF   (32-bit wraparound arithmetic).
    /// Inverted here.
    /// </summary>
    public static int DecodeBaseKey(int falseKey)
    {
        unchecked
        {
            return (falseKey - 0x3FF2CCCF) ^ (int)0xCD92E4DD;
        }
    }

    /// <summary>
    /// Opcodes on the wire are obfuscated as obfuscated = (real + 0xCC) ^ 0xDD (16-bit). Inverted here.
    /// </summary>
    public static ushort DecodeOpcode(ushort obfuscated)
    {
        unchecked
        {
            return (ushort)(((obfuscated ^ 0xDD) - 0xCC) & 0xFFFF);
        }
    }

    /// <summary>
    /// Per-connection rotating decryption state for the SERVER -> CLIENT direction.
    /// Mirrors the emulator's EncryptionKeyPair, but only the decrypt path we need as a
    /// passive observer (we are inverting what the real server's "encrypt" produced).
    /// </summary>
    public sealed class ServerStream
    {
        private readonly byte[] _key = new byte[8];

        public ServerStream(int baseKey)
        {
            _key[0] = (byte)(baseKey & 0xff);
            _key[1] = (byte)((baseKey >> 8) & 0xff);
            _key[2] = (byte)((baseKey >> 16) & 0xff);
            _key[3] = (byte)((baseKey >> 24) & 0xff);
            KeySuffix.CopyTo(_key, 4);
        }

        /// <summary>
        /// Decrypts <paramref name="size"/> bytes starting at <paramref name="offset"/> in place,
        /// then rotates the key exactly as the real server does after producing that ciphertext.
        /// </summary>
        public void Decrypt(byte[] data, int offset, int size)
        {
            if (size <= 0)
            {
                return;
            }

            byte prevCipher = data[offset];
            data[offset] ^= _key[0];

            for (int i = 1; i < size; i++)
            {
                byte cipher = data[offset + i];
                data[offset + i] ^= (byte)(StaticKey[i & 63] ^ _key[i & 7] ^ prevCipher);
                prevCipher = cipher;
            }

            // key state evolves by simple addition of the packet size, as a little-endian 64-bit value
            long keyAsLong = 0;
            for (int i = 0; i < 8; i++)
            {
                keyAsLong |= (long)_key[i] << (8 * i);
            }

            keyAsLong += size;

            for (int i = 0; i < 8; i++)
            {
                _key[i] = (byte)((keyAsLong >> (8 * i)) & 0xff);
            }
        }
    }
}
