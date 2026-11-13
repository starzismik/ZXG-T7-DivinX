using System.Diagnostics;
using System.IO;
using System.Windows;

namespace ZXGT7Guard;

public partial class MainWindow
{
    private void OpenDlcSteam_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("https://store.steampowered.com/dlc/311210/") { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }

    private async void RefreshDlc_Click(object sender, RoutedEventArgs e)
    {
        DlcFilesStatus.Text = "Vérification des fichiers locaux…";
        try
        {
            var result = await Task.Run(() =>
            {
                var exe = GuardServices.FindGame();
                if (exe is null) return "Installation de BO3 introuvable.";
                var zone = Path.Combine(Path.GetDirectoryName(exe)!, "zone");
                if (!Directory.Exists(zone)) return "Dossier des cartes introuvable.";
                var maps = new (string File, string Name)[] {
                    ("zm_factory", "The Giant"), ("zm_castle", "Der Eisendrache"),
                    ("zm_island", "Zetsubou No Shima"), ("zm_stalingrad", "Gorod Krovi"),
                    ("zm_genesis", "Revelations"), ("zm_prototype", "Nacht der Untoten"),
                    ("zm_asylum", "Verrückt"), ("zm_sumpf", "Shi No Numa"),
                    ("zm_theater", "Kino der Toten"), ("zm_cosmodrome", "Ascension"),
                    ("zm_temple", "Shangri-La"), ("zm_moon", "Moon"), ("zm_tomb", "Origins")
                };
                var files = Directory.EnumerateFiles(zone, "zm_*.ff", SearchOption.AllDirectories)
                    .Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var present = maps.Where(m => files.Contains(m.File)).Select(m => m.Name).ToArray();
                return present.Length == 0 ? "Aucun fichier des cartes DLC recherchées trouvé."
                    : "Fichiers de cartes présents : " + string.Join(", ", present);
            });
            DlcFilesStatus.Text = result;
        }
        catch (Exception ex) { DlcFilesStatus.Text = "Impossible de vérifier les fichiers locaux."; Log(ex.Message); }
    }
}
