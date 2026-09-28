using System.Windows;

namespace ZXGT7Guard;
public partial class MainWindow
{
    private string? _pendingName, _pendingPassword;
    private bool _settingsBusy;
    private int? _pendingKey;
    private void RefreshFeatureStatus()
    {

        var ready = _runtime?.Protections == "PARTIAL";
        ProtectionValue.Text = ready ? "Partielles" : "Non installées";
        ProtectionDetail.Text = ready ? (_runtime!.Hooks == 14 ? "5 filtres + 3 blocages T7 Patch · 14 hooks" : $"5 filtres · {_runtime.Hooks} hooks") : "Initialisation non confirmée";
        MenuValue.Text = _runtime?.Menu == "RENDERED" ? (_runtime.MenuOpen ? "Ouvert" : "Disponible") : "En attente";
        MenuDetail.Text = _runtime?.Menu == "RENDERED" ? $"{_runtime.Frames} frames · F5 par défaut" : "Rendu non confirmé";
        CurrentNameValue.Text = "Pseudo du jeu : " + (_runtime?.PlayerName is { Length: > 0 } name ? name : "en attente");
        NetworkValue.Text = _runtime is null ? "État réseau : en attente" : _runtime.PrivateNetwork ? "Mot de passe réseau actif" : "Réseau public · mot de passe vide";
        if (_runtime?.Error is { Length: > 0 } error) OperationValue.Text = "Runtime : " + error;
    }
    private async Task ApplyPendingSettingsAsync()
    {
        if (_settingsBusy || _runtime?.Protections != "PARTIAL" || (_pendingName is null && _pendingPassword is null && _pendingKey is null)) return;
        _settingsBusy = true;
        try
        {
            int pid = _runtime.Pid;
            if (_pendingName is { } name)
            {
                await GuardServices.SendCommandAsync(pid,"NAME|" + GuardServices.EncodeSetting(name) + "\n",_shutdown.Token);
                if (_pendingName == name) _pendingName = null; Log("Pseudo appliqué par le runtime.");
            }
            if (_pendingPassword is { } pass)
            {
                await GuardServices.SendCommandAsync(pid,"PASSWORD|" + GuardServices.EncodeSetting(pass) + "\n",_shutdown.Token);
                if (_pendingPassword == pass) _pendingPassword = null; Log("Configuration réseau appliquée ; valeur non journalisée.");
            }
            if (_pendingKey is { } key)
            {
                await GuardServices.SendCommandAsync(pid,$"KEY|{key}\n",_shutdown.Token);
                if (_pendingKey == key) _pendingKey = null;
            }
            _runtime = await GuardServices.ProbeAsync(pid,_shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception ex) { SettingsMessage.Text = "Réglages non confirmés : " + ex.Message; }
        finally { _settingsBusy = false; RefreshFeatureStatus(); }
    }
    private bool QueueLaunchSettings()
    {
        string name = NameInput.Text;
        string pass = NetworkPassword.Password;
        if (name.Length > 15 || name.Any(c => c < 32 || c > 126)) { SettingsMessage.Text = "Pseudo : 15 caractères ASCII maximum."; return false; }
        if (pass.Length > 64 || pass.Any(c => c < 32 || c > 126)) { SettingsMessage.Text = "Mot de passe : 64 caractères ASCII maximum."; return false; }
        if (PrivateSession.IsChecked == true && pass.Length == 0 && string.IsNullOrEmpty(_pendingPassword) && _runtime?.PrivateNetwork != true) { SettingsMessage.Text = "Saisissez un mot de passe pour la session privée."; return false; }
        if (name.Length > 0) _pendingName = name;
        if (PrivateSession.IsChecked != true) _pendingPassword = "";
        else if (pass.Length > 0) _pendingPassword = pass;
        _pendingKey = MenuKeyInput.SelectedIndex switch { 1 => 117, 2 => 118, 3 => 119, _ => 116 };
        NetworkPassword.Clear();
        SettingsMessage.Text = "Réglages préparés ; application après confirmation du moteur.";
        return true;
    }
    private async void ApplyName_Click(object sender,RoutedEventArgs e)
    {
        string name = NameInput.Text;
        if (name.Length is < 1 or > 15 || name.Any(c=>c < 32 || c > 126)) { SettingsMessage.Text = "Utilisez 1 à 15 caractères ASCII imprimables."; return; }
        _pendingName = name; SettingsMessage.Text = "Pseudo en attente de confirmation du moteur.";
        await ApplyPendingSettingsAsync();
        if (_pendingName is null) SettingsMessage.Text = "Pseudo appliqué par le runtime.";
    }
    private async void ApplyPassword_Click(object sender,RoutedEventArgs e)
    {
        string pass = NetworkPassword.Password;
        if (pass.Length > 64 || pass.Any(c=>c < 32 || c > 126)) { SettingsMessage.Text = "Utilisez au maximum 64 caractères ASCII imprimables."; return; }
        PrivateSession.IsChecked = pass.Length > 0; _pendingPassword = pass; NetworkPassword.Clear(); SettingsMessage.Text = "Mot de passe en attente du moteur, gardé en mémoire seulement.";
        await ApplyPendingSettingsAsync();
        if (_pendingPassword is null) SettingsMessage.Text = "Configuration réseau appliquée.";
    }
    private async void ClearPassword_Click(object sender,RoutedEventArgs e)
    {
        PrivateSession.IsChecked = false; _pendingPassword = ""; NetworkPassword.Clear(); await ApplyPendingSettingsAsync();
        SettingsMessage.Text = _pendingPassword is null ? "Mot de passe effacé." : "Effacement prévu à la connexion.";
    }
    private async Task RunMenuCommand(string command)
    {
        if (_runtime is null) { SettingsMessage.Text = "Connectez le runtime au jeu."; return; }
        try { await GuardServices.SendCommandAsync(_runtime.Pid,command,_shutdown.Token); _runtime=await GuardServices.ProbeAsync(_runtime.Pid,_shutdown.Token); RefreshFeatureStatus(); SettingsMessage.Text="Commande confirmée par le runtime."; }
        catch (Exception ex) { SettingsMessage.Text=ex.Message; }
    }
    private async void RestoreName_Click(object sender,RoutedEventArgs e) { _pendingName=null; NameInput.Clear(); if (_runtime is null) { SettingsMessage.Text="Pseudo personnalisé annulé ; le pseudo du jeu sera conservé."; return; } await RunMenuCommand("RESTORE\n"); }
    private async void OpenMenu_Click(object sender,RoutedEventArgs e) => await RunMenuCommand("MENU|OPEN\n");
    private async void CloseMenu_Click(object sender,RoutedEventArgs e) => await RunMenuCommand("MENU|CLOSE\n");
    private async void ApplyKey_Click(object sender,RoutedEventArgs e)
    {
        _pendingKey = MenuKeyInput.SelectedIndex switch {1=>117,2=>118,3=>119,_=>116};
        await ApplyPendingSettingsAsync(); SettingsMessage.Text=_pendingKey is null ? "Touche du menu appliquée." : "Touche prévue à la connexion.";
    }
}
