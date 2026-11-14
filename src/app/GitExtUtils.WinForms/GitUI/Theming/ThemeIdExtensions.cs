namespace GitExtUtils.GitUI.Theming;

public static class ThemeIdExtensions
{
    extension(ThemeId)
    {
        /// <summary>
        /// Get the default ThemeId for the current Windows SystemColorMode
        /// </summary>
        public static ThemeId ColorModeThemeId
            => Application.SystemColorMode == SystemColorMode.Dark
                ? ThemeId.DefaultDark
                : ThemeId.DefaultLight;
    }
}
