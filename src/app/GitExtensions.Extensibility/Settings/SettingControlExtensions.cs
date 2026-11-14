using System.Runtime.CompilerServices;

namespace GitExtensions.Extensibility.Settings;

/// <summary>
///  Optional interface for a setting that supplies its own WinForms control binding.
///  Settings that do not implement it get the default binding from
///  GitUI.SettingControlBindings.SettingControlBindingsProvider.
/// </summary>
public interface ISettingWithControlBinding : ISetting
{
    /// <summary>
    ///  Creates a new binding between a setting and a user interface control.
    /// </summary>
    /// <returns>The binding, or <see langword="null"/> if the default binding should be used.</returns>
    ISettingControlBinding? CreateControlBinding();
}

/// <summary>
///  Attaches an optional WinForms <c>CustomControl</c> to the platform-neutral setting types
///  (which live in GitExtensions.Extensibility.Core and cannot reference WinForms).
///  Source-compatible with the former <c>CustomControl</c> properties.
/// </summary>
public static class SettingControlExtensions
{
    private static readonly ConditionalWeakTable<object, StrongBox<Control?>> _controls = [];

    private static TControl? Get<TControl>(object setting)
        where TControl : Control
        => _controls.TryGetValue(setting, out StrongBox<Control?>? box) ? box.Value as TControl : null;

    private static void Set(object setting, Control? control)
        => _controls.GetOrCreateValue(setting).Value = control;

    extension(BoolSetting setting)
    {
        public CheckBox? CustomControl
        {
            get => Get<CheckBox>(setting);
            set => Set(setting, value);
        }
    }

    extension(StringSetting setting)
    {
        public TextBox? CustomControl
        {
            get => Get<TextBox>(setting);
            set => Set(setting, value);
        }
    }

    extension(PasswordSetting setting)
    {
        public TextBox? CustomControl
        {
            get => Get<TextBox>(setting);
            set => Set(setting, value);
        }
    }

    extension(ChoiceSetting setting)
    {
        public ComboBox? CustomControl
        {
            get => Get<ComboBox>(setting);
            set => Set(setting, value);
        }
    }

    extension<T>(NumberSetting<T> setting)
    {
        public Control? CustomControl
        {
            get => Get<Control>(setting);
            set => Set(setting, value);
        }
    }
}
