# Rapport de Projet - VRMeet
## Application de Salles de Reunion Virtuelles Multiplayer

**Auteur:** [Votre Nom]
**Organisation:** Rndp
**Date:** Fevrier 2026

---

# Table des Matieres

2. [Analyse et Choix Techniques](#2-analyse-et-choix-techniques)
3. [Architecture du Projet](#3-architecture-du-projet)
4. [Methodologie de Developpement](#4-methodologie-de-developpement)
5. [Realisation Technique](#5-realisation-technique)
6. [Difficultes Rencontrees et Solutions](#6-difficultes-rencontrees-et-solutions)
7. [Resultats et Demonstration](#7-resultats-et-demonstration)
8. [Perspectives et Ameliorations](#8-perspectives-et-ameliorations)
9. [Conclusion](#9-conclusion)

---



# 2. Analyse et Choix Techniques

## 2.1 Choix du Moteur de Jeu: Unity

### Analyse des Options

| Moteur | Avantages | Inconvenients |
|--------|-----------|---------------|
| **Unity** | Large ecosysteme VR, C#, cross-platform | Licence payante pour gros projets |
| Unreal Engine | Graphismes superieurs, C++ | Courbe d'apprentissage, lourd |
| Godot | Open source, leger | Ecosysteme VR limite |

### Justification du Choix

**Unity 6000.2.14f1** a ete choisi pour:

1. **Ecosysteme VR mature** - XR Interaction Toolkit officiel, support OpenXR natif
2. **Cross-platform** - Build unique pour Quest, PCVR, Desktop
3. **WebRTC integre** - Package `com.unity.webrtc` officiel
4. **Communaute active** - Documentation abondante, solutions aux problemes courants
5. **Langage C#** - Productivite elevee, typage fort, async/await

## 2.2 Choix du Protocole Reseau: WebSocket

### Analyse des Options

| Protocole | Latence | Fiabilite | Complexite |
|-----------|---------|-----------|------------|
| **WebSocket** | Faible | TCP garanti | Moyenne |
| UDP brut | Tres faible | Non garanti | Elevee |
| HTTP Polling | Elevee | Garanti | Faible |
| WebRTC DataChannel | Faible | Configurable | Elevee |

### Justification du Choix

**WebSocket** a ete choisi pour:

1. **Bidirectionnel** - Communication client-serveur dans les deux sens
2. **Persistant** - Connexion maintenue, pas de reconnexion a chaque message
3. **Compatibilite** - Fonctionne sur toutes les plateformes sans configuration
4. **Fiabilite TCP** - Messages garantis dans l'ordre (important pour la synchronisation)
5. **Simplicite** - Implementation plus rapide qu'UDP avec gestion de paquets

> **Note:** Pour la voix, WebRTC (UDP) est utilise car la perte de quelques paquets audio est acceptable, mais pas la latence.

## 2.3 Choix pour la Communication Vocale: WebRTC

### Raisonnement

La voix necessite:
- **Faible latence** (< 150ms pour conversation naturelle)
- **Tolerance aux pertes** (quelques paquets perdus = acceptable)
- **Peer-to-peer** (reduire charge serveur)

**WebRTC** repond parfaitement a ces criteres:
- Protocole concu pour l'audio/video temps reel
- Connexions P2P directes entre clients
- Codecs audio optimises (Opus)
- Traversee NAT avec STUN/TURN

## 2.4 Architecture Client-Serveur vs P2P

### Choix: Hybride

```
┌─────────────────────────────────────────────────────┐
│                    ARCHITECTURE                      │
├─────────────────────────────────────────────────────┤
│                                                      │
│   WebSocket (via Serveur)     WebRTC (P2P Direct)   │
│   ━━━━━━━━━━━━━━━━━━━━━       ━━━━━━━━━━━━━━━━━━    │
│   • Positions/Rotations       • Audio voix          │
│   • Gestion des rooms         • (Signaling seul     │
│   • Chat/Messages              via serveur)         │
│   • Whiteboard                                      │
│   • Partage fichiers                                │
│                                                      │
└─────────────────────────────────────────────────────┘
```

### Justification

| Donnee | Via Serveur (WebSocket) | P2P (WebRTC) |
|--------|------------------------|--------------|
| Positions | ✓ (besoin de broadcast a tous) | |
| Voix | | ✓ (latence critique) |
| Whiteboard | ✓ (persistence, late-joiners) | |
| Fichiers | ✓ (fiabilite requise) | |

## 2.5 Tableau Recapitulatif des Technologies

| Composant | Technologie | Justification |
|-----------|-------------|---------------|
| Moteur | Unity 6000.2.14f1 | Ecosysteme VR, C#, cross-platform |
| Rendu | URP 17.2.0 | Performance VR, effets modernes |
| VR Framework | XR Interaction Toolkit 3.2.2 | Standard Unity, bien documente |
| VR Runtime | OpenXR 1.16.1 | Standard ouvert, multi-casques |
| Reseau sync | NativeWebSocket | Bidirectionnel, fiable, simple |
| Voix | Unity WebRTC 3.0.0 | P2P, faible latence, audio spatial |
| Backend | Node.js + ws | Leger, async natif, npm ecosystem |
| Database | MariaDB (optionnel) | Open source, compatible MySQL |
| Auth | bcrypt + JWT | Securite standard, stateless |

---

# 3. Architecture du Projet

## 3.1 Architecture Globale

```
┌─────────────────────────────────────────────────────────────────────┐
│                         CLIENTS UNITY                                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │
│  │   Quest     │  │    PCVR     │  │   Desktop   │                 │
│  │   Client    │  │   Client    │  │   Client    │                 │
│  └──────┬──────┘  └──────┬──────┘  └──────┬──────┘                 │
│         │                │                │                         │
│         └────────────────┴────────────────┘                         │
│                          │                                          │
│              ┌───────────┴───────────┐                              │
│              │      WebSocket        │  (sync, rooms, signaling)    │
│              │      WebRTC P2P       │  (voix directe)              │
│              └───────────┬───────────┘                              │
└──────────────────────────┼──────────────────────────────────────────┘
                           │
┌──────────────────────────┼──────────────────────────────────────────┐
│                     SERVER NODE.JS                                   │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │                    WebSocket Server (ws)                     │   │
│  │  • Gestion connexions (Map clients)                          │   │
│  │  • Routage messages (broadcast, room-scoped)                 │   │
│  │  • Gestion rooms (create, join, leave)                       │   │
│  │  • Signaling WebRTC (offer/answer/ICE relay)                 │   │
│  │  • Heartbeat (detection deconnexions)                        │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                          │                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │              MariaDB (Optionnel)                             │   │
│  │  • Comptes utilisateurs                                      │   │
│  │  • Authentification                                          │   │
│  └─────────────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────────────┘
```

## 3.2 Architecture des Scenes Unity

### Raisonnement

Le projet utilise une architecture **2 scenes** pour separer les responsabilites:

```
Scene 0: Bootstrap (Persistante)
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Charge au demarrage, reste en memoire tout le temps.
Contient les singletons et managers globaux.

    ┌─────────────────────────────────────┐
    │  NetworkManager (DontDestroyOnLoad) │
    │  RoomManager                        │
    │  GameManager                        │
    │  VoiceChatManager                   │
    │  AuthManager                        │
    │  SoundManager                       │
    │  MainMenuUI                         │
    └─────────────────────────────────────┘


Scene 1: Meet (Additive)
━━━━━━━━━━━━━━━━━━━━━━━━
Chargee additivement apres connexion.
Contient l'environnement 3D et les objets de jeu.

    ┌─────────────────────────────────────┐
    │  Lobby                              │
    │  MeetingRoomA                       │
    │  MeetingRoomB                       │
    │  Whiteboards                        │
    │  SpectatorCamera                    │
    └─────────────────────────────────────┘
```

### Avantages de cette Architecture

1. **Persistence** - Les managers reseau survivent aux changements de scene
2. **Separation** - Logique (Bootstrap) vs Contenu (Meet)
3. **Chargement rapide** - Scene Meet chargee en additif, pas de reload complet
4. **Maintenance** - Modification de l'environnement sans toucher au reseau

## 3.3 Architecture Logicielle (Patterns)

### Pattern Singleton

Utilise pour les managers globaux:

```csharp
public class VRNetworkManager : MonoBehaviour
{
    public static VRNetworkManager Instance { get; private set; }

    void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
}
```

**Justification:** Un seul manager reseau doit exister. Le singleton garantit l'unicite et l'acces global.

### Pattern Observer (Evenements)

Utilise pour le decouplage des systemes:

```csharp
// Declaraton (VRNetworkManager)
public static event Action OnConnected;
public static event Action<NetworkMessage> OnMessageReceived;

// Abonnement (VRRoomManager)
void OnEnable()
{
    VRNetworkManager.OnConnected += HandleConnected;
    VRNetworkManager.OnMessageReceived += HandleMessage;
}

void OnDisable()
{
    VRNetworkManager.OnConnected -= HandleConnected;
    VRNetworkManager.OnMessageReceived -= HandleMessage;
}
```

**Justification:**
- Decouplage fort entre les systemes
- Ajout/suppression de fonctionnalites sans modifier le code existant
- Gestion propre du cycle de vie (OnEnable/OnDisable)

## 3.4 Diagramme de Flux des Donnees

```
┌─────────────────────────────────────────────────────────────────┐
│                      FLUX DE CONNEXION                           │
└─────────────────────────────────────────────────────────────────┘

Client                          Server                         Autres Clients
  │                               │                                  │
  │──── WebSocket Connect ───────>│                                  │
  │                               │                                  │
  │<─── welcome {senderId} ───────│                                  │
  │                               │─── peer-connected ──────────────>│
  │                               │                                  │
  │──── room-join {roomId} ──────>│                                  │
  │                               │─── room-join (broadcast) ───────>│
  │                               │                                  │
  │<─── room-welcome ─────────────│ (etat actuel de la room)         │
  │                               │                                  │


┌─────────────────────────────────────────────────────────────────┐
│                    FLUX DE SYNCHRONISATION VR                    │
└─────────────────────────────────────────────────────────────────┘

Client A                        Server                         Client B
  │                               │                                  │
  │ (30x par seconde)             │                                  │
  │──── vr-position ─────────────>│                                  │
  │     {head, leftHand,          │──── vr-position ────────────────>│
  │      rightHand}               │                                  │
  │                               │                                  │
  │<──── vr-position ─────────────│<─── vr-position ─────────────────│
  │                               │     (de Client B)                │


┌─────────────────────────────────────────────────────────────────┐
│                    FLUX WEBRTC VOICE                             │
└─────────────────────────────────────────────────────────────────┘

Client A                        Server                         Client B
  │                               │                                  │
  │──── webrtc-offer ────────────>│──── webrtc-offer ───────────────>│
  │                               │                                  │
  │<─── webrtc-answer ────────────│<─── webrtc-answer ───────────────│
  │                               │                                  │
  │──── webrtc-ice ──────────────>│──── webrtc-ice ─────────────────>│
  │<─── webrtc-ice ───────────────│<─── webrtc-ice ──────────────────│
  │                               │                                  │
  │<═══════════════ P2P Audio Direct ═══════════════════════════════>│
  │                 (ne passe plus par le serveur)                   │
```

---

# 4. Methodologie de Developpement

## 4.1 Approche Iterative

Le developpement a suivi une approche iterative:

```
Iteration 1: Fondations
━━━━━━━━━━━━━━━━━━━━━━━
• Setup projet Unity + structure dossiers
• Implementation WebSocket basique
• Connexion client-serveur
• Premier serveur Node.js

Iteration 2: Multiplayer de Base
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Systeme de rooms
• Synchronisation des positions
• Spawn/despawn des joueurs distants
• Interpolation des mouvements

Iteration 3: VR
━━━━━━━━━━━━━━━
• Integration XR Interaction Toolkit
• Tracking tete et mains
• Teleportation
• Controles VR

Iteration 4: Communication Vocale
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Integration WebRTC
• Signaling via WebSocket
• Audio spatial 3D
• Push-to-talk

Iteration 5: Outils Collaboratifs
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Whiteboard
• Partage d'ecran
• Partage de fichiers
• Pointeur laser

Iteration 6: Polish et Features
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
• Mode desktop
• Authentification
• Enregistrement
• UI/UX
```

## 4.2 Outils de Developpement

| Outil | Usage |
|-------|-------|
| Unity Editor | Developpement principal |
| Visual Studio / Rider | Edition C# |
| ParrelSync | Test multi-instances local |
| Git | Versioning |
| Node.js | Backend server |

## 4.3 Tests et Validation

### Tests Locaux avec ParrelSync

ParrelSync permet de cloner le projet et d'executer plusieurs instances Unity simultanement:

1. Clone du projet automatique
2. Synchronisation des modifications
3. Test multiplayer sur une seule machine

### Mode Offline

Un mode offline a ete implemente pour tester sans serveur:
- Simule la connexion
- Cree une room locale
- Permet de tester les fonctionnalites VR independamment

---

# 5. Realisation Technique

## 5.1 Systeme de Rooms

### Fonctionnement

```
┌─────────────────────────────────────────────────────────────────┐
│                      GESTION DES ROOMS                           │
└─────────────────────────────────────────────────────────────────┘

                    ┌─────────────┐
                    │   LOBBY     │
                    │  (defaut)   │
                    └──────┬──────┘
                           │
            ┌──────────────┼──────────────┐
            │              │              │
            ▼              ▼              ▼
     ┌────────────┐ ┌────────────┐ ┌────────────┐
     │ Meeting    │ │ Meeting    │ │   Autre    │
     │  Room A    │ │  Room B    │ │   Room     │
     │  (ABC123)  │ │  (XYZ789)  │ │  (code)    │
     └────────────┘ └────────────┘ └────────────┘
```

### Implementation

- **Codes de room:** 6 caracteres alphanumeriques uniques
- **Host authority:** Le createur est l'host
- **Broadcast scope:** Messages limites a la room

### Messages Reseau

```javascript
// Creation de room
{ type: "room-available", data: { roomId, roomName, roomType, maxPlayers } }

// Rejoindre une room
{ type: "room-join", data: { roomId, playerName } }

// Quitter une room
{ type: "room-leave", data: { roomId } }
```

## 5.2 Synchronisation des Joueurs

### Frequence et Optimisation

- **30 Hz** de mise a jour (30 messages/seconde)
- **Seuil de mouvement:** 0.01m / 1° (n'envoie pas si immobile)
- **Interpolation:** Lissage des mouvements recus

### Donnees Synchronisees

```csharp
public class VRPositionData
{
    public float[] headPos;      // Position tete [x, y, z]
    public float[] headRot;      // Rotation tete [x, y, z, w]
    public float[] leftHandPos;  // Position main gauche
    public float[] leftHandRot;  // Rotation main gauche
    public float[] rightHandPos; // Position main droite
    public float[] rightHandRot; // Rotation main droite
}
```

### Pourquoi des Tableaux float[] ?

`JsonUtility` de Unity ne supporte pas les types `Vector3` ou `Quaternion` directement. Les tableaux `float[]` permettent une serialisation JSON simple et performante.

## 5.3 Communication Vocale WebRTC

### Topologie Mesh

```
        Client A
         /    \
        /      \
    P2P/        \P2P
      /          \
Client B ──P2P── Client C
```

Chaque client etablit une connexion P2P directe avec tous les autres clients de la room.

### Processus de Connexion

1. **Signaling** (via WebSocket serveur)
   - Client A envoie `offer` a Client B
   - Client B repond avec `answer`
   - Echange des `ICE candidates` (chemins reseau)

2. **Connexion P2P**
   - Une fois le signaling termine, connexion directe
   - Audio transite en P2P sans passer par le serveur

### Audio Spatial 3D

L'audio de chaque participant est attache a la position de sa tete dans l'espace 3D, creant une spatialisation naturelle.

## 5.4 Tableau Blanc Collaboratif

### Architecture 3 Couches

```
┌─────────────────────────────────────────┐
│ Couche 3: Outils de Dessin              │
│ (WhiteboardMarker / DesktopDrawer)      │
│ → Capture input, dessine localement     │
├─────────────────────────────────────────┤
│ Couche 2: Surface de Dessin Reseau      │
│ (WhiteboardDrawingSurface)              │
│ → Recoit les traits du reseau           │
│ → Transparente, superposee              │
├─────────────────────────────────────────┤
│ Couche 1: Fond du Whiteboard            │
│ (Whiteboard)                            │
│ → Fond blanc                            │
│ → Mode presentation (images)            │
└─────────────────────────────────────────┘
```

### Synchronisation

- **Batch:** Les traits sont envoyes par lots (33ms)
- **Late-joiners:** Pattern Request/State pour recevoir l'etat actuel

## 5.5 Enregistrement Video

### Defi: Performance VR

L'enregistrement video classique (capture synchrone) cause des drops de framerate qui provoquent le motion sickness en VR.

### Solution: Pipeline Async 3 Etapes

```
Main Thread              Encode Thread           Write Thread
━━━━━━━━━━━━             ━━━━━━━━━━━━           ━━━━━━━━━━━
    │                         │                      │
    │ AsyncGPUReadback        │                      │
    │ (non-bloquant)          │                      │
    │─────────────────────────>                      │
    │                         │ RGB → TGA            │
    │                         │ (background)         │
    │                         │──────────────────────>
    │                         │                      │ File.Write
    │                         │                      │ (background)
    │                         │                      │
```

**Resultat:** Impact < 0.1ms sur le main thread, pas de motion sickness.

---

# 6. Difficultes Rencontrees et Solutions

## 6.1 Latence de Synchronisation

### Probleme
Les mouvements des joueurs distants apparaissaient saccades.

### Solution
- **Interpolation:** Lissage des positions entre les mises a jour
- **Prediction:** Continuation du mouvement si paquet manque
- **Frequence:** 30Hz offre un bon compromis latence/bande passante

## 6.2 Tracking VR Incorrect

### Probleme
Les mains et la tete des joueurs distants n'etaient pas correctement positionnees.

### Solution
- **Detachement hierarchique:** Tete et mains en world-space, pas enfants du body
- **Scripts de correction:** `VRTrackingFix.cs`, `ControllerTrackingFix.cs`

## 6.3 Motion Sickness pendant l'Enregistrement

### Probleme
L'enregistrement video causait des drops de framerate, provoquant des nausees.

### Solution
- **AsyncGPUReadback:** Lecture GPU non-bloquante
- **Pipeline multi-thread:** Encodage et ecriture en background
- **Buffer pooling:** Reutilisation des buffers memoire

## 6.4 Connexion WebRTC derriere NAT

### Probleme
Les clients derriere des firewalls/NAT ne pouvaient pas etablir de connexions P2P.

### Solution
- **STUN servers:** Pour decouvrir l'IP publique
- **TURN servers:** Relay si P2P impossible
- Configuration dans `WebRTCConfiguration.cs`

## 6.5 Serialisation JSON avec Unity

### Probleme
`JsonUtility` ne supporte pas les objets imbriques complexes ni les types Unity (`Vector3`).

### Solution
- Structures plates avec types primitifs
- Tableaux `float[]` pour les positions/rotations
- Donnees imbriquees: serialisation en string JSON dans le champ `data`

---

# 7. Resultats et Demonstration

## 7.1 Fonctionnalites Implementees

| Fonctionnalite | Statut | Description |
|----------------|--------|-------------|
| Connexion multiplayer | ✅ Complete | WebSocket avec reconnexion auto |
| Systeme de rooms | ✅ Complete | Creation, join, leave, kick |
| Synchronisation VR | ✅ Complete | 30Hz, tete + 2 mains |
| Communication vocale | ✅ Complete | WebRTC P2P, audio spatial |
| Mode desktop | ✅ Complete | WASD + souris |
| Tableau blanc | ✅ Complete | Dessin VR/Desktop, sync |
| Partage d'ecran | ✅ Complete | 854x480 @ 3fps |
| Partage fichiers | ✅ Complete | PDF, images, documents |
| Pointeur laser | ✅ Complete | VR (A) / Desktop (L) |
| Authentification | ✅ Complete | Login/Register/Guest |
| Personnalisation avatar | ✅ Complete | Couleurs |
| Enregistrement | ✅ Complete | 1080p, async pipeline |
| Menu VR | ✅ Complete | Interface complete |
| Systeme audio | ✅ Complete | Ambiance, effets |

## 7.2 Performances

| Metrique | Valeur |
|----------|--------|
| Latence reseau moyenne | < 50ms (local) |
| Framerate VR | 72 FPS stable |
| Utilisation CPU serveur | < 5% (10 clients) |
| Bande passante par client | ~50 KB/s |

## 7.3 Captures d'Ecran

[A inserer: captures de l'application]

---

# 8. Perspectives et Ameliorations

## 8.1 Ameliorations Prevues

| Priorite | Fonctionnalite | Description |
|----------|----------------|-------------|
| Haute | Avatars avances | Corps complet, animations |
| Haute | E2E Encryption | Chiffrement des communications |
| Moyenne | Calendrier | Integration calendrier reunions |
| Moyenne | Historique | Sauvegarde des reunions |
| Basse | SSO | Single Sign-On entreprise |
| Basse | Admin panel | Interface d'administration |

## 8.2 Optimisations Possibles

1. **Compression Delta** - N'envoyer que les changements de position
2. **LOD Reseau** - Reduire frequence pour joueurs distants
3. **Serveur Distribue** - Plusieurs instances pour scalabilite
4. **Streaming Video** - WebRTC pour le partage d'ecran (au lieu de JPEG)

---

# 9. Conclusion

## 9.1 Bilan

Le projet VRMeet a permis de developper une application complete de reunion virtuelle multiplayer combinant:

- **Technologies modernes:** Unity 6, WebSocket, WebRTC, OpenXR
- **Architecture solide:** Client-serveur hybride, patterns de conception
- **Fonctionnalites riches:** Voix, whiteboard, partage, enregistrement
- **Multi-plateforme:** VR et Desktop

## 9.2 Competences Acquises

- Developpement VR avec Unity et XR Interaction Toolkit
- Programmation reseau temps reel (WebSocket, WebRTC)
- Architecture logicielle (Singleton, Observer, async)
- Developpement backend Node.js
- Optimisation pour la VR (performance, motion sickness)

## 9.3 Mot de Fin

Ce projet demontre qu'il est possible de creer une solution de reunion virtuelle complete et performante en utilisant des technologies accessibles. L'architecture modulaire permet des evolutions futures tout en maintenant une base stable.

---

# Annexes

## A. Structure des Fichiers

```
WebSocket_VR/
├── Assets/
│   ├── Scrips/           # Code source C#
│   │   ├── Network/      # Coeur reseau
│   │   ├── VR/           # Controleurs VR
│   │   ├── WebRTC/       # Communication vocale
│   │   ├── WhiteBoard/   # Tableau blanc
│   │   ├── Sharing/      # Partage contenu
│   │   ├── UI/           # Interface
│   │   └── ...
│   ├── Scenes/           # Bootstrap + Meet
│   └── Prefabs/          # Prefabs Unity
├── Server/
│   ├── server.js         # Serveur principal
│   └── src/
│       ├── database.js   # Connexion DB
│       └── auth.js       # Authentification
└── Packages/
    └── manifest.json     # Dependances Unity
```

## B. Commandes Utiles

```bash
# Lancer le serveur en dev
cd Server && npm run dev

# Lancer le serveur en production
cd Server && npm start

# Tests
cd Server && npm test
```

## C. Configuration

### Variables d'Environnement (.env)

```env
PORT=8080
DB_HOST=localhost
DB_USER=vrmeet
DB_PASSWORD=password
DB_NAME=vrmeet_db
JWT_SECRET=secret_key
```

### URL Serveur (Unity)

Dans `VRNetworkManager.cs`:
- Dev: `ws://localhost:8080`
- Prod: `wss://votre-domaine.com`

---

*Rapport genere le 27 Fevrier 2026*
