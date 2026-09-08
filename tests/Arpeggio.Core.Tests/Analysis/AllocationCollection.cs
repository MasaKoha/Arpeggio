using Xunit;

namespace Arpeggio.Core.Tests.Analysis
{
    /// <summary>
    /// GC アロケーション量を計測するテストをまとめる collection。
    /// <see cref="System.GC.GetAllocatedBytesForCurrentThread"/> はスレッド単位の累計で、xunit が別 collection を
    /// 並列実行すると JIT のティア昇格などが計測区間に混ざって偽陽性になるため、この collection 内は直列に実行する。
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class AllocationCollection
    {
        /// <summary>collection 名。計測系のテストクラスへ <see cref="CollectionAttribute"/> で付ける。</summary>
        public const string Name = "Allocation";
    }
}
