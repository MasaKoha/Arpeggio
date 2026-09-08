using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Formats;
using Arpeggio.Formats.Export;
using Xunit;

namespace Arpeggio.Core.Tests.Formats
{
    /// <summary>生成物を独立 CPU へ渡す接続だけを担当し、CPU 内部には生成側の規則を持ち込まない。</summary>
    internal static class NsfExecutionFixture
    {
        internal static ConversionReport CreateReport() => new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes);

        internal static (NsfEncodedData Data, NsfDriverImage Image) Build(NsfFrameTimeline timeline)
        {
            ConversionReport report = CreateReport();
            NsfEncodedData? data = NsfDataEncoder.Encode(timeline, report);
            Assert.NotNull(data);
            NsfDriverImage? image = NsfDriverBuilder.Build(data, report);
            Assert.NotNull(image);
            return (data, image);
        }

        internal static Limited6502Memory Map(NsfDriverImage image, byte[] data)
        {
            const int bankBytes = 4096;
            int dataBanks = (data.Length + bankBytes - 1) / bankBytes;
            var rom = new byte[(1 + dataBanks) * bankBytes];
            image.Bank.ToArray().CopyTo(rom, 0);
            data.CopyTo(rom, bankBytes);
            var memory = new Limited6502Memory();
            memory.MapRom(rom, new byte[] { 0, 1, 0, 0, 0, 0, 0, 0 });
            return memory;
        }

        internal static void AssertPlayback(Limited6502 processor, Limited6502Memory memory,
            ushort playAddress, NsfFrameTimeline timeline, long maximumCycles)
        {
            memory.Writes.Clear();
            for (long frame = 0; frame <= timeline.EndFrame + 2; frame++)
            {
                memory.Frame = frame;
                long cycles = processor.Call(playAddress, 8000);
                Assert.InRange(cycles, 1, maximumCycles);
            }
            Assert.Equal(timeline.Writes.Select(write => (write.Frame, write.Address, write.Value)),
                memory.Writes.Where(write => write.Address is >= 0x4000 and <= 0x4017)
                    .Select(write => (write.Frame, write.Address, write.Value)));
            Assert.All(memory.Writes, write => Assert.True(
                write.Address <= 0x1F || write.Address is >= 0x100 and <= 0x1FF ||
                write.Address is >= 0x4000 and <= 0x4017 || write.Address == 0x5FF9,
                $"予約領域外への書き込み: ${write.Address:X4}"));
        }
    }
}
