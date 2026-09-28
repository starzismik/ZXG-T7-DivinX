# Sources, attributions et périmètre local

Le travail reste local. Aucune nouvelle licence ni autorisation de redistribution n'est déduite de la visibilité publique des dépôts. Les conditions générales de reprise de T7Patch-src et Scropts-QOL ne sont pas établies par une licence racine dans les copies auditées ; cela reste à résoudre avant toute distribution.

- Scroptss/T7Patch-src : commit `46b268a6cda05ac5ee2f8ff88c6db9f6d549df71`. Arxan.cpp et ses en-têtes nécessaires sont compilés ; GameBuild.h a reçu un garde de macro local. Les autres fichiers dans third_party/t7patch sont des références d'audit, hors compilation. Les mécanismes de pseudo, mot de passe et cinq filtres de features.cpp sont adaptés des sources auditées. Crédits Scroptss et shiversoftdev conservés.
- Scroptss/Scropts-QOL : commit `bbd4a19cead88372bb227d489a84d32c86a30e60`. Architecture de swap chain D3D11 et callbacks étudiée pour le renderer ZXG. Le menu complet, la progression et les fonctions Liquid Divinium ne sont pas compilés.
- Dear ImGui 1.89.5, Omar Cornut et contributeurs : sources et backends D3D11/Win32 de cette copie QOL, licence MIT dans `third_party/imgui/LICENSE.txt`. Aucun droit sur QOL dans son ensemble n'est déduit de cette licence.
- MinHook v1.3.4, Tsuda Kageyu : compilation depuis les sources, licence BSD à deux clauses dans `third_party/minhook/LICENSE.txt`.

Aucune bibliothèque Detours précompilée ni police Font Awesome n'est utilisée. Les bibliothèques ImGui et MinHook sont liées statiquement. Les sources originales et sauvegardes restent disponibles localement pour audit.

## Binaire officiel Scropts-QOL — mode ajouté en 0.6

Scroptss/Scropts-QOL v3.2.5, publié le 19 septembre 2026 : Scropts.QOL.v3.2.5.dll, copié sans modification sous le nom ZXG Menu.dll. SHA-256 60e43f0b2a6ca97b9f2d609830f32c33818cfa02581417229b006593a88a46eb, identique au digest publié par GitHub. Les options, l'interface et les crédits Scroptss restent inchangés. Ce binaire officiel n'a pas été reconstruit depuis la copie des sources auditées. Usage local demandé par l'utilisateur ; aucune redistribution publique effectuée. Le runtime ZXG est conservé comme un mode séparé, non chargé en parallèle. Voir docs/MENU_OFFICIEL.md.

## DivinX 0.7 — personnalisation locale
La DLL complète utilisée localement est désormais ZXG DivinX.dll, obtenue par localisation de la DLL officielle 3.2.5 : titre, 291 textes d’interface et références aux textes. Les fonctions de jeu ne sont pas recompilées depuis la copie publique incompatible. Le manifeste third_party/scropts-qol/divinx-provenance.json donne les différences et le SHA-256. Personnalisation : par STARZISMIK. Base : Scroptss / Scropts-QOL, Serious, InsaneCallum et contributeurs amont. Les descriptions de bibliothèques et de fonctionnalités du runtime 0.5 ci-dessus ne décrivent pas le binaire QOL complet. Aucun droit de redistribution publique supplémentaire n’est revendiqué.
