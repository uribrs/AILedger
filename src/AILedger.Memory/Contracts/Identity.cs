using System.Security.Cryptography;
using System.Text;
using System.Buffers.Binary;

namespace AILedger.Memory.Contracts;

public static class MemoryIdentity
{
    public const string NormalizerVersion = "1";

    public static string CreateDocumentId(
        string sourceIdentity,
        MemoryDocumentKind kind,
        string recordIdentity)
        => HashParts("document-v1", sourceIdentity, kind.ToString(), recordIdentity);

    public static string CreateContentHash(string normalizedText)
        => HashParts("content-v1", NormalizerVersion, normalizedText);

    public static string HashParts(params string[] parts)
    {
        using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];

        foreach (var part in parts)
        {
            var value = Encoding.UTF8.GetBytes(part);
            BinaryPrimitives.WriteInt32BigEndian(length, value.Length);
            incrementalHash.AppendData(length);
            incrementalHash.AppendData(value);
        }

        return Convert.ToHexString(incrementalHash.GetHashAndReset()).ToLowerInvariant();
    }
}
