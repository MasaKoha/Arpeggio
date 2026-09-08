using System;
using System.IO;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>writer の CPU 予算拒否・Stream 所有権・事前検証・I/O 例外の契約を検証する。</summary>
    public sealed class NsfWriterContractTests
    {
        /// <summary>保存経路自身が 29 書き込みを受理、30 書き込みを CPU 予算で無変更拒否する。</summary>
        [Theory]
        [InlineData(29, 7776, true)]
        [InlineData(30, 8033, false)]
        public void WriterEnforcesCpuBudgetBeforeTouchingStream(int writeCount, long cycles, bool accepted)
        {
            var timeline = new NsfFrameTimeline(0, Enumerable.Repeat(new NsfRegisterWrite(0, 0x4015, 0), writeCount).ToArray());
            ConversionReport report = NsfExecutionFixture.CreateReport();
            NsfEncodedData? data = NsfDataEncoder.Encode(timeline, report);
            Assert.NotNull(data);
            using var destination = new MemoryStream();
            destination.WriteByte(0x42);
            Assert.Equal(accepted, NsfWriter.Write(destination, data, "", report));
            Assert.Equal(cycles, report.Statistics["maximumPlayCycles"]);
            Assert.Equal(146L, report.Statistics["maximumInitCycles"]);
            if (!accepted)
            {
                Assert.Equal("NsfCpuBudgetExceeded", Assert.Single(report.Errors).Code);
                Assert.Equal(new byte[] { 0x42 }, destination.ToArray());
                Assert.Equal(1L, destination.Position);
                return;
            }
            Assert.Equal((byte)0x42, destination.ToArray()[0]);
            IndependentNsfLoader file = IndependentNsfLoader.Load(destination.ToArray().Skip(1).ToArray());
            var processor = new Limited6502(file.Memory);
            Assert.InRange(processor.Call(file.InitAddress, 20000), 1, report.Statistics["maximumInitCycles"]);
            NsfExecutionFixture.AssertPlayback(processor, file.Memory, file.PlayAddress, timeline, cycles);
            Assert.True(destination.CanWrite);
        }

        /// <summary>先行エラー・明細保持ゼロの strict 警告では保存しないが、制限だけなら許可する。</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ExistingDiagnosticsPreventWritesEvenWithoutDetails(bool strict)
        {
            var (data, _) = NsfExecutionFixture.Build(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()));
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, strict, diagnosticDetailLimit: 0);
            if (strict)
            {
                report.AddWarning(new ConversionDiagnostic("NsfTimingQuantized", "検証用の警告。"));
            }
            else
            {
                report.AddError(new ConversionDiagnostic("InvalidInput", "検証用のエラー。"));
            }
            using var destination = new VgmTestStream();
            Assert.False(NsfWriter.Write(destination, data, "", report));
            Assert.Empty(destination.GetWrittenBytes());
            var limitationsOnly = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, strict: true);
            limitationsOnly.AddLimitation("有限演奏のみ。");
            Assert.True(NsfWriter.Write(destination, data, "", limitationsOnly));
            Assert.Equal(8320, destination.GetWrittenBytes().Length);
            Assert.True(destination.CanWrite);
        }

        /// <summary>header・固定 bank・データ bank 途中の I/O 失敗を伝播し、呼び出し元の Stream を閉じない。</summary>
        [Theory]
        [InlineData(0)]
        [InlineData(127)]
        [InlineData(128)]
        [InlineData(4223)]
        [InlineData(4225)]
        [InlineData(8319)]
        public void PartialIoFailurePropagatesWithoutClosingStream(long failureAfterBytes)
        {
            var (data, _) = NsfExecutionFixture.Build(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()));
            using var destination = new VgmTestStream(failureAfterBytes);
            ConversionReport report = NsfExecutionFixture.CreateReport();
            Assert.Throws<IOException>(() => NsfWriter.Write(destination, data, "", report));
            Assert.Equal(failureAfterBytes, destination.GetWrittenBytes().LongLength);
            Assert.True(destination.CanWrite);
            Assert.Equal(8320L, report.OutputBytes);
        }

        /// <summary>形式・チップ不一致と書き込み不可を引数エラーとし、保存しない。</summary>
        [Fact]
        public void IncompatibleReportsAndReadOnlyStreamAreRejected()
        {
            var (data, _) = NsfExecutionFixture.Build(new NsfFrameTimeline(0, Array.Empty<NsfRegisterWrite>()));
            using var destination = new MemoryStream();
            Assert.Throws<ArgumentException>(() => NsfWriter.Write(destination, data, "", new ConversionReport(ConversionFormat.Vgm, ChipKind.Nes)));
            Assert.Throws<ArgumentException>(() => NsfWriter.Write(destination, data, "", new ConversionReport(ConversionFormat.Nsf, ChipKind.GameBoy)));
            Assert.Empty(destination.ToArray());
            using var readOnly = new MemoryStream(new byte[] { 0x42 }, writable: false);
            Assert.Throws<ArgumentException>(() => NsfWriter.Write(readOnly, data, "", NsfExecutionFixture.CreateReport()));
            Assert.Equal(new byte[] { 0x42 }, readOnly.ToArray());
        }
    }
}
