using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Windows;
namespace ZXGT7Guard;
public partial class MainWindow
{
    // Set the publisher's HTTPS manifest URL before distribution.
    private const string UpdateManifestUrl = "https://raw.githubusercontent.com/starzismik/ZXG-T7-DivinX/main/update.json";
    private bool _updateRequired, _startupCompleted;
    private Uri? _updateDownload;
    private static readonly HttpClient UpdateHttp = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(12) };
    private async Task<bool> CheckUpdatesAsync()
    {
        if (string.IsNullOrWhiteSpace(UpdateManifestUrl)) return true;
        _updateRequired = true;
        ApplicationContent.IsEnabled = false;
        ApplicationContent.Opacity = 0.3;
        UpdateOverlay.Visibility = Visibility.Visible;
        UpdateTitle.Text = "RECHERCHE DE MISE À JOUR";
        UpdateSummary.Text = "Vérification de la version…";
        UpdateDetails.Text = "Veuillez patienter avant d’utiliser l’application.";
        UpdateDownloadButton.Visibility = Visibility.Collapsed;
        UpdateRetryButton.Visibility = Visibility.Collapsed;
        UpdateNotice.Visibility = Visibility.Collapsed;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await UpdateHttp.GetAsync(UpdateManifest.ParseHttps(UpdateManifestUrl), HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            response.EnsureSuccessStatusCode();
            using var body = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new System.IO.MemoryStream();
            var chunk = new byte[4096];
            int read;
            while ((read = await body.ReadAsync(chunk, timeout.Token)) > 0)
            {
                if (buffer.Length + read > 65536) throw new FormatException("Réponse trop volumineuse.");
                buffer.Write(chunk, 0, read);
            }
            var manifest = UpdateManifest.Parse(System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
            var current = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1,0,0,0);
            UpdateVersions.Text = $"Version installée : {current.ToString(3)}  ·  Nouvelle version : {manifest.Version.ToString(3)}";
            if (manifest.Version > current)
            {
                _updateDownload = manifest.Download;
                UpdateTitle.Text = "MISE À JOUR DISPONIBLE";
                UpdateSummary.Text = "Une nouvelle version est disponible.";
                UpdateDetails.Text = "Téléchargez et installez la dernière version pour continuer.";
                UpdateDownloadButton.Visibility = Visibility.Visible;
                return false;
            }
            _updateRequired = false;
            ApplicationContent.IsEnabled = true;
            ApplicationContent.Opacity = 1;
            UpdateOverlay.Visibility = Visibility.Collapsed;
            return true;
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { return false; }
        catch (Exception e)
        {
            Log("Vérification des mises à jour impossible : " + e.Message);
            UpdateTitle.Text = "VÉRIFICATION INDISPONIBLE";
            UpdateSummary.Text = "Impossible de vérifier la version.";
            UpdateDetails.Text = "Vérifiez votre connexion Internet, puis réessayez pour continuer.";
            UpdateRetryButton.Visibility = Visibility.Visible;
            return false;
        }
    }
    private async Task StartAfterUpdateCheckAsync()
    {
        if (_startupCompleted || !await CheckUpdatesAsync()) return;
        _startupCompleted = true;
        await DiscoverAsync();
        _monitor.Start();
        await MonitorAsync();
    }
    private async void UpdateRetry_Click(object sender, RoutedEventArgs e) => await StartAfterUpdateCheckAsync();
    private void UpdateDownload_Click(object sender, RoutedEventArgs e)
    {
        if (_updateDownload is null) return;
        try { Process.Start(new ProcessStartInfo(_updateDownload.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception)
        {
            UpdateNotice.Text = "Impossible d’ouvrir le navigateur. Réessayez.";
            UpdateNotice.Visibility = Visibility.Visible;
        }
    }
}
