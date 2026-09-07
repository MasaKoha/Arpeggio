namespace Arpeggio.Cli
{
    /// <summary>arpeggio コマンドのエントリーポイント。</summary>
    public static class Program
    {
        /// <summary>引数の処理を実行境界へ委譲する。</summary>
        public static int Main(string[] arguments)
        {
            return CliExecution.Run(arguments);
        }
    }
}
