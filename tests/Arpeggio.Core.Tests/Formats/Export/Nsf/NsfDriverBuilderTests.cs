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
    /// <summary>生成機械語の opcode・オペランド・ラベル・静的上限を CPU 実行から独立して検証する。</summary>
    public sealed class NsfDriverBuilderTests
    {
        private const int LoadAddress = 0x8000;

        /// <summary>限定 opcode の実バイトを独立した長さ表で走査し、固定 bank とゼロ埋めを検証する。</summary>
        [Fact]
        public void GeneratedInstructionsUseOnlyDocumentedOpcodesInsideFixedBank()
        {
            NsfDriverImage image = BuildImage();
            var expectedInstructions = new Dictionary<byte, (int Length, int Cycles)>
            {
                [0x05] = (2, 3), [0x18] = (1, 2), [0x20] = (3, 6), [0x38] = (1, 2),
                [0x4C] = (3, 3), [0x60] = (1, 6), [0x85] = (2, 3), [0x8D] = (3, 4),
                [0x90] = (2, 4), [0x9D] = (3, 5), [0xA0] = (2, 2), [0xA5] = (2, 3),
                [0xA9] = (2, 2), [0xAA] = (1, 2), [0xB1] = (2, 6), [0xBD] = (3, 5),
                [0xC6] = (2, 5), [0xC9] = (2, 2), [0xD0] = (2, 4), [0xE6] = (2, 5), [0xF0] = (2, 4)
            };
            Assert.Equal(4096, image.Bank.Count);
            Assert.Equal(290, image.CodeLength);
            Assert.Equal(0x8000, image.InitAddress);
            Assert.Equal(0x8064, image.PlayAddress);
            int cursor = 0;
            foreach (NsfDriverInstruction instruction in image.Instructions)
            {
                byte opcode = image.Bank[cursor];
                Assert.True(expectedInstructions.TryGetValue(opcode, out var expected), $"未許可 opcode: {opcode:X2}");
                int length = expected.Length;
                Assert.Equal(expected.Cycles, instruction.MaximumCycles);
                Assert.Equal(LoadAddress + cursor, instruction.Address);
                Assert.Equal(opcode, (byte)instruction.Opcode);
                Assert.Equal(length, instruction.Length);
                if (length >= 2)
                {
                    Assert.Equal((byte)instruction.Operand, image.Bank[cursor + 1]);
                }
                if (length == 3)
                {
                    Assert.Equal((byte)(instruction.Operand >> 8), image.Bank[cursor + 2]);
                }
                cursor += length;
            }
            Assert.Equal(image.Labels["AllowedRegisters"] - LoadAddress, cursor);
            Assert.All(image.Bank.Skip(image.CodeLength), value => Assert.Equal((byte)0, value));
        }

        /// <summary>相対分岐と絶対参照が実ラベルへ到達し、コード参照先は命令境界となる。</summary>
        [Fact]
        public void ResolvedReferencesReachLabelsAndInstructionBoundaries()
        {
            NsfDriverImage image = BuildImage();
            var addresses = image.Instructions.Select(instruction => instruction.Address).ToHashSet();
            foreach (NsfDriverInstruction instruction in image.Instructions.Where(instruction => instruction.TargetLabel is not null))
            {
                int destination = instruction.Operand;
                if ((byte)instruction.Opcode is 0x90 or 0xB0 or 0xD0 or 0xF0)
                {
                    destination = instruction.Address + 2 + unchecked((sbyte)instruction.Operand);
                }
                Assert.Equal(image.Labels[instruction.TargetLabel!], destination);
                Assert.InRange(destination, 0x8000, 0x8FFF);
                if (instruction.TargetLabel != "AllowedRegisters")
                {
                    Assert.Contains((ushort)destination, addresses);
                }
            }
            Assert.Throws<NotSupportedException>(() => ((IDictionary<string, ushort>)image.Labels).Add("Injected", 0));
            Assert.Throws<NotSupportedException>(() => ((IList<byte>)image.Bank)[0] = 0);
        }

        /// <summary>再 INIT で全作業 RAM・待機・終端・カーソルと bank 1 を明示的に復元する。</summary>
        [Fact]
        public void InitRestoresRamCursorBankAndApuWithoutChangingStackPointer()
        {
            NsfDriverImage image = BuildImage();
            var expected = new List<byte> { 0xA9, 0x00 };
            for (byte address = 0; address < 0x20; address++)
            {
                expected.Add(0x85);
                expected.Add(address);
            }
            expected.AddRange(new byte[]
            {
                0x8D, 0x15, 0x40, 0x8D, 0x10, 0x40, 0x8D, 0x11, 0x40,
                0xA9, 0x08, 0x8D, 0x01, 0x40, 0x8D, 0x05, 0x40,
                0xA9, 0xC0, 0x8D, 0x17, 0x40,
                0xA9, 0x90, 0x85, 0x01, 0xA9, 0x01, 0x85, 0x02, 0x8D, 0xF9, 0x5F, 0x60
            });
            Assert.Equal(expected, Between(image, "Init", "Play"));
            Assert.DoesNotContain(image.Instructions, instruction => (byte)instruction.Opcode == 0x9A);
        }

        /// <summary>WAIT の借り下がりとゼロ到達を比較し、格納後は読み進めず RTS へ戻る。</summary>
        [Fact]
        public void WaitUsesSixteenBitCountdownAndReturnsAfterLoading()
        {
            NsfDriverImage image = BuildImage();
            Assert.Equal(new byte[]
            {
                0xA5, 0x05, 0xF0, 0x03, 0x4C, 0xE4, 0x80,
                0xA5, 0x03, 0x05, 0x04, 0xF0, 0x11,
                0xA5, 0x03, 0xD0, 0x02, 0xC6, 0x04,
                0xC6, 0x03, 0xA5, 0x03, 0x05, 0x04, 0xF0, 0x03, 0x4C, 0xE4, 0x80
            }, Between(image, "Play", "NextCommand"));
            Assert.Equal(new byte[]
            {
                0x20, 0xE5, 0x80, 0x90, 0x03, 0x4C, 0xDB, 0x80, 0x85, 0x03,
                0x20, 0xE5, 0x80, 0x90, 0x03, 0x4C, 0xDB, 0x80, 0x85, 0x04,
                0x05, 0x03, 0xD0, 0x03, 0x4C, 0xDB, 0x80, 0x60
            }, Between(image, "WaitCommand", "Fail"));
        }

        /// <summary>bank 255 判定を読み出し前だけに置き、最終 byte の END を先行切り替えで失わない。</summary>
        [Fact]
        public void ByteReaderDefersBankAdvanceAndPreservesAccumulatorAndX()
        {
            NsfDriverImage image = BuildImage();
            Assert.Equal(new byte[]
            {
                0xA5, 0x01, 0xC9, 0xA0, 0xD0, 0x11,
                0xA5, 0x02, 0xC9, 0xFF, 0xF0, 0x17,
                0xE6, 0x02, 0xA5, 0x02, 0x8D, 0xF9, 0x5F, 0xA9, 0x90, 0x85, 0x01,
                0xA0, 0x00, 0xB1, 0x00, 0xE6, 0x00, 0xD0, 0x02, 0xE6, 0x01,
                0x18, 0x60, 0x38, 0x60
            }, Between(image, "ReadByte", "AllowedRegisters"));
            Assert.Equal(2, image.Instructions.Count(instruction => (byte)instruction.Opcode == 0x8D && instruction.Operand == 0x5FF9));
            Assert.DoesNotContain(image.Instructions, instruction => (byte)instruction.Opcode == 0x8D && instruction.Operand is >= 0x5FF8 and <= 0x5FFF && instruction.Operand != 0x5FF9);
        }

        /// <summary>END は終端状態を保持し、不正命令・アドレス・bank 越えは APU を停止して復帰する。</summary>
        [Fact]
        public void EndAndDefensiveStopHaveFixedInstructionsAndWhitelist()
        {
            NsfDriverImage image = BuildImage();
            Assert.Equal(new byte[] { 0xA9, 0, 0x8D, 0x15, 0x40, 0xA9, 1, 0x85, 5, 0x60 }, Between(image, "Fail", "ReadByte"));
            Assert.Equal(new byte[]
            {
                1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 1, 1, 1, 0, 1, 1,
                1, 1, 0, 0, 0, 1, 0, 1
            }, image.Bank.Skip(image.Labels["AllowedRegisters"] - LoadAddress).Take(24));
            Assert.Equal(new byte[]
            {
                0x20, 0xE5, 0x80, 0x90, 3, 0x4C, 0xDB, 0x80,
                0xC9, 0x18, 0x90, 3, 0x4C, 0xDB, 0x80, 0xAA,
                0xBD, 0x0A, 0x81, 0xD0, 3, 0x4C, 0xDB, 0x80,
                0x20, 0xE5, 0x80, 0x90, 3, 0x4C, 0xDB, 0x80,
                0x9D, 0, 0x40, 0x4C, 0x82, 0x80
            }, Between(image, "WriteCommand", "WaitCommand"));
        }

        /// <summary>最悪分岐を足した固定上限を提示し、29 書き込みを受理、30 書き込みを拒否する。</summary>
        [Theory]
        [InlineData(0, 323, true)]
        [InlineData(23, 6234, true)]
        [InlineData(29, 7776, true)]
        [InlineData(30, 8033, false)]
        public void StaticCycleBudgetHasFixedBoundary(int writeCount, long maximumPlayCycles, bool accepted)
        {
            var report = CreateReport();
            NsfEncodedData data = EncodeWrites(writeCount);
            NsfDriverImage? image = NsfDriverBuilder.Build(data, report);
            Assert.Equal(accepted, image is not null);
            Assert.Equal(146L, report.Statistics["maximumInitCycles"]);
            Assert.Equal(maximumPlayCycles, report.Statistics["maximumPlayCycles"]);
            Assert.Equal(53L, report.Statistics["nsfPlayEntryCycles"]);
            Assert.Equal(257L, report.Statistics["nsfWriteCommandCycles"]);
            Assert.Equal(270L, report.Statistics["nsfTerminalCommandCycles"]);
            if (image is not null)
            {
                Assert.Equal(maximumPlayCycles, image.MaximumPlayCycles);
                Assert.Equal(146L, image.MaximumInitCycles);
            }
            else
            {
                Assert.Equal("NsfCpuBudgetExceeded", Assert.Single(report.Errors).Code);
            }
        }

        /// <summary>再生成してもラベルと機械語が変わらず、strict の全警告数も尊重する。</summary>
        [Fact]
        public void GenerationIsDeterministicAndHonorsStrict()
        {
            NsfDriverImage first = BuildImage();
            NsfDriverImage second = BuildImage();
            Assert.Equal(first.Bank, second.Bank);
            Assert.Equal(first.Instructions, second.Instructions);
            var report = new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes, strict: true, diagnosticDetailLimit: 0);
            report.AddWarning(new ConversionDiagnostic("NsfTimingQuantized", "検証用の警告。"));
            Assert.Null(NsfDriverBuilder.Build(EncodeWrites(0), report));
        }

        private static byte[] Between(NsfDriverImage image, string firstLabel, string nextLabel)
            => image.Bank.Skip(image.Labels[firstLabel] - LoadAddress).Take(image.Labels[nextLabel] - image.Labels[firstLabel]).ToArray();

        private static NsfEncodedData EncodeWrites(int writeCount)
        {
            var writes = Enumerable.Repeat(new NsfRegisterWrite(0, 0x4015, 0), writeCount).ToArray();
            NsfEncodedData? data = NsfDataEncoder.Encode(new NsfFrameTimeline(0, writes), CreateReport());
            Assert.NotNull(data);
            return data;
        }

        private static NsfDriverImage BuildImage()
        {
            NsfDriverImage? image = NsfDriverBuilder.Build(EncodeWrites(0), CreateReport());
            Assert.NotNull(image);
            return image;
        }

        private static ConversionReport CreateReport() => new ConversionReport(ConversionFormat.Nsf, ChipKind.Nes);
    }
}
