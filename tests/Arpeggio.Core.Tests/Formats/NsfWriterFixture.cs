using System;
using System.IO;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>NSF を実ファイルに保存して閉じた後、独立ローダーで再読込する接続を共用する。</summary>
    internal static class NsfWriterFixture
    {
        internal static (byte[] Bytes, IndependentNsfLoader File, ConversionReport Report) Save(NsfFrameTimeline timeline,
            string title = "", string author = "", string copyright = "")
        {
            var report = NsfExecutionFixture.CreateReport();
            NsfEncodedData? data = NsfDataEncoder.Encode(timeline, report);
            Assert.NotNull(data);
            return Save(data, report, title, author, copyright);
        }

        internal static (byte[] Bytes, IndependentNsfLoader File, ConversionReport Report) Save(NsfEncodedData data,
            ConversionReport report, string title = "", string author = "", string copyright = "")
        {
            string path = Path.Combine(Directory.GetCurrentDirectory(), "nsf-test-" + Guid.NewGuid().ToString("N") + ".nsf");
            var destination = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            try
            {
                using (destination)
                {
                    Assert.True(NsfWriter.Write(destination, data, title, report, author, copyright));
                    Assert.True(destination.CanWrite);
                }
                return (File.ReadAllBytes(path), IndependentNsfLoader.Load(path), report);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
}
