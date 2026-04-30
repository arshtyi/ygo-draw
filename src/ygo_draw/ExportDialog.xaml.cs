using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace ygo_draw;

public enum ExportScope
{
    Current,
    History,
    List
}

public partial class ExportDialog : Window
{
    public ExportDialog(string projectRoot)
    {
        InitializeComponent();
        OutputDirectory = Path.Combine(projectRoot, "exports");
        OutputBox.Text = OutputDirectory;
    }

    public ExportScope Scope { get; private set; } = ExportScope.Current;
    public string OutputDirectory { get; private set; }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new WinForms.FolderBrowserDialog
        {
            SelectedPath = OutputDirectory,
            Description = "Select export folder",
            UseDescriptionForTitle = true
        };

        if (dialog.ShowDialog() == WinForms.DialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;
            OutputBox.Text = OutputDirectory;
        }
    }

    private void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        Scope = HistoryRadio.IsChecked == true
            ? ExportScope.History
            : ListRadio.IsChecked == true
                ? ExportScope.List
                : ExportScope.Current;

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
