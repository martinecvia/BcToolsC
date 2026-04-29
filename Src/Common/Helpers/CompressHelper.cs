using System; // Keep for .NET 4.6
using System.IO;
using System.IO.Compression;

namespace BcToolsC.Helpers
{
    public static class CompressHelper
    {
        internal static double[,] DeserializeFromBase64(string base64)
        {
            byte[] data = Convert.FromBase64String(base64);
            return DecompressAndDeserialize(data);
        }

        static double[,] DecompressAndDeserialize(byte[] data)
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
    }
}