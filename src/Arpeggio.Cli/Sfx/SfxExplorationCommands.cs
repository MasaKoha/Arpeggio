using System;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;
using Arpeggio.Core.Sfx.Parameters;
using Arpeggio.Core.Sfx.Presets;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>決定的な探索と、生成領域の明示置換・定義解除を CLI へ登録する。</summary>
    internal static class SfxExplorationCommands
    {
        internal static void AddTo(Command command)
        {
            command.Subcommands.Add(CreateRandomize());
            command.Subcommands.Add(CreateMutate());
            command.Subcommands.Add(CreateRegenerate());
            command.Subcommands.Add(CreateEdit("detach", "音を保持して通常ソングへ移行",
                (session, _, revision, dryRun) => session.Sfx.Detach(revision, dryRun)));
        }

        private static Command CreateRandomize()
        {
            var category = new Option<string>("--category") { Required = true, Description = "8用途または any" };
            var seed = new Option<string>("--seed") { Required = true, Description = "uint32 の固定シード" };
            Command command = CreateEdit("randomize", "カテゴリのレシピを固定シードで生成",
                (session, result, revision, dryRun) => session.Sfx.Randomize(
                    result.GetValue(category)!, ReadSeed(result.GetValue(seed)!), revision, dryRun));
            command.Options.Add(category);
            command.Options.Add(seed);
            return command;
        }

        private static Command CreateMutate()
        {
            var seed = new Option<string>("--seed") { Required = true, Description = "uint32 の固定シード" };
            var strength = new Option<string>("--strength")
            {
                DefaultValueFactory = _ => SfxParameterRandomizer.DefaultStrength.ToString(CultureInfo.InvariantCulture),
                Description = "0〜1 の変異強度"
            };
            var locks = new Option<string[]>("--lock")
            {
                Arity = ArgumentArity.OneOrMore, AllowMultipleArgumentsPerToken = false,
                Description = "保持する正規パス。一パスずつ反復指定可能。"
            };
            locks.Validators.Add(CliArgumentGuard.RejectFlagLikeToken);
            Command command = CreateEdit("mutate", "現在値へ固定シードの変異を適用",
                (session, result, revision, dryRun) => session.Sfx.Mutate(ReadSeed(result.GetValue(seed)!),
                    ReadStrength(result.GetValue(strength)!), result.GetValue(locks), revision, dryRun));
            command.Options.Add(seed);
            command.Options.Add(strength);
            command.Options.Add(locks);
            return command;
        }

        private static Command CreateRegenerate()
        {
            var replace = new Option<bool>("--replace-generated")
            {
                Required = true, Description = "保存パラメータから生成領域を全置換する明示要求"
            };
            Command command = CreateEdit("regenerate", "保存パラメータから生成領域を全置換",
                (session, result, revision, dryRun) => session.Sfx.Regenerate(result.GetValue(replace), revision, dryRun));
            command.Options.Add(replace);
            return command;
        }

        private static Command CreateEdit(string operation, string description,
            Func<EditSession, ParseResult, string?, bool, SfxEditResult> edit)
        {
            var command = new Command(operation, description);
            var path = new Argument<string>("path");
            var revision = new Option<string?>("--expected-revision");
            var dryRun = new Option<bool>("--dry-run");
            var json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(revision);
            command.Options.Add(dryRun);
            command.Options.Add(json);
            command.SetAction(result => CliSfxExecution.Run(operation, result.GetValue(json), () =>
                SfxFileTransaction.Edit(result.GetValue(path)!, session =>
                    edit(session, result, result.GetValue(revision), result.GetValue(dryRun)))));
            return command;
        }

        private static uint ReadSeed(string value)
        {
            if (!uint.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out uint seed))
            {
                throw new SfxParameterException("InvalidParameter", "seed", "seed は0〜4294967295の整数で指定してください。");
            }
            return seed;
        }

        private static double ReadStrength(string value)
        {
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double strength))
            {
                throw new SfxParameterException("InvalidParameter", "strength", "strength は有限の0〜1で指定してください。");
            }
            return strength;
        }
    }
}
