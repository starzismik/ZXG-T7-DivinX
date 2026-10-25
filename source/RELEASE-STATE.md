# Sources de la release 1.0.2

Interface Windows gris foncé compacte validée par STARZISMIK : 1160 × 740,
icônes Lucide, panneau MODMENU, onglet MODULE et journal récent avec heure et message.
La compilation WPF et le démarrage du paquet autonome ont été vérifiés.
Les ajustements natifs antérieurs sont inclus ; leur stabilité en jeu reste à tester.

Le ZIP contient un seul EXE avec .NET 8.0.19 et les deux modules embarqués.
Le lanceur extrait le paquet dans %LOCALAPPDATA%/ZXG/T7DivinX/1.0.2-<empreinte>.
Ce cache et les journaux locaux sont conservés après fermeture.
Aucun journal, mot de passe ni réglage personnel n'est inclus dans la distribution.
Les modules distribués sont vérifiés contre les empreintes de GuardServices.cs.
Le manifeste de mise à jour conserve l'adresse publique du dépôt.

Le lanceur autonome se trouve dans tools/StandaloneLauncher.cs.
