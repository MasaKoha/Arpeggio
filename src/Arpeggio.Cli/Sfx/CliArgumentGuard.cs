using System;
using System.CommandLine.Parsing;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>値オプションが後続の別オプション識別子を値として飲み込むのを防ぐ。</summary>
    internal static class CliArgumentGuard
    {
        /// <summary>値が "--" で始まる場合、値未指定として解析エラーにする。
        /// System.CommandLine は多値・単値オプションの末尾に値が無いとき、次に続く
        /// 既知オプションのトークン（例: "--json"）を値として飲み込んでしまうため、
        /// このライブラリのパラメータ・ロック値が "--" で始まることは無い前提で弾く。</summary>
        internal static void RejectFlagLikeToken(OptionResult optionResult)
        {
            foreach (Token token in optionResult.Tokens)
            {
                if (token.Value != null && token.Value.StartsWith("--", StringComparison.Ordinal))
                {
                    optionResult.AddError($"{optionResult.Option.Name} に値が指定されていません。");
                    return;
                }
            }
        }

        /// <summary>未知オプションが位置引数として飲み込まれるのを防ぐ。
        /// 例: "sfx params --unknown" の "--unknown" は既知オプションに一致しないため、
        /// 解析エラーにならず ZeroOrOne の path 引数の値として吸収されてしまう。</summary>
        internal static void RejectFlagLikeToken(ArgumentResult argumentResult)
        {
            foreach (Token token in argumentResult.Tokens)
            {
                if (token.Value != null && token.Value.StartsWith("--", StringComparison.Ordinal))
                {
                    argumentResult.AddError($"認識できない引数です: {token.Value}");
                    return;
                }
            }
        }
    }
}
