# [ZXG] T7 DivinX — état actuel et utilisation

Application Windows personnalisée par STARZISMIK pour Call of Duty: Black Ops III PC/Steam. État actualisé après la première étape de renforcement de sécurité du 28 septembre 2026.

## Ce que fait l’application

Elle détecte BO3, permet son lancement, attend la disponibilité du rendu puis charge l’un de deux modules alternatifs : le menu complet ou Divinium / Marché noir. Le lanceur présente les réglages de session, le diagnostic et les journaux. Les onglets Modules et Infos sont maintenant distincts.

Le menu complet conserve ses catégories adaptées de Scropts-QOL. Les termes techniques, crédits et composants tiers sont conservés. Toutes les options du menu n’ont pas été testées.

## Démarrer et ouvrir le menu

1. Ouvrir `ZXG T7 DivinX.exe` dans le dossier du paquet.
2. Choisir le module souhaité dans Accueil. Les deux modules sont alternatifs : redémarrer BO3 pour changer de module.
3. Préparer, si nécessaire, le pseudo, le mot de passe de session et la touche du menu affichée dans l’application. F5 est la touche par défaut.
4. Démarrer BO3, via Steam ou le lanceur. Le chargement du menu ne peut fonctionner que lorsque le jeu est en route et que son rendu est prêt.
5. Attendre la fin du démarrage. Le lanceur impose notamment une stabilisation d’au moins 45 secondes après le démarrage du processus et vérifie le rendu. Ce délai ne signifie pas que le chargement sera forcément prêt à la seconde près.
6. En mode manuel, utiliser le bouton de chargement lorsqu’il devient disponible. Cliquer plusieurs fois ne rend pas le jeu prêt plus vite. Lire le statut et le journal si le chargement tarde.
7. Si « Chargement automatique » est coché, le lanceur tente lui-même le chargement lorsque les conditions sont réunies. Il n’est pas nécessaire de cliquer en plus. Une incompatibilité ou une erreur n’est pas contournée par cette option.
8. Une fois le module chargé et son rendu confirmé, utiliser la touche d’ouverture indiquée dans l’application. La même touche ferme le menu.

Après une mise à jour des DLL, rouvrir le lanceur et redémarrer BO3 : une DLL déjà chargée reste en mémoire jusqu’à la fermeture du jeu. Un lanceur ouvert ne signifie pas, à lui seul, que le menu ou ses protections sont chargés dans BO3.

## Menu Divinium

- Illustration complète approuvée : titre néon, fioles et fumée cyan réunis dans une seule image, ajustée sans déformation ni découpe des fioles.
- Lecture du solde natif, Actualiser et quantité souhaitée.
- Quantité limitée sous le solde lu ; nouvelle lecture et contrôle lors de l’application. Si le solde est nul ou indisponible, la saisie et l’application sont désactivées.
- La diminution utilise des dépenses par trois avec attente de confirmation. Une cible non atteignable par ces dépenses est refusée sans arrondi silencieux.
- Une demande d’augmentation par la quantité souhaitée n’est pas autorisée. L’ancienne abstraction interne d’augmentation ne fournit pas une attribution réelle et n’est pas utilisée par ce parcours.
- Actions : Farm de Divinium et Dépenser le Divinium reprennent les indicateurs et la boucle du menu source. La dépense désactive le farm et la simulation du marché noir, puis appelle la dépense par trois.
- Les actions continues restent actives lorsque le menu est fermé : les décocher ou utiliser Arrêter pour les stopper. Elles sont distinctes de l’application d’une quantité cible.
- Onglet Infos avec crédits centrés dans un cadre, repris du menu complet.

## Apparence du lanceur

Image du soldat à visière bleue fournie par l’utilisateur. Titre [ZXG] T7 DivinX avec « par STARZISMIK », pied de page « Version 1.0.0 ». Modules décrit les variantes, protections et diagnostic ; Infos présente la version, les crédits et la portée des protections.

## Sécurité : intégration partielle

Le socle commun conserve cinq filtres adaptés de T7 Patch et le mécanisme de mot de passe réseau. Cette étape ajoute trois blocages repris de la référence : point de commande Lua dédié, navigateur intégré au jeu et abonnements Workshop automatiques. Installer manuellement les cartes nécessaires via Steam.

Les quatorze hooks du socle commun ont été confirmés dans les deux modules lors des essais. Ce nombre ne signifie pas qu’il existe quatorze protections indépendantes ni que tous les exploits sont couverts. Les protections complètes de lobby, les autres corrections amont et leur validation restent à poursuivre.

Le mot de passe réseau doit être identique chez les participants. Il ne s’agit ni d’un mot de passe Steam, ni d’un chiffrement général, ni d’une garantie contre le piratage de compte. Effacer ce mot de passe avant de revenir au matchmaking public.

Ne pas présenter cette application comme le T7 Patch complet. Ne pas ajouter à l’aveugle une autre DLL T7 Patch : des hooks peuvent entrer en conflit. Consulter `audit-securite-t7.md` pour le détail des éléments présents et manquants.

## Tests et limites

Compilations native et WPF réussies ; tests de quantité et d’échec d’installation des hooks réussis. Chargement, quatorze hooks, rendu, ouverture/fermeture et contrôle bref de communication vérifiés séparément dans BO3 pour chaque module.

Les essais historiques de pseudo et de mot de passe ne sont pas des tests de sécurité exhaustifs. Aucun test offensif, aucune dépense et aucun farm n’ont été lancés pendant cette étape. La stabilité prolongée, les échanges entre deux clients et le blocage de chaque famille d’attaque restent à valider. Les exécutables ne sont pas signés.

## Crédits et sources

Personnalisation : STARZISMIK. Base des menus : Scroptss / Scropts-QOL. Protections : travaux T7 Patch de shiversoftdev / Serious et adaptations Scroptss. Contributions : InsaneCallum et contributeurs amont. Composants : Dear ImGui, MinHook, Detours et autres composants accompagnés de leurs notices.

L’archive `sources-divinx-etape-securite-1.zip` rassemble les sources et ressources réconciliées de cette étape ; le dossier GitHub initial ne contient pas automatiquement les dernières modifications de cette tâche.
