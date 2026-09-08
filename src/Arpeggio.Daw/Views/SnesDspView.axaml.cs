using System;
using System.Globalization;
using Arpeggio.Core.Instruments;
using Arpeggio.Daw.Presenters;
using Arpeggio.Daw.Themes;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Arpeggio.Daw.Views
{
    /// <summary>ADSR の入力状態と DSP フラグの表示を音色パネルから分離する。</summary>
    public partial class SnesDspView : UserControl, IDisposable
    {
        private const string BorderResource = "TextControlBorderBrush";
        private const string HoverBorderResource = "TextControlBorderBrushPointerOver";
        private const string FocusBorderResource = "TextControlBorderBrushFocused";
        private readonly TextBox attackInput;
        private readonly TextBox decayInput;
        private readonly TextBox sustainLevelInput;
        private readonly TextBox sustainRateInput;
        private readonly TextBlock registerError;
        private readonly CheckBox pitchModulationInput;
        private readonly Border pitchModulationHint;
        private readonly CheckBox noiseEnabledInput;
        private readonly Slider noiseRateInput;
        private readonly TextBlock noiseRateValue;
        private bool isRefreshing;

        /// <summary>固定の入力部品を解決し、画面内の入力通知を接続する。</summary>
        public SnesDspView()
        {
            AvaloniaXamlLoader.Load(this);
            attackInput = Require<TextBox>("AttackInput");
            decayInput = Require<TextBox>("DecayInput");
            sustainLevelInput = Require<TextBox>("SustainLevelInput");
            sustainRateInput = Require<TextBox>("SustainRateInput");
            registerError = Require<TextBlock>("RegisterError");
            pitchModulationInput = Require<CheckBox>("PitchModulationInput");
            pitchModulationHint = Require<Border>("PitchModulationHint");
            noiseEnabledInput = Require<CheckBox>("NoiseEnabledInput");
            noiseRateInput = Require<Slider>("NoiseRateInput");
            noiseRateValue = Require<TextBlock>("NoiseRateValue");
            attackInput.TextChanged += OnRegistersChanged;
            decayInput.TextChanged += OnRegistersChanged;
            sustainLevelInput.TextChanged += OnRegistersChanged;
            sustainRateInput.TextChanged += OnRegistersChanged;
            noiseEnabledInput.IsCheckedChanged += OnNoiseChanged;
            noiseRateInput.ValueChanged += OnNoiseRateChanged;
        }

        /// <summary>未確定のノイズ切替に合わせて波形選択の有効状態を更新する。</summary>
        public event Action? NoiseChanged;

        /// <summary>現在のノイズ入力。</summary>
        public bool IsNoiseEnabled => noiseEnabledInput.IsChecked == true;

        /// <summary>音色参照が変わった場合だけ確定値を入力欄へ反映する。</summary>
        public void ShowInstrument(SnesSampleInstrument? instrument)
        {
            isRefreshing = true;
            try
            {
                IsEnabled = instrument != null;
                SnesAdsrRegisters? registers = instrument?.AdsrRegisters;
                attackInput.Text = registers?.Attack.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                decayInput.Text = registers?.Decay.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                sustainLevelInput.Text = registers?.SustainLevel.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                sustainRateInput.Text = registers?.SustainRate.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
                pitchModulationInput.IsChecked = instrument?.PitchModulation == true;
                noiseEnabledInput.IsChecked = instrument?.NoiseEnabled == true;
                noiseRateInput.Value = instrument?.NoiseRate ?? SnesInstrumentInput.MaximumRate;
                UpdateNoiseRateLabel();
                ValidateRegisters();
            }
            finally
            {
                isRefreshing = false;
            }
        }

        /// <summary>音色参照が同一でも、選択ボイスによる変調可否を更新する。</summary>
        public void ShowPitchModulationAvailability(bool canUsePitchModulation)
        {
            pitchModulationInput.IsEnabled = canUsePitchModulation;
            string? explanation = canUsePitchModulation ? null : "ボイス 0 は変調できません";
            ToolTip.SetTip(pitchModulationHint, explanation);
            ToolTip.SetTip(pitchModulationInput, explanation);
        }

        /// <summary>適用ボタンから検証境界へ渡す入力を取得する。</summary>
        public SnesInstrumentInput ReadInput() => new SnesInstrumentInput
        {
            Attack = attackInput.Text ?? string.Empty,
            Decay = decayInput.Text ?? string.Empty,
            SustainLevel = sustainLevelInput.Text ?? string.Empty,
            SustainRate = sustainRateInput.Text ?? string.Empty,
            PitchModulation = pitchModulationInput.IsChecked == true,
            NoiseEnabled = IsNoiseEnabled,
            NoiseRate = (int)noiseRateInput.Value
        };

        /// <summary>所有入力部品のイベントを解除する。</summary>
        public void Dispose()
        {
            attackInput.TextChanged -= OnRegistersChanged;
            decayInput.TextChanged -= OnRegistersChanged;
            sustainLevelInput.TextChanged -= OnRegistersChanged;
            sustainRateInput.TextChanged -= OnRegistersChanged;
            noiseEnabledInput.IsCheckedChanged -= OnNoiseChanged;
            noiseRateInput.ValueChanged -= OnNoiseRateChanged;
        }

        private void OnRegistersChanged(object? sender, TextChangedEventArgs arguments)
        {
            if (!isRefreshing)
            {
                ValidateRegisters();
            }
        }

        private void ValidateRegisters()
        {
            SnesInstrumentInput input = ReadInput();
            ShowValidity(attackInput, input.IsAttackValid);
            ShowValidity(decayInput, input.IsDecayValid);
            ShowValidity(sustainLevelInput, input.IsSustainLevelValid);
            ShowValidity(sustainRateInput, input.IsSustainRateValid);
            registerError.IsVisible = !input.IsAttackValid || !input.IsDecayValid ||
                !input.IsSustainLevelValid || !input.IsSustainRateValid;
        }

        private static void ShowValidity(TextBox input, bool isValid)
        {
            if (isValid)
            {
                input.Resources.Remove(BorderResource);
                input.Resources.Remove(HoverBorderResource);
                input.Resources.Remove(FocusBorderResource);
                input.ClearValue(TextBox.BorderBrushProperty);
            }
            else
            {
                var dangerBrush = ThemeResources.GetBrush("Arpeggio.Danger");
                input.BorderBrush = dangerBrush;
                // Fluent のフォーカス・ホバー枠にも同じエラー色を渡す。
                input.Resources[BorderResource] = dangerBrush;
                input.Resources[HoverBorderResource] = dangerBrush;
                input.Resources[FocusBorderResource] = dangerBrush;
            }
        }

        private void OnNoiseChanged(object? sender, RoutedEventArgs arguments)
        {
            if (!isRefreshing)
            {
                NoiseChanged?.Invoke();
            }
        }

        private void OnNoiseRateChanged(object? sender, RangeBaseValueChangedEventArgs arguments) => UpdateNoiseRateLabel();
        private void UpdateNoiseRateLabel() => noiseRateValue.Text = ((int)noiseRateInput.Value).ToString(CultureInfo.InvariantCulture);

        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"XAML に {name} がありません。");
    }
}
