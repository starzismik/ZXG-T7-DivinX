using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ZXGT7Guard;
public partial class MainWindow
{
    private bool _qolLoaded;
    private bool _qolReady;
    private int _windowDelayRemaining;
    private LoadedMenuInfo _detectedMenu = new(LoadedMenuKind.None,"");
    private string? _lastDetectedMenu;
    private bool MenuConflict => _detectedMenu.Kind is LoadedMenuKind.LegacyRuntime or LoadedMenuKind.Foreign or LoadedMenuKind.Multiple;
    private string SelectedModule => RuntimeMode.SelectedIndex == 1 ? GuardServices.DiviniumName : GuardServices.QolName;
    private bool IsQolMode => RuntimeMode?.SelectedIndex is 0 or 1;
    private async void RuntimeMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _busy) return;
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"selected-module.txt"),RuntimeMode.SelectedIndex.ToString());
        _runtime = null; _qolLoaded = false; _qolReady = false; _build = null; _attempted = false;
        _pendingName = _pendingPassword = null; _pendingKey = null;
        NetworkPassword.Clear();
        Log(IsQolMode ? "Mode T7 DivinX sélectionné (base Scropts-QOL)." : "Mode runtime ZXG sélectionné.");
        await MonitorAsync();
    }
    private async Task MonitorQolAsync()
    {
        if (_busy || _shutdown.IsCancellationRequested) return;
        _qolReady=false; _busy=true; bool load=false; string selectedModule=SelectedModule;
        try
        {
            var current=await Task.Run(GuardServices.DetectGame);
            if(current!=_game)
            {
                _game=current;_build=null;_runtime=null;_qolLoaded=false;_attempted=false;
                _detectedMenu=new(LoadedMenuKind.None,"");_lastDetectedMenu=null;
                Log(current is null ? "BO3 fermé ; état du menu invalidé." : $"BO3 détecté : PID {current.Pid}.");
            }
            if(current is not null)
            {
                _launchUntil=default;
                _windowDelayRemaining=await Task.Run(()=>GuardServices.GameWindowDelayRemaining(current));
                _build ??= await Task.Run(()=>GuardServices.InspectBuild(current.Path));
                _detectedMenu=await Task.Run(()=>GuardServices.GetLoadedMenu(current,selectedModule));
                _qolLoaded=_detectedMenu.Kind is LoadedMenuKind.Selected or LoadedMenuKind.OtherDivinX;
                if(_qolLoaded) {
                    // Selection reflects the actual resident DLL. The change event
                    // is suppressed by _busy, so it cannot start a second injection.
                    RuntimeMode.SelectedIndex=_detectedMenu.Name.Equals(GuardServices.DiviniumName,StringComparison.OrdinalIgnoreCase)?1:0;
                    selectedModule=SelectedModule;
                    _attempted=true;
                }
                string detectionKey=$"{current.Pid}:{current.Started}:{_detectedMenu.Kind}:{_detectedMenu.Name}";
                if(_lastDetectedMenu!=detectionKey) {
                    _lastDetectedMenu=detectionKey;
                    Log(_detectedMenu.Kind==LoadedMenuKind.None ? "BO3 ouvert : aucun menu connu chargé, injection disponible après vérification."
                        : $"Module déjà présent : {_detectedMenu.Name}. Aucune nouvelle injection.");
                }
                _runtime = _qolLoaded ? await GuardServices.ProbeAsync(current.Pid,_shutdown.Token) : null;
                if(MenuConflict) OperationValue.Text=$"Module déjà chargé : {_detectedMenu.Name}. Redémarrez BO3 pour charger DivinX.";
                else if(_qolLoaded) OperationValue.Text=_runtime?.Menu=="RENDERED"
                    ? "Menu déjà chargé : connexion rétablie, aucune réinjection."
                    : "DLL DivinX déjà présente : attente de confirmation du menu, aucune réinjection.";
                else if(!_build.DiagnosticSupported) OperationValue.Text="Build inconnu : chargement bloqué.";
                else if(!(_qolReady=await Task.Run(()=>GuardServices.QolRendererReady(current))))
                    OperationValue.Text=_windowDelayRemaining>0
                        ? $"Chargement disponible dans {_windowDelayRemaining} s après apparition de la fenêtre BO3."
                        : "Vérification de DirectX et Steam…";
                else { OperationValue.Text="BO3 prêt. Vous pouvez charger le menu."; load=AutoLoad.IsChecked==true&&!_attempted; }
            }
            else {
                _windowDelayRemaining=0;
                GuardServices.ObserveGameWindow(null,false,Environment.TickCount64);
                OperationValue.Text="En attente du jeu.";
            }
            _lastError=null;
        }
        catch(Exception ex){_qolLoaded=false;_runtime=null;ShowError(ex);}
        finally{_busy=false;Render();}
        await ApplyPendingSettingsAsync();
        if(load&&!_shutdown.IsCancellationRequested && QueueLaunchSettings())await ConnectQolAsync();
    }
    private async Task ConnectQolAsync()
    {
        if(_busy||_game is null||!_qolReady||_shutdown.IsCancellationRequested)return;
        var game=_game;var selectedModule=SelectedModule;_busy=true;_attempted=true;Render();
        try
        {
            var result=await Task.Run(()=>GuardServices.LoadQol(game,Path.Combine(AppContext.BaseDirectory,"plugins",selectedModule)));
            _qolLoaded=result.Loaded;Log(result.Message);OperationValue.Text=result.Message;
            if(result.Loaded&&MinimizeAfterLoad.IsChecked==true)WindowState=WindowState.Minimized;
        }
        catch(Exception ex){_qolLoaded=false;_runtime=null;ShowError(ex);}
        finally{_busy=false;Render();}
    }
    private void RenderQol()
    {
        SessionSettings.Visibility=Visibility.Visible;
        QolHelp.Visibility=Visibility.Visible;
        RuntimeMode.IsEnabled=!_busy&&!_qolLoaded&&!MenuConflict;
        bool detected=_game is not null;
        GameState.Text=detected?"Black Ops III détecté":_launchUntil>DateTime.UtcNow?"Lancement via Steam…":"Black Ops III est fermé";
        ProcessValue.Text=detected?$"BlackOps3.exe · PID {_game!.Pid}":"Aucun processus détecté";
        BuildValue.Text=_build is null?"Empreinte non vérifiée":_build.DiagnosticSupported?"Build BO3 reconnu":"Build inconnu · chargement bloqué";
        IpcValue.Text=_runtime is null?"En attente":"Connectée";RuntimeVersion.Text="Réglages transmis au menu unifié";
        RefreshFeatureStatus();
        if(MenuConflict)MenuValue.Text="Autre module détecté";
        else if (!_qolLoaded) MenuValue.Text="Non chargé";
        if (_runtime is null) MenuDetail.Text="T7 DivinX · F5 par défaut";
        PrimaryActionLabel.Text=!detected?"Lancer Call of Duty Black Ops 3":_busy?"Vérification…":MenuConflict?"Module déjà chargé":_qolLoaded?(_runtime?.Menu=="RENDERED"?"Menu déjà chargé":"Menu détecté · vérification…"):!_qolReady?(_windowDelayRemaining>0?$"Attente : {_windowDelayRemaining} s":"Préparation de BO3…"):"Injecter T7 DivinX";
        PrimaryAction.IsEnabled=!_busy&&!_qolLoaded&&!MenuConflict&&(detected?_qolReady&&_build?.DiagnosticSupported==true:_launchUntil<=DateTime.UtcNow);
        StopAction.IsEnabled=detected&&!_busy;
        StopAction.Visibility=detected ? Visibility.Visible : Visibility.Collapsed;
        BusyBar.Visibility=_busy?Visibility.Visible:Visibility.Collapsed;
    }
}
