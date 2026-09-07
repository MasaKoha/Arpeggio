namespace Arpeggio.Core.Instruments.Snes
{
    /// <summary>内蔵音色の説明と再生時の推奨値。サンプル本体を保持しない。</summary>
    public sealed class SnesInstrumentPreset
    {
        internal SnesInstrumentPreset(string name, string category, string description, int sampleCount,
            int rootMidiNote, bool loop, SnesAdsrRegisters adsrRegisters, double echoSend)
        {
            Name = name;
            Category = category;
            Description = description;
            SampleCount = sampleCount;
            RootMidiNote = rootMidiNote;
            Loop = loop;
            AdsrRegisters = adsrRegisters;
            EchoSend = echoSend;
        }

        /// <summary>保存に使う小文字の名前。</summary>
        public string Name { get; }
        /// <summary>持続系・減衰系・ドラムの分類。</summary>
        public string Category { get; }
        /// <summary>音色の特徴。</summary>
        public string Description { get; }
        /// <summary>32 kHz での生成長。</summary>
        public int SampleCount { get; }
        /// <summary>元サンプルに対応する MIDI 音程。</summary>
        public int RootMidiNote { get; }
        /// <summary>サンプル全体をループするか。</summary>
        public bool Loop { get; }
        /// <summary>推奨 DSP ADSR レジスタ。</summary>
        public SnesAdsrRegisters AdsrRegisters { get; }
        /// <summary>推奨エコー送り。</summary>
        public double EchoSend { get; }
        /// <summary>素材のサンプルレート。</summary>
        public int SampleRate => SnesInstrumentBank.SampleRate;
        /// <summary>ループ開始サンプル。</summary>
        public int LoopStart => 0;
        /// <summary>ループ終端（含まない）。</summary>
        public int LoopEnd => SampleCount;
    }
}
