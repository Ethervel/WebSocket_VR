# Historique de Développement - WebSocket VR Meeting Room

Ce document retrace les étapes de développement du projet VR Multiplayer Meeting Room, basé sur l'historique des commits Git.

---

## Table des Matières

1. [Phase 1 : Initialisation du Projet](#phase-1--initialisation-du-projet)
2. [Phase 2 : Infrastructure Réseau WebSocket](#phase-2--infrastructure-réseau-websocket)
3. [Phase 3 : Système de Rooms Multiplayer](#phase-3--système-de-rooms-multiplayer)
4. [Phase 4 : Contrôleurs VR et Spawning](#phase-4--contrôleurs-vr-et-spawning)
5. [Phase 5 : Architecture des Scènes](#phase-5--architecture-des-scènes)
6. [Phase 6 : Environnement et Lobby](#phase-6--environnement-et-lobby)
7. [Phase 7 : Interface Utilisateur VR](#phase-7--interface-utilisateur-vr)
8. [Phase 8 : Téléportation et Navigation](#phase-8--téléportation-et-navigation)
9. [Phase 9 : Salles de Réunion](#phase-9--salles-de-réunion)
10. [Phase 10 : Voice Chat WebRTC](#phase-10--voice-chat-webrtc)
11. [Phase 11 : Whiteboard Collaboratif](#phase-11--whiteboard-collaboratif)
12. [Phase 12 : Mode Desktop](#phase-12--mode-desktop)
13. [Phase 13 : Partage d'Écran et Fichiers](#phase-13--partage-décran-et-fichiers)
14. [Phase 14 : Personnalisation Avatar](#phase-14--personnalisation-avatar)
15. [Phase 15 : Menu In-Game VR](#phase-15--menu-in-game-vr)
16. [Phase 16 : Laser Pointer](#phase-16--laser-pointer)
17. [Phase 17 : Menu Principal et Options](#phase-17--menu-principal-et-options)
18. [Phase 18 : Système Audio](#phase-18--système-audio)
19. [Phase 19 : Système d'Enregistrement](#phase-19--système-denregistrement)
20. [Phase 20 : Authentification](#phase-20--authentification)
21. [Phase 21 : Finitions et Polish](#phase-21--finitions-et-polish)

---

## Phase 1 : Initialisation du Projet

**Commits associés :**
- `8074408` Initial commit
- `7b7786f` Initialize Unity
- `b893be0` feat : Project Settings
- `d3516e4` feat : add package Xr interaction toolkit
- `00edf2b` feat: Add folders

**Description :**
Création du projet Unity 6000.2.14f1 avec la configuration initiale pour la VR. Installation du package XR Interaction Toolkit qui servira de base pour toutes les interactions VR.

**Éléments clés :**
- Création du repository Git
- Configuration des Project Settings Unity
- Installation de XR Interaction Toolkit
- Mise en place de la structure de dossiers

**Capture d'écran suggérée :**
> [Image : Structure initiale du projet dans Unity]

![Structure initiale](images/phase1_project_structure.png)

---

## Phase 2 : Infrastructure Réseau WebSocket

**Commits associés :**
- `074f156` feat : add NativeWebsocket
- `db0682d` feat : Create Network manager for the app
- `4ceba7a` feat : Implement VR WebSocket network manager with singleton lifecycle, auto-reconnect, message routing and event-based API

**Description :**
Mise en place de l'architecture réseau basée sur WebSocket. Création du `VRNetworkManager` avec :
- Pattern Singleton pour persistance
- Auto-reconnexion en cas de déconnexion
- Système de routage des messages
- API basée sur des événements (OnConnected, OnDisconnected, etc.)

**Fichiers créés :**
- `Assets/Scrips/Network/VRNetworkManager.cs`

**Architecture :**
```
Unity Client ←──WebSocket──→ Node.js Server
                 JSON
```

**Capture d'écran suggérée :**
> [Image : Diagramme de l'architecture réseau]

![Architecture réseau](images/phase2_network_architecture.png)

---

## Phase 3 : Système de Rooms Multiplayer

**Commits associés :**
- `1b0b14d` feat: Add VR multiplayer room system with WebSocket support

**Description :**
Implémentation du système de rooms permettant aux joueurs de rejoindre différentes salles de réunion. Chaque room est identifiée par un code unique de 6 caractères.

**Fonctionnalités :**
- Création de rooms avec codes uniques
- Rejoindre une room existante
- Liste des rooms disponibles
- Gestion de l'hôte (host authority)

**Fichiers créés :**
- `Assets/Scrips/Network/VRRoomManager.cs`

**Capture d'écran suggérée :**
> [Image : Interface de création/rejoindre une room]

![Système de rooms](images/phase3_room_system.png)

---

## Phase 4 : Contrôleurs VR et Spawning

**Commits associés :**
- `b67ae3e` feat: Add VR player spawning and real-time avatar synchronization
- `be40e99` feat: Add VR and desktop player controllers
- `e5e3bb6` feat : add remoteplayer prefab
- `ba73f47` fix: remote player hand
- `25c2164` Fix : fix remote player position

**Description :**
Création des contrôleurs pour les joueurs VR et Desktop. Système de spawn des joueurs locaux et distants avec synchronisation en temps réel des positions (tête + mains).

**Fichiers créés :**
- `Assets/Scrips/VR/VRPlayerController.cs`
- `Assets/Scrips/VR/DesktopPlayerController.cs`
- `Assets/Scrips/Network/VRGameManager.cs`

**Prefabs créés :**
- `LocalPlayer` - Joueur local avec XR Origin
- `RemoteVRPlayer` - Représentation des autres joueurs

**Synchronisation (30Hz) :**
```
Local Player → Position/Rotation (Head + Hands) → Server → Remote Players
```

**Capture d'écran suggérée :**
> [Image : Prefabs LocalPlayer et RemoteVRPlayer dans l'inspecteur]

![Player Controllers](images/phase4_player_controllers.png)

---

## Phase 5 : Architecture des Scènes

**Commits associés :**
- `90b4acd` fix : change scene name (SimpleScene-> Meet)
- `fbaa131` feat : creation of the Bootstrap scene with managers

**Description :**
Mise en place de l'architecture à deux scènes :
- **Bootstrap** : Scène persistante contenant tous les managers (DontDestroyOnLoad)
- **Meet** : Scène de jeu chargée additivement

**Flow :**
```
Bootstrap (Index 0) → Chargement additif → Meet (Index 1)
     ↓
  Managers persistants
  (Network, Room, Game, Audio, etc.)
```

**Capture d'écran suggérée :**
> [Image : Hiérarchie de la scène Bootstrap avec les managers]

![Architecture scènes](images/phase5_scene_architecture.png)

---

## Phase 6 : Environnement et Lobby

**Commits associés :**
- `d4a4981` feat : add sky cubemap, add lobby prefab, add xr interaction toolkit samples
- `b3e3725` feat: add xr interaction simulator
- `c82dbeb` feat : add lobby spawn point
- `b32f063` feat : add lobby mesh prefab
- `680ab64` fix : add table to the lobby and fix some environment position

**Description :**
Création de l'environnement du lobby, point d'entrée des joueurs. Ajout du skybox, des meshes de décor, et des points de spawn.

**Éléments ajoutés :**
- Skybox cubemap
- Prefab du lobby avec mobilier
- Points de spawn pour les joueurs
- XR Interaction Simulator pour tests sans casque

**Capture d'écran suggérée :**
> [Image : Vue du lobby dans Unity]

![Lobby](images/phase6_lobby.png)

---

## Phase 7 : Interface Utilisateur VR

**Commits associés :**
- `cb0d6f2` feat : add lobby main UI
- `a4cba39` feat: Add VR multiplayer room UI with three panels
- `f0fbb53` feat : add room ui Panel
- `872ffe6` Fix : MAKE UI WORK
- `f3c0cb3` feat : made ui vr interactable

**Description :**
Création des interfaces utilisateur interactables en VR :
- Panel de création de room
- Panel pour rejoindre une room
- Panel de liste des rooms
- UI in-room avec informations

**Composants utilisés :**
- Canvas World Space
- TrackedDeviceGraphicRaycaster
- XR UI Input Module

**Capture d'écran suggérée :**
> [Image : Les différents panels UI dans le lobby]

![UI VR](images/phase7_vr_ui.png)

---

## Phase 8 : Téléportation et Navigation

**Commits associés :**
- `706f780` feat : add Teleportation
- `3728250` feat : add spatial keyboard
- `be7cb0d` feat : add teleport on grab Script
- `1a0adeb` feat : add anchor lobby to room and room to lobby
- `8147675` feat : add Teleportation Anchor

**Description :**
Implémentation du système de téléportation VR pour la navigation :
- Téléportation libre sur les surfaces autorisées
- Anchors de téléportation entre les salles
- Clavier spatial pour la saisie de texte

**Configuration :**
- Teleport Layer : bit 31
- Teleportation Anchor pour les portes entre salles

**Capture d'écran suggérée :**
> [Image : Système de téléportation avec les anchors visibles]

![Téléportation](images/phase8_teleportation.png)

---

## Phase 9 : Salles de Réunion

**Commits associés :**
- `e4f572e` feat : add room A and B
- `85b7812` feat : inroom ui room name show
- `efd8d86` add door that disappear at room creation

**Description :**
Création des deux salles de réunion (Meeting Room A et B) avec :
- Environnements distincts
- Affichage du nom de la room
- Portes qui disparaissent lors de la création de room
- Téléportation entre lobby et salles

**Types de rooms :**
```csharp
enum RoomType { Lobby, MeetingRoomA, MeetingRoomB }
```

**Capture d'écran suggérée :**
> [Image : Vue des deux salles de réunion]

![Salles de réunion](images/phase9_meeting_rooms.png)

---

## Phase 10 : Voice Chat WebRTC

**Commits associés :**
- `323bf05` feat : add webRTC
- `d7a8060` feat: add voice chat ui
- `205eb3f` fix: webrtc audio mesh configuration

**Description :**
Implémentation du chat vocal en temps réel via WebRTC :
- Topologie mesh (connexion directe entre pairs)
- Audio spatial 3D positionné sur la tête des avatars
- Push-to-talk (touche V)
- Configuration STUN/TURN pour NAT traversal

**Fichiers créés :**
- `Assets/Scrips/WebRTC/VoiceChatManager.cs`
- `Assets/Scrips/WebRTC/WebRTCPeerManager.cs`
- `Assets/Scrips/WebRTC/MicrophoneManager.cs`

**Architecture :**
```
Player A ←──WebRTC P2P──→ Player B
              ↑
    Signaling via WebSocket
```

**Capture d'écran suggérée :**
> [Image : UI du voice chat et indicateur de parole]

![Voice Chat](images/phase10_voice_chat.png)

---

## Phase 11 : Whiteboard Collaboratif

**Commits associés :**
- `82b8722` feat: Add collaborative whiteboard manager with real-time sync
- `60e9d81` feat : add a whiteboard test scene
- `938641c` feat : add working whiteboard in the test scene
- `01f6669` feat : add whiteboard fbx
- `3ac3e22` fix : sync whiteboard drawing
- `8e05503` feat : sync whiteboard
- `812e32c` feat : add whiteboard eraser

**Description :**
Système de tableau blanc collaboratif avec synchronisation en temps réel entre tous les participants.

**Architecture 3 couches :**
1. `Whiteboard.cs` - Fond blanc + mode présentation
2. `WhiteboardDrawingSurface.cs` - Surface transparente, réseau uniquement
3. `WhiteboardMarker.cs` (VR) / `DesktopWhiteboardDrawer.cs` (Desktop) - Dessin local

**Configuration :**
- Résolution : 2048x2048
- Shader : Sprites/Default
- Taux d'envoi : 33ms
- Couleur par défaut : Bleu

**Capture d'écran suggérée :**
> [Image : Whiteboard avec dessins synchronisés entre plusieurs utilisateurs]

![Whiteboard](images/phase11_whiteboard.png)

---

## Phase 12 : Mode Desktop

**Commits associés :**
- `55ce8f6` feat : add desktop mode
- `845f798` UI fix for desktop mode

**Description :**
Ajout du support pour les utilisateurs sans casque VR :
- Contrôles clavier/souris (WASD + souris)
- Caméra à la première personne
- Interaction avec le whiteboard au clic
- Même fonctionnalités que le mode VR

**Contrôles Desktop :**
| Action | Touche |
|--------|--------|
| Mouvement | WASD |
| Sprint | Shift |
| Regarder | Clic droit + souris |
| Dessiner | Clic gauche |
| Laser | L |

**Fichiers créés :**
- `Assets/Scrips/VR/DesktopPlayerController.cs`
- `Assets/Scrips/WhiteBoard/DesktopWhiteboardDrawer.cs`

**Capture d'écran suggérée :**
> [Image : Vue du mode Desktop avec l'interface]

![Mode Desktop](images/phase12_desktop_mode.png)

---

## Phase 13 : Partage d'Écran et Fichiers

**Commits associés :**
- `0a5807c` feat : File and screen share script
- `5957e3e` feat : add screen sharing and make the whiteboard able to draw on it
- `416ae80` feat : add ui for screen share
- `f72cb64` feat : file sharing button
- `06b7cdb` fix : add correct file presentation
- `eb8827f` feat : add presentation feature

**Description :**
Système complet de partage de contenu :

**Screen Share :**
- Capture d'écran : 854x480 @ 3fps
- Compression JPEG 50%
- Affichage sur le whiteboard en mode présentation

**File Share :**
- Limite : 10MB max
- Extensions supportées : pdf, doc, docx, xls, xlsx, png, jpg, jpeg, gif
- Mode présentation avec navigation

**Fichiers créés :**
- `Assets/Scrips/Sharing/ScreenShareManager.cs`
- `Assets/Scrips/Sharing/FileShareManager.cs`
- `Assets/Scrips/Sharing/FilePresentationManager.cs`

**Capture d'écran suggérée :**
> [Image : Partage d'écran affiché sur le whiteboard]

![Partage](images/phase13_sharing.png)

---

## Phase 14 : Personnalisation Avatar

**Commits associés :**
- `d506676` feat : avatar and name customization and add nametag on remoteplayer
- `95f4d16` add new avatar head and hand

**Description :**
Système de personnalisation des avatars :
- Choix de couleur pour la tête et les mains
- Nom personnalisé affiché au-dessus de l'avatar
- Synchronisation des paramètres entre tous les joueurs

**Fichiers créés :**
- `Assets/Scrips/Avatar/AvatarCustomization.cs`
- `Assets/Scrips/Avatar/AvatarColorTarget.cs`

**Capture d'écran suggérée :**
> [Image : Interface de personnalisation d'avatar]

![Avatar](images/phase14_avatar.png)

---

## Phase 15 : Menu In-Game VR

**Commits associés :**
- `8c9ec0b` feat : add in game menu
- `50bd723` fix : menu play now appear
- `046a0de` update vr menu
- `52c046c` vrmenu ui fix (add korean)

**Description :**
Menu accessible en jeu pour les utilisateurs VR :
- Toggle via bouton menu du contrôleur
- Options de room (quitter, téléporter)
- Contrôles audio (mute micro, volume)
- Support multilingue (anglais, coréen)

**Fichiers créés :**
- `Assets/Scrips/UI/Menu/VRMenuUI.cs`
- `Assets/Scrips/UI/Menu/VRMenuToggle.cs`

**Capture d'écran suggérée :**
> [Image : Menu VR ouvert devant le joueur]

![VR Menu](images/phase15_vr_menu.png)

---

## Phase 16 : Laser Pointer

**Commits associés :**
- `caec5b0` in room change and snap turn
- `d0b2aa5` add laserpointer and eraser fix

**Description :**
Pointeur laser pour les présentations :
- VR : Bouton A pour activer
- Desktop : Touche L
- Synchronisation à 10Hz entre tous les clients
- Visuel : LineRenderer rouge + point

**Fichiers créés :**
- `Assets/Scrips/Interaction/LaserPointer.cs`
- `Assets/Scrips/Interaction/LaserPointerData.cs`

**Capture d'écran suggérée :**
> [Image : Laser pointer pointant sur le whiteboard]

![Laser Pointer](images/phase16_laser_pointer.png)

---

## Phase 17 : Menu Principal et Options

**Commits associés :**
- `8b1f6ae` add main menu (to finish (the options doesn't work)), and loading screen
- `0611f4b` add option functionalities on the main menu
- `24f5af8` change font of some text on the main menu UI

**Description :**
Menu principal au lancement de l'application :
- Bouton Start pour lancer le jeu
- Options complètes (audio, graphiques, VR, contrôles)
- Écran de chargement avec progression

**Options disponibles :**
- **Audio** : Volume master, volume voix, micro
- **Graphiques** : Qualité, résolution, fullscreen
- **VR** : Mode rotation (snap/smooth), angles
- **Desktop** : Sensibilité souris, inversion Y

**Fichiers créés :**
- `Assets/Scrips/UI/MainMenu/MainMenuManager.cs`
- `Assets/Scrips/UI/MainMenu/MainMenuSettings.cs`
- `Assets/Scrips/UI/MainMenu/MainMenuOptionsUI.cs`

**Capture d'écran suggérée :**
> [Image : Menu principal avec les options]

![Main Menu](images/phase17_main_menu.png)

---

## Phase 18 : Système Audio

**Commits associés :**
- `79e59b2` add audio effect
- `1207571` move audio manager to bootstrap and create some ambient mute zone
- `fde44de` add mutezone for the other room

**Description :**
Système audio complet :
- SoundManager centralisé dans Bootstrap
- Effets sonores pour les interactions
- Zones de mute pour isoler les salles
- Ambiance sonore par zone

**Fichiers créés :**
- `Assets/Scrips/Audio/SoundManager.cs`
- `Assets/Scrips/Audio/AudioMuteZone.cs`
- `Assets/Scrips/Audio/AmbienceManager.cs`

**Capture d'écran suggérée :**
> [Image : Configuration des AudioMuteZones dans la scène]

![Audio System](images/phase18_audio.png)

---

## Phase 19 : Système d'Enregistrement

**Commits associés :**
- `525baae` update for recording and add server documentation
- `3b8db9b` little fix for the capture of the frames during recording
- `3885a3a` update project memo, make recording mode more fluid to avoid motion sickness

**Description :**
Système d'enregistrement optimisé pour la VR (évite le motion sickness) :

**Architecture pipeline 3 étapes :**
```
Main Thread          Encode Thread       Write Thread
RequestFrame() ──▶  RGB → TGA ──────▶  File.Write()
  (~0.1ms)          (background)        (background)
     ↑
AsyncGPUReadback (lecture GPU non-bloquante)
```

**Fichiers créés :**
- `Assets/Scrips/Recording/RecordingManager.cs`
- `Assets/Scrips/Recording/SpectatorCameraController.cs`
- `Assets/Scrips/Recording/FFmpegEncoder.cs`
- `Assets/Scrips/Recording/AudioCapture.cs`

**Configuration :**
- Résolution : 1920x1080 @ 30fps
- Format : TGA frames → FFmpeg → MP4
- Markers : Important, Question, Todo, Idea

**Capture d'écran suggérée :**
> [Image : Interface d'enregistrement avec les contrôles]

![Recording](images/phase19_recording.png)

---

## Phase 20 : Authentification

**Commits associés :**
- `2f3fd77` auth code
- `f55b8f3` add auth UI
- `2e530c3` change menu order, before: auth -> main menu, after: main menu -> auth

**Description :**
Système d'authentification (implémenté mais pas encore intégré aux fonctionnalités) :
- Login / Register / Mode invité
- Hachage bcrypt (12 rounds)
- Tokens JWT (24h)
- Rate limiting (5 tentatives/min)

**Flow actuel :**
```
Main Menu → [Start] → Auth Screen → [Login/Register/Guest] → Loading → Meet
```

**Fichiers créés :**
- `Assets/Scrips/Auth/AuthManager.cs`
- `Assets/Scrips/Auth/AuthUI.cs`
- `Server/src/auth.js`
- `Server/src/database.js`

**Capture d'écran suggérée :**
> [Image : Écran d'authentification avec les options]

![Auth](images/phase20_auth.png)

---

## Phase 21 : Finitions et Polish

**Commits associés :**
- `db163f0` code optimization
- `33510b1` add singleton pattern at DesktopWhiteboardDrawer
- `2dfe067` menu and transition update
- `7c13222` lobby update

**Description :**
Phase finale d'optimisation et de polish :
- Optimisation du code (caching, batch processing)
- Corrections de bugs divers
- Améliorations UI/UX
- Mises à jour des transitions entre scènes

**Optimisations notables :**
- Batch Apply() sur whiteboard : ~30ms → ~1ms/frame
- Cache FindObjects : O(n) → O(1) lookups
- Suppression allocations GC dans les boucles

**Capture d'écran suggérée :**
> [Image : Version finale de l'application]

![Final](images/phase21_final.png)

---

## Résumé des Technologies Utilisées

| Catégorie | Technologie |
|-----------|-------------|
| Moteur | Unity 6000.2.14f1 |
| VR SDK | OpenXR + XR Interaction Toolkit |
| Réseau | WebSocket (NativeWebSocket) |
| Voice | WebRTC (Unity WebRTC 3.0) |
| Backend | Node.js + Express |
| Database | MariaDB |
| Auth | bcrypt + JWT |
| Rendering | URP 17.2.0 |

---

## Statistiques du Projet

- **Nombre total de commits** : ~130
- **Nombre de Pull Requests** : 23
- **Branches principales** : main, develop
- **Plateformes supportées** : Quest, PCVR, Desktop

---

## Instructions pour Ajouter des Screenshots

1. Créer un dossier `Documentation/images/`
2. Nommer les images selon le format : `phaseX_description.png`
3. Les images seront automatiquement liées dans ce document

**Exemple de structure :**
```
Documentation/
├── HISTORIQUE_DEVELOPPEMENT.md
└── images/
    ├── phase1_project_structure.png
    ├── phase2_network_architecture.png
    ├── phase3_room_system.png
    └── ...
```

---

*Document généré à partir de l'historique Git du projet*
