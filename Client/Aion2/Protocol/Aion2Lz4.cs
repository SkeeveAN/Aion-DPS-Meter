namespace AionDPS.Aion2.Protocol;

/// <summary>
/// Minimal LZ4 block decompressor. Aion 2 wraps many small frames into one "bundle" frame whose
/// body is an LZ4 block (verified on a real capture: every bundle decompressed and split into
/// complete frames). Written out here rather than pulled in as a dependency - the format is a
/// dozen lines - and strict: any offset or length that would leave the output bounds fails the
/// whole block instead of producing half a bundle.
/// </summary>
public static class Aion2Lz4
{
    public const int MaxOutput = 1 << 20;

    public static bool TryDecompress(ReadOnlySpan<byte> source, int expectedSize, out byte[] result)
    {
        result = Array.Empty<byte>();
        if (expectedSize <= 0 || expectedSize > MaxOutput)
        {
            return false;
        }

        var output = new byte[expectedSize];
        int written = 0;
        int i = 0;
        while (i < source.Length)
        {
            int token = source[i++];
            int literal = token >> 4;
            if (literal == 15)
            {
                int extra;
                do
                {
                    if (i >= source.Length)
                    {
                        return false;
                    }

                    extra = source[i++];
                    literal += extra;
                }
                while (extra == 255);
            }

            if (literal > source.Length - i || literal > expectedSize - written)
            {
                return false;
            }

            source.Slice(i, literal).CopyTo(output.AsSpan(written));
            i += literal;
            written += literal;
            if (i >= source.Length)
            {
                break; // the last sequence carries literals only
            }

            if (i + 2 > source.Length)
            {
                return false;
            }

            int offset = source[i] | source[i + 1] << 8;
            i += 2;
            int length = token & 15;
            if (length == 15)
            {
                int extra;
                do
                {
                    if (i >= source.Length)
                    {
                        return false;
                    }

                    extra = source[i++];
                    length += extra;
                }
                while (extra == 255);
            }

            length += 4;
            if (offset == 0 || offset > written || length > expectedSize - written)
            {
                return false;
            }

            for (int k = 0; k < length; k++)
            {
                output[written] = output[written - offset];
                written++;
            }
        }

        if (written != expectedSize)
        {
            return false;
        }

        result = output;
        return true;
    }
}
