using System;
using System.CommandLine;
using System.Linq;
using Arpeggio.Core.Document;
using Arpeggio.Core.Session;
using Arpeggio.Core.Sfx;

namespace Arpeggio.Cli.Sfx
{
    /// <summary>定義付き SFX の作成・現在値・部分編集を既存 CLI へ登録する。</summary>
    internal static class EditableSfxCommands
    {
        internal static void AddTo(Command command)
        {
            command.Subcommands.Add(CreateNew());
            command.Subcommands.Add(CreateParameters());
            command.Subcommands.Add(CreateTweak());
        }

        internal static Command CreateList()
        {
            var command = new Command("list", "全プリセットの名前と説明");
            var editable = new Option<bool>("--editable");
            var json = new Option<bool>("--json");
            command.Options.Add(editable);
            command.Options.Add(json);
            command.SetAction(result =>
            {
                if (!result.GetValue(editable) && !result.GetValue(json))
                {
                    foreach (SfxPresetDescription preset in SfxPresetCatalog.GetAll())
                    {
                        Console.WriteLine($"{preset.Name}: {preset.Description}");
                    }
                    return CliExecution.Success;
                }
                return CliSfxExecution.Run("list", result.GetValue(json), () =>
                {
                    if (!result.GetValue(editable))
                    {
                        return new { operation = "list", editable = false, presets = SfxPresetCatalog.GetAll() };
                    }
                    ChipKind[] chips = { ChipKind.Nes, ChipKind.GameBoy, ChipKind.Snes };
                    return new
                    {
                        operation = "list", editable = true,
                        presets = SfxParameterPresetCatalog.GetAll(ChipKind.Nes).Select(preset => new
                        {
                            preset.Name, preset.Description, supportedChips = chips,
                            defaults = chips.Select(chip => new
                            {
                                chip, parameters = SfxOutput.Parameters(SfxParameterPresetCatalog.Get(preset.Kind, chip).Parameters)
                            }).ToArray()
                        }).ToArray()
                    };
                });
            });
            return command;
        }

        private static Command CreateNew()
        {
            var command = new Command("create", "再編集できる定義付き効果音を新規保存");
            var path = new Argument<string>("path");
            var chip = new Option<string>("--chip") { DefaultValueFactory = _ => "nes" };
            var preset = new Option<string>("--preset") { DefaultValueFactory = _ => "jump" };
            var title = new Option<string?>("--title");
            var dryRun = new Option<bool>("--dry-run");
            var json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(chip);
            command.Options.Add(preset);
            command.Options.Add(title);
            command.Options.Add(dryRun);
            command.Options.Add(json);
            command.SetAction(result => CliSfxExecution.Run("create", result.GetValue(json), () =>
            {
                string outputPath = result.GetValue(path)!;
                SfxFileTransaction.RequireNewDestination(outputPath);
                ChipKind selectedChip = ChipReference.ParseChip(result.GetValue(chip)!);
                SfxParameterPresetDescription selectedPreset = SfxParameterPresetCatalog.Get(
                    SfxParameterPresetCatalog.Parse(result.GetValue(preset)), selectedChip);
                SfxSongCompilationResult generation = SfxEditor.CreateCandidate(selectedPreset.Parameters,
                    selectedChip, result.GetValue(title) ?? selectedPreset.Name, selectedPreset.Name);
                bool preview = result.GetValue(dryRun);
                if (!preview)
                {
                    SfxFileTransaction.Create(outputPath, generation.Song);
                }
                return SfxOutput.Describe("create", generation.Song, generation, true, preview,
                    preview ? null : SfxHash.ComputeRevision(generation.Song));
            }));
            return command;
        }

        private static Command CreateParameters()
        {
            var command = new Command("params", "現在値・同期状態・revision・生成診断");
            var path = new Argument<string?>("path") { Arity = ArgumentArity.ZeroOrOne };
            path.Validators.Add(CliArgumentGuard.RejectFlagLikeToken);
            var chip = new Option<string>("--chip") { DefaultValueFactory = _ => "nes" };
            var schema = new Option<bool>("--schema");
            var json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(chip);
            command.Options.Add(schema);
            command.Options.Add(json);
            command.SetAction(result => CliSfxExecution.Run("params", result.GetValue(json), () =>
            {
                string? sourcePath = result.GetValue(path);
                ChipKind selectedChip = ChipReference.ParseChip(result.GetValue(chip)!);
                if (sourcePath == null)
                {
                    SfxSongCompilationResult defaults = SfxEditor.CreateCandidate(SfxParameterCatalog.CreateDefaults(selectedChip), selectedChip);
                    var initial = SfxOutput.Describe("params", defaults.Song, defaults);
                    initial["candidateRevision"] = null;
                    initial["schema"] = SfxOutput.Schema(selectedChip);
                    return initial;
                }
                Song song = SongSerializer.Load(sourcePath);
                // chip の既定値を、読み込んだ別チップへの明示指定として扱わない。
                if (result.GetResult(chip) is { Implicit: false } && selectedChip != song.Chip)
                {
                    throw new SfxParameterException("UnsupportedParameter", "chip", "保存済み Song と異なるチップは指定できません。");
                }
                SfxSynchronizationState synchronization = SfxSynchronization.Inspect(song);
                SfxSongCompilationResult? generation = synchronization.Editable
                    ? SfxSongCompiler.Compile(synchronization.Parameters!, song.Chip, song.Title) : null;
                var output = SfxOutput.Describe("params", song, generation, revision: SfxHash.ComputeRevision(song));
                if (result.GetValue(schema))
                {
                    output["schema"] = SfxOutput.Schema(song.Chip);
                }
                return output;
            }));
            return command;
        }

        private static Command CreateTweak()
        {
            var command = new Command("tweak", "部分パラメータを一操作で適用");
            var path = new Argument<string>("path");
            var revision = new Option<string?>("--expected-revision");
            var dryRun = new Option<bool>("--dry-run");
            var json = new Option<bool>("--json");
            command.Arguments.Add(path);
            command.Options.Add(revision);
            command.Options.Add(dryRun);
            command.Options.Add(json);
            var parameters = new SfxParameterOptions(command);
            command.SetAction(result => CliSfxExecution.Run("tweak", result.GetValue(json), () =>
                SfxFileTransaction.Edit(result.GetValue(path)!, session => session.Sfx.Tweak(
                    parameters.Read(result, session.Song!.Chip), result.GetValue(revision), result.GetValue(dryRun)))));
            return command;
        }
    }
}
