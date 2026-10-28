using System.Windows;
using System.Windows.Input;

namespace TiaStudio.Desktop
{
    /// <summary>
    /// Az egyedi ablakkeret (PortalWindow stílus) fejléc-gombjainak parancskötései:
    /// SystemCommands.Minimize/Maximize/Restore/Close.
    /// </summary>
    public static class WindowChromeBehavior
    {
        public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
            "IsEnabled", typeof(bool), typeof(WindowChromeBehavior), new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject element, bool value)
        {
            element.SetValue(IsEnabledProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (!(d is Window window) || !(bool)e.NewValue)
            {
                return;
            }
            window.CommandBindings.Add(new CommandBinding(SystemCommands.CloseWindowCommand, (s, a) => SystemCommands.CloseWindow(window)));
            window.CommandBindings.Add(new CommandBinding(SystemCommands.MinimizeWindowCommand, (s, a) => SystemCommands.MinimizeWindow(window)));
            window.CommandBindings.Add(new CommandBinding(SystemCommands.MaximizeWindowCommand, (s, a) => SystemCommands.MaximizeWindow(window)));
            window.CommandBindings.Add(new CommandBinding(SystemCommands.RestoreWindowCommand, (s, a) => SystemCommands.RestoreWindow(window)));
        }
    }
}
