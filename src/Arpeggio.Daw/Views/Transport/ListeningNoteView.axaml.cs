using System;
using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Arpeggio.Daw.Presenters.Transport;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Arpeggio.Daw.Views.Transport
{
    /// <summary>感想メモの入力と保存操作、一時的な保存結果の表示を接続する。</summary>
    public partial class ListeningNoteView : UserControl, IDisposable
    {
        private readonly CompositeDisposable subscriptions = new CompositeDisposable();
        private readonly TextBox noteInput;
        private readonly Button saveButton;
        private readonly TextBlock operationMessage;
        private readonly TextBlock errorMessage;
        private ListeningNotePresenter listeningNotePresenter = null!;
        private bool isBound;
        private bool isDisposed;

        /// <summary>Flyout 内の入力・保存・結果表示部品を解決する。</summary>
        public ListeningNoteView()
        {
            AvaloniaXamlLoader.Load(this);
            noteInput = Require<TextBox>("NoteInput");
            saveButton = Require<Button>("SaveButton");
            operationMessage = Require<TextBlock>("OperationMessage");
            errorMessage = Require<TextBlock>("ErrorMessage");
        }

        /// <summary>MainWindow が所有する感想メモの Presenter を接続する。</summary>
        public void Bind(ListeningNotePresenter listeningNotePresenter)
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            if (isBound)
            {
                throw new InvalidOperationException("感想メモは接続済みです。");
            }
            this.listeningNotePresenter = listeningNotePresenter;
            isBound = true;
            SetEvent();
        }

        /// <summary>入力と保存操作、一時表示タイマーの購読を解放する。</summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }
            isDisposed = true;
            subscriptions.Dispose();
        }

        private void SetEvent()
        {
            var scheduler = new SynchronizationContextScheduler(new AvaloniaSynchronizationContext());
            subscriptions.Add(noteInput.GetObservable(TextBox.TextProperty)
                .Subscribe(text => saveButton.IsEnabled = !string.IsNullOrWhiteSpace(text)));
            subscriptions.Add(listeningNotePresenter.ObserveMessages(scheduler).Subscribe(ShowMessage));
            subscriptions.Add(Observable.Create<Unit>(observer =>
                saveButton.AddDisposableHandler(Button.ClickEvent, (_, _) => observer.OnNext(Unit.Default)))
                .Subscribe(_ => Save()));
        }

        private void Save()
        {
            if (listeningNotePresenter.Save(noteInput.Text ?? string.Empty))
            {
                noteInput.Text = string.Empty;
            }
        }

        private void ShowMessage(string message)
        {
            bool hasError = listeningNotePresenter.Error.Length > 0;
            operationMessage.Text = hasError ? string.Empty : message;
            errorMessage.Text = hasError ? message : string.Empty;
            operationMessage.IsVisible = !hasError && message.Length > 0;
            errorMessage.IsVisible = hasError && message.Length > 0;
        }

        private TControl Require<TControl>(string name) where TControl : Control => this.FindControl<TControl>(name)
            ?? throw new InvalidOperationException($"XAML に {name} がありません。");
    }
}
