using System;
using System.Reactive.Linq;
using Avalonia.Interactivity;

namespace Arpeggio.Daw.Views.Sfx
{
    /// <summary>ルーティング入力を購読の寿命に結び付ける。</summary>
    internal static class SfxViewEvents
    {
        internal static IObservable<TArguments> Observe<TArguments>(Interactive control,
            RoutedEvent<TArguments> routedEvent, RoutingStrategies routes = RoutingStrategies.Bubble,
            bool handledEventsToo = false) where TArguments : RoutedEventArgs =>
            Observable.Create<TArguments>(observer => control.AddDisposableHandler(routedEvent,
                (_, arguments) => observer.OnNext(arguments), routes, handledEventsToo));
    }
}
