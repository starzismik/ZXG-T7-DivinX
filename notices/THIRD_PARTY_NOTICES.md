# [ZXG] T7 DivinX — sources et attributions

Adaptation locale par STARZISMIK / ZXG, version 1.0.0. Site : https://modtools.fr.

Cette version compile le menu et les fonctions de session dans une seule DLL. Elle remplace les deux modes exclusifs précédents ; les descriptions historiques des versions 0.5 à 0.7 ne décrivent pas ce paquet.

- Menu : Scroptss / Scropts-QOL, copie du commit `bbd4a19cead88372bb227d489a84d32c86a30e60`, adaptée au build local de BO3, traduite et intégrée. Crédits d’origine : Scroptss, Serious, InsaneCallum et contributeurs amont.
- Fonctions de session et filtres défensifs : adaptations des travaux T7Patch de Scroptss et shiversoftdev. Copie T7Patch-src `46b268a6cda05ac5ee2f8ff88c6db9f6d549df71` ; Arxan.cpp est adapté dans `unified/native` pour suspendre temporairement les autres threads pendant l’écriture. Les en-têtes nécessaires sont conservés.
- Dear ImGui : Omar Cornut et contributeurs, sources et backends présents dans la copie QOL. Licence MIT jointe.
- MinHook 1.3.4 : Tsuda Kageyu, licence BSD à deux clauses jointe. Une seule instance est compilée pour le module unifié.
- Microsoft Detours : bibliothèque de la copie QOL liée au module. Les ressources de police et icônes de la copie QOL sont conservées.

Les sources adaptées sont dans `unified/`, les références amont et leurs notices dans `third_party/`. Les attributions ne sont pas remplacées par la personnalisation de l’interface.

Ce paquet est préparé pour l’usage local demandé. Il n’a pas été publié. La visibilité des dépôts amont ne constitue pas à elle seule une autorisation de redistribution ; les conditions amont doivent être vérifiées avant une publication.
