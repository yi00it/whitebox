using System.Windows;

namespace Whitebox.Core;

public enum ToastKind { Info, Success, Warning, Error }

public static class Notifier
{
    public static event Action<string, string, ToastKind>? Raised;

    public static void Show(string title, string message, ToastKind kind = ToastKind.Info) =>
        Application.Current?.Dispatcher.InvokeAsync(() => Raised?.Invoke(title, message, kind));
}

public static class Dialogs
{
    /// <summary>Set by the main window; shows the in-app confirmation dialog.</summary>
    public static Func<string, string, string, bool, bool>? ConfirmHandler { get; set; }

    public static bool Confirm(string title, string message, string confirmText = "Continue", bool danger = false) =>
        ConfirmHandler?.Invoke(title, message, confirmText, danger)
        ?? MessageBox.Show(message, title, MessageBoxButton.OKCancel) == MessageBoxResult.OK;
}

public static class Navigator
{
    /// <summary>Set by the main window; switches the visible page by key.</summary>
    public static Action<string>? Handler { get; set; }

    public static void Go(string key) => Handler?.Invoke(key);
}
