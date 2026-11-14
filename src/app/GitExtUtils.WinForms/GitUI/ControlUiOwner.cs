using GitExtensions.Extensibility;

namespace GitUI;

/// <summary>
///  Adapts a WinForms <see cref="Control"/> to <see cref="IUiOwner"/>.
/// </summary>
public sealed class ControlUiOwner(Control control) : IUiOwner
{
    public CancellationToken DisposedToken => control.GetDisposedCancellationToken();

    public object? NativeOwner => control;
}

public static class ControlUiOwnerExtensions
{
    public static IUiOwner AsUiOwner(this Control control) => new ControlUiOwner(control);
}
