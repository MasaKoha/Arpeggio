using System;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Xunit;

namespace Arpeggio.Core.Tests.Brief
{
    /// <summary>必須値・文字数・テンポとチップの境界を固定する。</summary>
    public sealed class CompositionBriefValidatorTests
    {
        /// <summary>タイトルの未指定・空白と上限超過を拒否する。</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" \t\n")]
        public void EmptyTitleIsRejected(string? title)
        {
            AssertInvalid(new CompositionBrief { Title = title! }, "title");
        }

        /// <summary>タイトルは上限ちょうどまで許容する。</summary>
        [Fact]
        public void TitleLengthBoundaryIsEnforced()
        {
            var brief = new CompositionBrief { Title = new string('曲', CompositionBriefValidator.MaximumTitleLength) };
            CompositionBriefValidator.Validate(brief);
            AssertInvalid(brief with { Title = brief.Title + "曲" }, "title");
        }

        /// <summary>全自由記述で未指定・空文字・上限と超過を区別する。</summary>
        [Theory]
        [InlineData("mood")]
        [InlineData("structure")]
        [InlineData("instrumentation")]
        [InlineData("references")]
        [InlineData("constraints")]
        [InlineData("notes")]
        public void EveryTextFieldEnforcesItsLengthBoundary(string field)
        {
            var brief = new CompositionBrief { Title = "曲" };
            CompositionBriefValidator.Validate(WithText(brief, field, null));
            CompositionBriefValidator.Validate(WithText(brief, field, string.Empty));
            CompositionBriefValidator.Validate(WithText(brief, field, new string('あ', CompositionBriefValidator.MaximumTextLength)));
            AssertInvalid(WithText(brief, field, new string('あ', CompositionBriefValidator.MaximumTextLength + 1)), field);
        }

        /// <summary>非正数のテンポ、None と未知のチップを拒否する。</summary>
        [Fact]
        public void InvalidTempoAndChipAreRejected()
        {
            var brief = new CompositionBrief { Title = "曲" };
            AssertInvalid(brief with { TempoBpm = 0 }, "tempoBpm");
            AssertInvalid(brief with { TempoBpm = -1 }, "tempoBpm");
            AssertInvalid(brief with { Chip = ChipKind.None }, "chip");
            AssertInvalid(brief with { Chip = (ChipKind)99 }, "chip");
            CompositionBriefValidator.Validate(brief);
            CompositionBriefValidator.Validate(brief with { TempoBpm = CompositionBriefValidator.MinimumTempoBpm, Chip = ChipKind.Nes });
            CompositionBriefValidator.Validate(brief with { TempoBpm = int.MaxValue, Chip = ChipKind.GameBoy });
            CompositionBriefValidator.Validate(brief with { Chip = ChipKind.Snes });
        }

        private static CompositionBrief WithText(CompositionBrief brief, string field, string? value)
        {
            return field switch
            {
                "mood" => brief with { Mood = value },
                "structure" => brief with { Structure = value },
                "instrumentation" => brief with { Instrumentation = value },
                "references" => brief with { References = value },
                "constraints" => brief with { Constraints = value },
                "notes" => brief with { Notes = value },
                _ => throw new ArgumentException("未知の検証項目です。", nameof(field))
            };
        }

        private static void AssertInvalid(CompositionBrief brief, string path)
        {
            CompositionBriefException exception = Assert.Throws<CompositionBriefException>(() => CompositionBriefValidator.Validate(brief));
            Assert.Equal("InvalidParameter", exception.Code);
            Assert.Equal(path, exception.ParameterPath);
        }
    }
}
