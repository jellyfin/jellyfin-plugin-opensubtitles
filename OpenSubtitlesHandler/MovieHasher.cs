using System;
using System.IO;

namespace OpenSubtitlesHandler
{
    public static class MovieHasher
    {
        public static byte[] ComputeMovieHash(Stream input)
        {
            using (input)
            {
                long lhash, streamsize;
                streamsize = input.Length;
                lhash = streamsize;

                long i = 0;
                byte[] buffer = new byte[sizeof(long)];
                while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
                {
                    i++;
                    lhash += BitConverter.ToInt64(buffer, 0);
                }

                input.Position = Math.Max(0, streamsize - 65536);
                i = 0;
                while (i < 65536 / sizeof(long) && (input.Read(buffer, 0, sizeof(long)) > 0))
                {
                    i++;
                    lhash += BitConverter.ToInt64(buffer, 0);
                }
                byte[] result = BitConverter.GetBytes(lhash);
                Array.Reverse(result);
                return result;
            }
        }
    }
}
