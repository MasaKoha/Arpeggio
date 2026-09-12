using System;
using System.ComponentModel;
using System.IO;
using Arpeggio.Core.Brief;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using ModelContextProtocol.Server;

namespace Arpeggio.Mcp.Brief
{
    /// <summary>現在の Song と履歴を維持したまま作曲指示書を操作する MCP ツール。</summary>
    [McpServerToolType]
    public sealed class McpBriefTools
    {
        private readonly EditSession _session;

        /// <summary>既存ツールと直列実行するための共有セッションを受け取る。</summary>
        public McpBriefTools(EditSession session)
        {
            _session = session;
        }

        /// <summary>空テンプレートを新規保存し、保存先と作曲指示書の JSON を返す。</summary>
        [McpServerTool(Name = "brief_create", ReadOnly = false, Destructive = false)]
        [Description("作曲指示書を新規保存する。既存の保存先は拒否する。現在の Song と履歴は変更しない。")]
        public string CreateBrief(
            [Description("保存先 *.brief.json")] string path,
            [Description("曲名。省略時は無題")] string? title = null,
            [Description("nes / gameboy / snes。省略時は AI に一任")] string? chip = null)
        {
            return McpBriefExecution.Run(_session, "create", () =>
            {
                var brief = new CompositionBrief
                {
                    Title = title ?? CompositionBrief.DefaultTitle,
                    Chip = chip == null ? null : ParseChip(chip)
                };
                CompositionBriefFile.Create(brief, path);
                return Saved("create", path, brief);
            });
        }

        /// <summary>省略項目を保持して部分編集し、検証成功時だけ一度保存する。</summary>
        [McpServerTool(Name = "brief_tweak", ReadOnly = false, Destructive = true)]
        [Description("作曲指示書の指定項目をまとめて編集する。省略または null は保持、自由記述の空文字は解除。chip / tempo は clearChip / clearTempo で解除し、値との併用は拒否する。")]
        public string TweakBrief(
            [Description("編集する *.brief.json")] string path,
            [Description("曲名。空白は不可")] string? title = null,
            [Description("nes / gameboy / snes")] string? chip = null,
            [Description("正の整数の目安テンポ")] int? tempo = null,
            [Description("雰囲気・ジャンル。2000文字以内")] string? mood = null,
            [Description("構成メモ。2000文字以内")] string? structure = null,
            [Description("声の役割。2000文字以内")] string? instrumentation = null,
            [Description("参考曲・スタイル。2000文字以内")] string? references = null,
            [Description("制約。2000文字以内")] string? constraints = null,
            [Description("自由メモ。2000文字以内")] string? notes = null,
            [Description("チップ指定を解除し AI に一任")] bool clearChip = false,
            [Description("テンポ指定を解除し AI に一任")] bool clearTempo = false)
        {
            return McpBriefExecution.Run(_session, "tweak", () =>
            {
                RequireExclusiveClear(chip != null, clearChip, "chip");
                RequireExclusiveClear(tempo.HasValue, clearTempo, "tempoBpm");
                if (title == null && chip == null && tempo == null && mood == null && structure == null
                    && instrumentation == null && references == null && constraints == null && notes == null
                    && !clearChip && !clearTempo)
                {
                    throw new CompositionBriefException("InvalidParameter", "arguments", "変更する項目を指定してください。");
                }
                CompositionBrief current = CompositionBriefFile.Load(path);
                ChipKind? selectedChip = chip == null ? current.Chip : ParseChip(chip);
                CompositionBrief candidate = current with
                {
                    Title = title ?? current.Title,
                    Chip = clearChip ? null : selectedChip,
                    TempoBpm = clearTempo ? null : tempo ?? current.TempoBpm,
                    Mood = mood ?? current.Mood,
                    Structure = structure ?? current.Structure,
                    Instrumentation = instrumentation ?? current.Instrumentation,
                    References = references ?? current.References,
                    Constraints = constraints ?? current.Constraints,
                    Notes = notes ?? current.Notes
                };
                CompositionBriefFile.Save(candidate, path);
                return Saved("tweak", path, candidate);
            });
        }

        /// <summary>保存形式と同じ JSON で作曲指示書の全項目を返す。</summary>
        [McpServerTool(Name = "brief_show", ReadOnly = true, Destructive = false)]
        [Description("作曲指示書を読み、version を含む構造化 JSON 文字列を返す。現在の Song は変更しない。")]
        public string ShowBrief([Description("読み取る *.brief.json")] string path)
        {
            return McpBriefExecution.Run(_session, "show", () =>
                CompositionBriefFile.Serialize(CompositionBriefFile.Load(path)));
        }

        /// <summary>AI のプロンプトへそのまま貼れる日本語テキストだけを返す。</summary>
        [McpServerTool(Name = "brief_text", ReadOnly = true, Destructive = false)]
        [Description("作曲指示書を日本語の見出し付きテキストで返す。チップ・テンポの未指定は AI に一任と明示する。")]
        public string BriefText([Description("読み取る *.brief.json")] string path)
        {
            return McpBriefExecution.Run(_session, "text", () =>
                CompositionBriefTextRenderer.Render(CompositionBriefFile.Load(path)));
        }

        private static void RequireExclusiveClear(bool hasValue, bool clear, string path)
        {
            if (hasValue && clear)
            {
                throw new CompositionBriefException("InvalidParameter", path, "値の指定と解除は併用できません。");
            }
        }

        private static ChipKind ParseChip(string chip)
        {
            try
            {
                return ChipReference.ParseChip(chip);
            }
            catch (ArgumentException exception)
            {
                throw new CompositionBriefException("InvalidParameter", "chip", exception.Message, exception);
            }
        }

        private static string Saved(string operation, string path, CompositionBrief brief)
        {
            return SessionOutput.Serialize(new { operation, path = Path.GetFullPath(path), brief });
        }
    }
}
