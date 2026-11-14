using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace GitExtensions.Desktop.Views;

/// <summary>
///  A modal dialog window hosting one view, like the dialogs of the original Git Extensions: it is centered on its owner,
///  not shown in the task bar and closes with Escape. Create them with <see cref="DialogHost.Bind"/>.
/// </summary>
public sealed class DialogWindow : Window
{
    public DialogWindow(string title, Control content, double width, double height, bool canResize)
    {
        Title = title;
        Content = content;
        Width = width;
        Height = height;
        MinWidth = Math.Min(width, 360);
        MinHeight = Math.Min(height, 200);
        CanResize = canResize;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this[!BackgroundProperty] = new DynamicResourceExtension("DialogBackground");
        KeyDown += OnKeyDown;
    }

    /// <summary>Gets the dialogs that are currently shown (newest last); tests use it to find them.</summary>
    public static List<DialogWindow> Open { get; } = [];

    /// <summary>Gets the view shown by the dialog.</summary>
    public Control View => (Control)Content!;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Open.Add(this);
        FocusFirstInput();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        Open.Remove(this);
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Also when the owner was closed with the dialog still open.
        Open.Remove(this);
        base.OnClosed(e);
    }

    private void FocusFirstInput()
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(
            () =>
            {
                if (FocusManager?.GetFocusedElement() is null && View.IsEffectivelyVisible)
                {
                    View.Focus();
                }
            },
            Avalonia.Threading.DispatcherPriority.Loaded);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            Close();
        }
    }
}

/// <summary>
///  Opens a <see cref="DialogWindow"/> while a view-model's <c>IsOpen</c> is true and closes it when it turns false; closing the
///  window (button, Escape, title bar) sets <c>IsOpen</c> back to false. This lets the existing "open/close" view-models
///  drive real dialogs instead of in-window overlays.
/// </summary>
public static class DialogHost
{
    /// <summary>Connects <paramref name="viewModel"/> (an object with a settable bool <c>IsOpen</c>) to a dialog owned by <paramref name="owner"/>.</summary>
    /// <param name="createView">Creates the view (with its DataContext) each time the dialog opens.</param>
    /// <param name="describe">When given, supplies the title and size each time the dialog opens (for dialogs whose title depends on the operation); overrides <paramref name="title"/>, <paramref name="width"/> and <paramref name="height"/>.</param>
    public static void Bind(Window owner, INotifyPropertyChanged viewModel, Func<Control> createView, string title, double width, double height, bool canResize = true, Func<(string Title, double Width, double Height)>? describe = null)
    {
        var isOpen = viewModel.GetType().GetProperty("IsOpen") ?? throw new ArgumentException("The view-model needs an IsOpen property.", nameof(viewModel));
        DialogWindow? dialog = null;

        void Sync()
        {
            bool wanted = (bool)isOpen.GetValue(viewModel)!;
            if (wanted && dialog is null)
            {
                (string openTitle, double openWidth, double openHeight) = describe?.Invoke() ?? (title, width, height);
                DialogWindow created = new(openTitle, createView(), openWidth, openHeight, canResize);
                dialog = created;
                created.Closed += (_, _) =>
                {
                    if (ReferenceEquals(dialog, created))
                    {
                        dialog = null;
                    }

                    if ((bool)isOpen.GetValue(viewModel)!)
                    {
                        isOpen.SetValue(viewModel, false);
                    }
                };
                _ = created.ShowDialog(owner);
            }
            else if (!wanted && dialog is not null)
            {
                DialogWindow closing = dialog;
                dialog = null;
                closing.Close();
            }
        }

        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != "IsOpen")
            {
                return;
            }

            if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
            {
                Sync();
            }
            else
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(Sync);
            }
        };
    }
}
