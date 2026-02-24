# Liste des Scripts du Projet VR Meeting Room

**Total : 91 scripts C#** (dont 7 scripts Editor)

---

## Audio/ (6 scripts)

| # | Script | Description |
|---|--------|-------------|
| 1 | `SoundManager.cs` | Gestionnaire principal des sons (singleton) |
| 2 | `UIButtonSounds.cs` | Sons des boutons UI (hover, click) |
| 3 | `SoundManagerIntegration.cs` | Intégration automatique du système audio |
| 4 | `AmbienceManager.cs` | Gestionnaire d'ambiance sonore |
| 5 | `AudioMuteZone.cs` | Zones de mute audio spatial |
| 6 | `Editor/SoundManagerSetup.cs` | Outil de setup éditeur |

---

## Auth/ (3 scripts)

| # | Script | Description |
|---|--------|-------------|
| 7 | `AuthManager.cs` | Gestionnaire d'authentification (login, register, JWT) |
| 8 | `AuthUI.cs` | Interface utilisateur login/register/guest |
| 9 | `Editor/AuthUICreator.cs` | Créateur d'UI éditeur |

---

## Avatar/ (2 scripts)

| # | Script | Description |
|---|--------|-------------|
| 10 | `AvatarCustomization.cs` | Personnalisation d'avatar (couleurs, apparence) |
| 11 | `AvatarColorTarget.cs` | Cibles de couleur sur les parties de l'avatar |

---

## Debug/ (3 scripts)

| # | Script | Description |
|---|--------|-------------|
| 12 | `DebugManager.cs` | Gestionnaire de debug avec catégories |
| 13 | `XRDebugOverlay.cs` | Overlay de debug pour XR |
| 14 | `Editor/DebugManagerWindow.cs` | Fenêtre éditeur pour le debug |

---

## Desktop/ (1 script)

| # | Script | Description |
|---|--------|-------------|
| 15 | `DesktopPlayerController.cs` | Contrôleur joueur mode desktop (WASD, souris) |

---

## Effects/ (1 script)

| # | Script | Description |
|---|--------|-------------|
| 16 | `GlowingLight.cs` | Effet de lumière brillante/pulsante |

---

## Interaction/ (2 scripts)

| # | Script | Description |
|---|--------|-------------|
| 17 | `LaserPointer.cs` | Pointeur laser VR/Desktop avec sync réseau |
| 18 | `LaserPointerData.cs` | Structures de données pour le laser |

---

## Network/ (3 scripts)

| # | Script | Description |
|---|--------|-------------|
| 19 | `VRNetworkManager.cs` | Gestionnaire WebSocket principal (connexion, messages) |
| 20 | `VRRoomManager.cs` | Gestionnaire de salles (create, join, leave) |
| 21 | `VRGameManager.cs` | Gestionnaire de jeu (spawn local/remote players) |

---

## Recording/ (6 scripts)

| # | Script | Description |
|---|--------|-------------|
| 22 | `RecordingManager.cs` | Orchestration pipeline d'enregistrement |
| 23 | `RecordingData.cs` | Settings, metadata, markers pour l'enregistrement |
| 24 | `SpectatorCameraController.cs` | Caméra spectateur avec AsyncGPUReadback |
| 25 | `FFmpegEncoder.cs` | Encodeur TGA → MP4 via FFmpeg |
| 26 | `AudioCapture.cs` | Capture audio pour l'enregistrement |
| 27 | `RecordingTestHelper.cs` | Helper de test pour le recording |

---

## Sharing/ (7 scripts)

| # | Script | Description |
|---|--------|-------------|
| 28 | `ScreenShareManager.cs` | Partage d'écran (capture, envoi, affichage) |
| 29 | `ScreenShareData.cs` | Structures de données partage écran |
| 30 | `FileShareManager.cs` | Partage de fichiers (upload, download) |
| 31 | `FileShareData.cs` | Structures de données partage fichiers |
| 32 | `FilePresentationManager.cs` | Gestionnaire de présentation de fichiers |
| 33 | `FilePresentationData.cs` | Structures de données présentation |
| 34 | `WindowCapture.cs` | Capture de fenêtre Windows |

---

## Testing/ (1 script)

| # | Script | Description |
|---|--------|-------------|
| 35 | `VRNetworkedInteractable.cs` | Objet interactable synchronisé réseau |

---

## UI/ (13 scripts)

| # | Script | Description |
|---|--------|-------------|
| 36 | `VRMenuUi.cs` | Menu VR principal (pages, navigation) |
| 37 | `VRMenuToggle.cs` | Toggle d'ouverture/fermeture du menu VR |
| 38 | `VRMenuSidebar.cs` | Sidebar de navigation du menu |
| 39 | `VRMenuUISetup.cs` | Setup automatique du menu UI |
| 40 | `VRMenuCloseButton.cs` | Bouton de fermeture du menu |
| 41 | `VRFileBrowser.cs` | Explorateur de fichiers VR |
| 42 | `VRFollowMenu.cs` | Menu qui suit le joueur en VR |
| 43 | `FileSharingUI.cs` | UI de partage de fichiers |
| 44 | `FileShareUISetup.cs` | Setup UI partage fichiers |
| 45 | `FilePresentationUI.cs` | UI de présentation de fichiers |
| 46 | `VoiceChatUI.cs` | UI du chat vocal (indicateurs, mute) |
| 47 | `GlobalKeyboardAutoBind.cs` | Auto-bind clavier global |
| 48 | `VRCanvasAdapter.cs` | Adaptateur Canvas Screen→World Space pour VR |
| 49 | `LaunchLoadingScreen.cs` | Ecran de chargement au lancement |

---

## UI/MainMenu/ (5 scripts)

| # | Script | Description |
|---|--------|-------------|
| 50 | `MainMenuManager.cs` | Gestionnaire menu principal (start, quit, auth) |
| 51 | `MainMenuSettings.cs` | Paramètres persistants (audio, graphiques, VR) |
| 52 | `MainMenuOptionsUI.cs` | UI des options/paramètres |
| 53 | `Editor/MainMenuUISetup.cs` | Setup éditeur du menu |
| 54 | `Editor/PlayFromBootstrap.cs` | Lancer le jeu depuis Bootstrap |

---

## UI/Menu/ (6 scripts)

| # | Script | Description |
|---|--------|-------------|
| 55 | `VRMenuPageRoom.cs` | Page salle (téléportation, infos room) |
| 56 | `VRMenuPageAvatar.cs` | Page avatar (personnalisation) |
| 57 | `VRMenuPageSettings.cs` | Page paramètres in-game |
| 58 | `VRMenuPageVoice.cs` | Page chat vocal (micro, volume) |
| 59 | `VRMenuPageRecording.cs` | Page enregistrement (start, stop, markers) |
| 60 | `VRMenuExitDialog.cs` | Dialogue de confirmation de sortie |

---

## Utils/ (2 scripts)

| # | Script | Description |
|---|--------|-------------|
| 61 | `TransformUtility.cs` | Utilitaires pour les transforms |
| 62 | `JsonHelper.cs` | Helper pour sérialisation JSON Unity |

---

## VR/ (10 scripts)

| # | Script | Description |
|---|--------|-------------|
| 63 | `BootstrapManager.cs` | Gestionnaire de démarrage (init XR, singletons) |
| 64 | `VRPlayerController.cs` | Contrôleur joueur VR (mouvement, téléport) |
| 65 | `TeleportOnButtonClick.cs` | Téléportation sur clic de bouton UI |
| 66 | `TeleportOnGrab.cs` | Téléportation sur grab d'objet |
| 67 | `ControllerInputFix.cs` | Fix pour les inputs manettes |
| 68 | `ControllerTrackingFix.cs` | Fix pour le tracking des manettes |
| 69 | `ControllerModelLoader.cs` | Chargeur de modèles 3D des manettes |
| 70 | `VRTrackingFix.cs` | Fix général pour le tracking VR |
| 71 | `XRUIInteractionBridge.cs` | Bridge pour interaction UI en XR |
| 72 | `XRInteractorInputBridge.cs` | Bridge pour input des interactors XR |

---

## WebRTC/ (7 scripts)

| # | Script | Description |
|---|--------|-------------|
| 73 | `VoiceChatManager.cs` | Gestionnaire principal chat vocal |
| 74 | `WebRTCPeerManager.cs` | Gestionnaire des connexions peer-to-peer |
| 75 | `WebRTCSignaling.cs` | Signaling WebRTC (offer, answer, ICE) |
| 76 | `WebRTCConfiguration.cs` | Configuration STUN/TURN |
| 77 | `MicrophoneManager.cs` | Gestionnaire microphone (capture, push-to-talk) |
| 78 | `RemoteAudioManager.cs` | Audio distant (spatial audio sur head) |
| 79 | `VoiceChatData.cs` | Structures de données chat vocal |

---

## WhiteBoard/ (12 scripts)

| # | Script | Description |
|---|--------|-------------|
| 80 | `Whiteboard.cs` | Tableau blanc principal (fond, mode présentation) |
| 81 | `WhiteboardDrawingSurface.cs` | Surface de dessin (réseau uniquement) |
| 82 | `WhiteboardMarker.cs` | Marqueur VR pour dessiner |
| 83 | `DesktopWhiteboardDrawer.cs` | Dessin desktop (clic souris) |
| 84 | `WhiteboardEraser.cs` | Gomme pour le whiteboard |
| 85 | `WhiteboardBarUI.cs` | Barre d'outils UI du whiteboard |
| 86 | `WhiteboardUIHelper.cs` | Helper UI whiteboard |
| 87 | `WhiteboardUIManager.cs` | Gestionnaire UI whiteboard |
| 88 | `WhiteboardUISetup.cs` | Setup UI whiteboard |
| 89 | `WhiteboardNetworkData.cs` | Structures de données réseau |
| 90 | `Editor/WhiteboardSetupTool.cs` | Outil de setup éditeur |
| 91 | `Editor/WhiteboardBarUISetup.cs` | Setup barre UI éditeur |

---

## Résumé par Catégorie

| Catégorie | Nombre de scripts |
|-----------|-------------------|
| Audio | 6 |
| Auth | 3 |
| Avatar | 2 |
| Debug | 3 |
| Desktop | 1 |
| Effects | 1 |
| Interaction | 2 |
| Network | 3 |
| Recording | 6 |
| Sharing | 7 |
| Testing | 1 |
| UI | 13 |
| UI/MainMenu | 5 |
| UI/Menu | 6 |
| Utils | 2 |
| VR | 10 |
| WebRTC | 7 |
| WhiteBoard | 12 |
| **TOTAL** | **91** |

---

## Scripts Editor (7)

Ces scripts ne fonctionnent que dans l'éditeur Unity :

1. `Audio/Editor/SoundManagerSetup.cs`
2. `Auth/Editor/AuthUICreator.cs`
3. `Debug/Editor/DebugManagerWindow.cs`
4. `UI/MainMenu/Editor/MainMenuUISetup.cs`
5. `UI/MainMenu/Editor/PlayFromBootstrap.cs`
6. `WhiteBoard/Editor/WhiteboardSetupTool.cs`
7. `WhiteBoard/Editor/WhiteboardBarUISetup.cs`

---

*Généré le 24/02/2026*
