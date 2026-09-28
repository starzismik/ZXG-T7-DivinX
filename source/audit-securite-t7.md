# Audit de sécurité T7 Patch — [ZXG] T7 DivinX

## Conclusion

L’application ne contient pas le T7 Patch complet. La conservation des deux menus et une intégration progressive ont été choisies par l’utilisateur. Aucun statut « protection complète » ni garantie contre le piratage de compte ne doit être affiché.

## Référence examinée

Sources locales Scroptss/T7Patch-src, commit documenté 46b268a6cda05ac5ee2f8ff88c6db9f6d549df71, incluant des adaptations du travail de shiversoftdev/Serious. Comparaison avec les dépôts publics :
- https://github.com/Scroptss/T7Patch-src
- https://github.com/Scroptss/T7Patch
- https://github.com/shiversoftdev/t7patch

Le dépôt officiel signale des incompatibilités avec de nombreux menus de modification du jeu. Ajouter une seconde DLL T7 Patch au hasard ne résout pas les collisions de hooks.

## Couverture constatée avant modification

Le CMake des deux modules compile `native/features.cpp` et une adaptation Arxan. Il ne compile pas les fichiers complets `third_party/t7patch/Protection.cpp`, `Hooks.cpp` et `dllmain.cpp`.

Le socle commun comporte cinq filtres : types de lobby signés et non signés, NAT, présence et certaines config strings. Quatre hooks supplémentaires gèrent le protocole de mot de passe réseau, un le pseudo et un le libellé de version : onze au total. Le nombre de hooks ne mesure pas une couverture de sécurité.

Le menu complet active également des fonctions QOL de filtrage connectionless, d’éléments de lobby et de messages instantanés. Le module Divinium ne lance pas `hooks::onFrame` ni les substitutions Steam du menu complet. La présence de ces fonctions dans une source ne prouve pas leur activation dans les deux variantes.

## Première étape réalisée dans les sources

Trois comportements de blocage repris de `third_party/t7patch/Hooks.cpp` :

| Fonction amont | Action | RVA septembre 2026 |
|---|---|---|
| hkExecLuaCMD | Ignore ce point d’entrée de commande Lua | 0x1EF83A0 |
| hkUI_BrowserOpen | Refuse le navigateur intégré au jeu | 0x1EA4770 |
| hkMods_SubscribeUGC | Refuse les abonnements Workshop automatiques | 0x20CAA00 |

Ces blocages ne désactivent pas tout Lua dans BO3. Les cartes nécessaires doivent être installées manuellement via Steam.

Leur installation utilise la vérification SHA-256 exacte du moteur déjà présente, l’identification du build et une signature de seize octets par fonction. Les signatures ont été relevées dans la mémoire décompactée de BO3 ; les octets chiffrés du fichier sur disque n’ont pas été utilisés. Un échec de signature empêche l’installation du socle de hooks.

L’installation vérifie désormais aussi chaque mise en file d’activation MinHook. Une erreur de création, de mise en file ou d’activation déclenche le retrait des hooks créés par cette transaction. L’état moteur prêt n’est publié qu’après succès.

## Reste à intégrer et valider

- Validation complète des messages de lobby : join party, lobby state, lobby state game, heartbeat, info response et suivi des requêtes.
- Couverture homogène des messages instantanés et connectionless dans les deux variantes.
- Restrictions d’invitations et politique amis uniquement.
- Autres corrections de chaînes, modèles UI et commandes serveur de la référence.
- Gestion des exceptions et corrections de crashs amont, à adapter sans collision avec le gestionnaire QOL.
- Tests de paquets synthétiques, tests entre deux clients consentants et sessions prolongées.

Le statut runtime reste `PARTIAL`. Le mot de passe réseau ne remplace pas le mot de passe Steam et ne constitue pas un chiffrement général des échanges.

## Réconciliation initiale

Les empreintes du lanceur installé et du module Divinium correspondaient aux dernières compilations de cette tâche. L’empreinte du menu complet correspondait au build original : ce menu n’avait pas été recompilé lors des changements visuels et Divinium.

Les modifications actuelles sont maintenues dans `work/actions-source` de cette tâche. Le dossier GitHub original ne doit pas être présenté comme la copie à jour de toutes les modifications visuelles sans synchronisation explicite.

## Validation et installation de cette étape

Les deux variantes ont été compilées, testées séparément dans BO3 puis installées dans le dossier de l’application.

- Tests de défaillance : échec de création et de mise en file à chaque position, échec après activation partielle, succès complet.
- Tests de quantité souhaitée : réussis, y compris valeur supérieure/égale, solde nul et baisse du solde.
- Module Divinium : 14 hooks annoncés, rendu actif, ouverture/fermeture et contrôle de communication pendant 15 secondes réussis.
- Menu complet : mêmes vérifications réussies dans un nouveau processus BO3.
- Aucune action de farm/dépense ou d’attaque réseau exécutée. Ces essais ne prouvent pas le blocage de tous les exploits et ne remplacent pas des tests de session prolongée.
- Lanceur recompilé sans erreur ni avertissement, installé et rouvert sur le bureau.

Empreintes du paquet installé :
- ZXG Divinium.dll : A8C80EC8EDF66A5366B8D8D3114E4F0B3257179BC03A90A049A9875061B86ED0
- ZXG DivinX.dll : 6EC15569828564FBC7C57EF27F9F23FC166E499B19A9F6526730712963C71E43
- ZXG T7 DivinX.dll : A97B30D0B457FBCB6851366CA0DA4E1B9D2AF34E3B04943B5D4A02483CDF63D2

L’archive `sources-divinx-etape-securite-1.zip` regroupe les sources actuelles et les ressources utilisées pour ces compilations, ainsi que les tests et le script `build-current.ps1`. Elle constitue la référence réconciliée pour cette étape. La reproductibilité fonctionnelle est prévue ; les empreintes d’une recompilation peuvent varier en raison des métadonnées et chemins de compilation.

Les journaux de validation se trouvent dans `validation-securite`. L’IPC utilise encore son ancien libellé interne de runtime 0.5.0 ; la version produit affichée reste 1.0.0. Aucun de ces numéros ne signifie « T7 Patch complet ».
