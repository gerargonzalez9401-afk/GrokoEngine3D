using System.IO;
using System.Windows.Forms;

namespace GrokoEngine.Hub;

/// <summary>Envoltorio del diálogo nativo de selección de carpeta (WinForms).</summary>
internal static class FolderDialog
{
    /// <summary>Muestra el diálogo y devuelve la carpeta elegida, o null si se cancela.</summary>
    public static string? Pick(string initialDir)
    {
        using var dlg = new FolderBrowserDialog
        {
            UseDescriptionForTitle = true,
            Description = "Selecciona una carpeta"
        };
        if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
            dlg.SelectedPath = initialDir;
        return dlg.ShowDialog() == DialogResult.OK ? dlg.SelectedPath : null;
    }
}
