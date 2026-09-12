using System;
using System.Collections.Generic;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;
using Arpeggio.Formats.Export.Nsf;

namespace Arpeggio.Core.Tests.Formats.Export.Nsf
{
    /// <summary>NSF データ命令と容量を独立した固定バイト列で検証する。</summary>
    public sealed class NsfDataEncoderTests
    {
        /// <summary>待機ゼロを作らず、16 bit を超える絶対差を正の WAIT へ分割する。</summary>
        [Theory]
        [InlineData(0, new byte[] { 2 })]
        [InlineData(1, new byte[] { 1, 1, 0, 2 })]
        [InlineData(65535, new byte[] { 1, 255, 255, 2 })]
        [InlineData(65536, new byte[] { 1, 255, 255, 1, 1, 0, 2 })]
        public void WaitAndEndHaveFixedBytes(long endFrame, byte[] expected)
        {
            var timeline = new NsfFrameTimeline(endFrame, Array.Empty<NsfRegisterWrite>());
            var report = CreateReport();
            NsfEncodedData? encoded = NsfDataEncoder.Encode(timeline, report);
            Assert.NotNull(encoded);
            Assert.Equal(expected, encoded.Bytes);
            Assert.Equal(expected.Length, NsfDataEncoder.EstimateDataBytes(timeline));
            Assert.Equal(8320L, encoded.OutputBytes);
            Assert.Equal(0, encoded.MaximumWritesPerPlay);
        }

        /// <summary>先頭無音・同値の副作用・終端停止の順序をバイト単位で保持する。</summary>
        [Fact]
        public void WritesPreserveOrderDuplicatesAndTerminalSilence()
        {
            var writes = new[]
            {
                new NsfRegisterWrite(1, 0x4003, 0), new NsfRegisterWrite(1, 0x4003, 0),
                new NsfRegisterWrite(2, 0x4015, 0)
            };
            var timeline = new NsfFrameTimeline(2, writes);
            writes[0] = new NsfRegisterWrite(1, 0x4000, 255);
            NsfEncodedData? encoded = NsfDataEncoder.Encode(timeline, CreateReport());
            Assert.NotNull(encoded);
            Assert.Equal(new byte[] { 1, 1, 0, 0, 3, 0, 0, 3, 0, 1, 1, 0, 0, 21, 0, 2 }, encoded.Bytes);
            Assert.Equal(2, encoded.MaximumWritesPerPlay);
            Assert.Throws<NotSupportedException>(() => ((IList<byte>)encoded.Bytes)[0] = 255);
            Assert.Throws<NotSupportedException>(() => ((IList<NsfRegisterWrite>)timeline.Writes)[0] = default);
        }

        /// <summary>4 KiB の両側と ROM 上限の見積もりにヘッダーを一度だけ加える。</summary>
        [Theory]
        [InlineData(1, 8320)]
        [InlineData(4096, 8320)]
        [InlineData(4097, 12416)]
        [InlineData(1044480, 1048704)]
        [InlineData(1044481, 1052800)]
        public void SizeEstimateIncludesBankPadding(long dataBytes, long expectedBytes)
        {
            Assert.Equal(expectedBytes, NsfDataEncoder.EstimateOutputBytes(dataBytes));
        }

        /// <summary>3 byte 命令と END で到達する容量境界の直前を受理し、直後を確保前に拒否する。</summary>
        [Theory]
        [InlineData(348159, 1044478, true)]
        [InlineData(348160, 1044481, false)]
        public void DataCapacityIsCheckedBeforeEncoding(int writeCount, long dataBytes, bool accepted)
        {
            var writes = Enumerable.Repeat(new NsfRegisterWrite(0, 0x4015, 0), writeCount).ToArray();
            var timeline = new NsfFrameTimeline(0, writes);
            var report = CreateReport();
            Assert.Equal(dataBytes, NsfDataEncoder.EstimateDataBytes(timeline));
            NsfEncodedData? encoded = NsfDataEncoder.Encode(timeline, report);
            Assert.Equal(accepted, encoded is not null);
            if (!accepted)
            {
                Assert.Contains(report.Errors, diagnostic => diagnostic.Code == "NsfDataLimitExceeded");
            }
        }

        /// <summary>穴・DMC DMA 関連・範囲外のアドレスを拒否する。</summary>
        [Theory]
        [InlineData(0x3FFF)]
        [InlineData(0x4009)]
        [InlineData(0x400D)]
        [InlineData(0x4012)]
        [InlineData(0x4013)]
        [InlineData(0x4014)]
        [InlineData(0x4016)]
        [InlineData(0x4018)]
        public void InvalidRegistersAreRejected(int address)
        {
            var timeline = new NsfFrameTimeline(0, new[] { new NsfRegisterWrite(0, (ushort)address, 0) });
            var report = CreateReport();
            Assert.Null(NsfDataEncoder.Encode(timeline, report));
            Assert.Equal("InvalidNsfData", Assert.Single(report.Errors).Code);
        }

        /// <summary>時刻逆転・終端越え・負時刻と先行エラーで部分列を公開しない。</summary>
        [Theory]
        [InlineData(-1, 0)]
        [InlineData(2, 1)]
        public void InvalidTimesAreRejected(long frame, long endFrame)
        {
            var timeline = new NsfFrameTimeline(endFrame, new[] { new NsfRegisterWrite(frame, 0x4015, 0) });
            Assert.Null(NsfDataEncoder.Encode(timeline, CreateReport()));
        }

        /// <summary>順序逆転と strict 警告も保存不可となる。</summary>
        [Fact]
        public void ReversedFramesAndStrictWarningsAreRejected()
        {
            var reversed = new NsfFrameTimeline(2, new[] { new NsfRegisterWrite(2, 0x4015, 0), new NsfRegisterWrite(1, 0x4015, 0) });
            Assert.Null(NsfDataEncoder.Encode(reversed, CreateReport()));
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, strict: true, diagnosticDetailLimit: 0);
            report.AddWarning(new ConversionDiagnostic("NsfTimingQuantized", "検証用の量子化警告。"));
            Assert.Null(NsfDataEncoder.Encode(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()), report));
        }

        private static ConversionReport CreateReport() => new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes);
    }
}
