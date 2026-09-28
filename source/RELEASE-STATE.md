# Sources de la release 1.0.0

Cette copie provient des sources réconciliées `actions-source`, et non de l'ancien
dépôt ZXG-T7-Guard. Les documents d'audit présents sont historiques.

Changements de publication : URL publique renseignée dans UpdateGate, distribution
.NET autonome en fichier unique et résolution des modules dans le dossier extrait.
Les deux DLL distribuées sont les compilations validées fournies par le responsable,
vérifiées contre les empreintes de GuardServices.cs ; leur code n'a pas été modifié
durant cette étape de conditionnement.

L'icône statue couronnée, le titre et les onglets Accueil / Modules / Journal / Infos
sont conservés. Les réglages de session se trouvent dans Accueil. L'intégration T7
Patch reste partielle. Consultez PUBLICATION.md et les notices à la racine.

Pour reconstruire les modules et publier le lanceur : `publish-release.ps1`.
Visual Studio 2022 avec C++, CMake et le SDK .NET 8 sont requis. Les empreintes
peuvent varier à la recompilation ; build-current.ps1 recalcule celles des modules.

Le paquet publié inclut les notices dans l'EXE. GitHub fournit également ces notices
dans le dossier notices, sans avoir à lancer l'application.
