using System; // Keep for .NET 4.6
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace BcToolsC.Helpers
{
    public static class CompressHelper
    {
        internal static double[,] DeserializeDblFromBase64(string base64)
        {
            byte[] data = Convert.FromBase64String(base64);
            return DecompressDblDeserialize(data);
        }

        internal static byte[] DeserializeExeFromBase64(string checksum, params string[] parts)
        {
            if (string.IsNullOrEmpty(checksum)) throw new ArgumentNullException(nameof(checksum));
            if (parts == null) throw new ArgumentNullException(nameof(parts));
            string base64 = string.Empty;
            for (int i = 0; i < parts.Length; i++) base64 += parts[i];
            if (string.IsNullOrEmpty(base64)) throw new ArgumentException("summary of collection is a empty string!");
            byte[] data = Convert.FromBase64String(base64);
            return DecompressExeDeserialize(checksum, data);
        }

        static double[,] DecompressDblDeserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            {
                using (var gzip = new GZipStream(ms, CompressionMode.Decompress))
                using (var br = new BinaryReader(gzip))
                {
                    int rows = br.ReadInt32();
                    var result = new double[rows, 2];
                    for (int i = 0; i < rows; i++)
                    {
                        result[i, 0] = br.ReadDouble(); // x
                        result[i, 1] = br.ReadDouble(); // y
                    }
                    return result;
                }
            }
        }

        static byte[] DecompressExeDeserialize(string checksum, byte[] data)
        {
            string tmpChecksum;
            using (var sha256 = SHA256.Create())
            tmpChecksum = BitConverter.ToString(sha256.ComputeHash(data)).Replace("-", "");
            if (!string.Equals(checksum, tmpChecksum, StringComparison.OrdinalIgnoreCase))
                new ArgumentException("checksums does not match!");
            return data;
        }
    }
}