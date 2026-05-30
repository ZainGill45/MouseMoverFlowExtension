using System.Windows.Controls;
using Flow.Launcher.Plugin;

namespace MouseMover;

/// <summary>WPF settings panel shown in Flow Launcher's plugin settings page.</summary>
public partial class SettingsControl : UserControl
{
    private readonly PluginInitContext _context;

    public SettingsControl(PluginInitContext context, MouseMoverSettings settings)
    {
        _context = context;
        InitializeComponent();
        DataContext = settings; // two-way bindings read/write the live settings object
    }

    /// <summary>Persist after each edit. Clamping already happened in the setters.</summary>
    private void OnSave(object sender, System.Windows.RoutedEventArgs e)
        => _context.API.SaveSettingJsonStorage<MouseMoverSettings>();
}
