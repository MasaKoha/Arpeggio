using Arpeggio.Core.Document;

namespace Arpeggio.Core.Brief
{
    /// <summary>作曲指示の必須項目・文字数・指定値を検証する。</summary>
    public static class CompositionBriefValidator
    {
        /// <summary>曲名の UTF-16 コード単位での最大長。</summary>
        public const int MaximumTitleLength = 200;

        /// <summary>自由記述一項目の UTF-16 コード単位での最大長。</summary>
        public const int MaximumTextLength = 2000;

        /// <summary>指定できる最小テンポ。</summary>
        public const int MinimumTempoBpm = 1;

        /// <summary>不正な入力を修正位置付きの例外で拒否する。</summary>
        public static void Validate(CompositionBrief brief)
        {
            // JSON からは NRT の宣言に反して null が入るため、必須項目を実値で検証する。
            if (string.IsNullOrWhiteSpace(brief.Title))
            {
                throw new CompositionBriefException("InvalidParameter", "title", "曲名は空白にできません。");
            }
            ValidateLength(brief.Title, MaximumTitleLength, "title");
            if (brief.Chip.HasValue && brief.Chip != ChipKind.Nes && brief.Chip != ChipKind.GameBoy && brief.Chip != ChipKind.Snes)
            {
                throw new CompositionBriefException("InvalidParameter", "chip", "チップは Nes・GameBoy・Snes または null で指定してください。");
            }
            if (brief.TempoBpm.HasValue && brief.TempoBpm.Value < MinimumTempoBpm)
            {
                throw new CompositionBriefException("InvalidParameter", "tempoBpm", "テンポは正の整数で指定してください。");
            }
            ValidateLength(brief.Mood, MaximumTextLength, "mood");
            ValidateLength(brief.Structure, MaximumTextLength, "structure");
            ValidateLength(brief.Instrumentation, MaximumTextLength, "instrumentation");
            ValidateLength(brief.References, MaximumTextLength, "references");
            ValidateLength(brief.Constraints, MaximumTextLength, "constraints");
            ValidateLength(brief.Notes, MaximumTextLength, "notes");
        }

        private static void ValidateLength(string? value, int maximumLength, string path)
        {
            if (value != null && value.Length > maximumLength)
            {
                throw new CompositionBriefException("InvalidParameter", path, $"{path} は {maximumLength} 文字以内で指定してください。");
            }
        }
    }
}
