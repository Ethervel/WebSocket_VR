# Protocole de Messages - Unity <-> Serveur

Ce document liste tous les messages echanges entre le client Unity et le serveur Node.js.

---

## Format General de Tous les Messages

Chaque message (dans les deux sens) a TOUJOURS cette structure :

```json
{
    "type": "nom-du-message",
    "senderId": "identifiant-unique",
    "data": "donnees-en-json-string"
}
```

| Champ | Description |
|-------|-------------|
| `type` | Le nom du message (ex: "room-join", "vr-position") |
| `senderId` | Qui envoie : ID du joueur ou "server" |
| `data` | Les donnees, TOUJOURS en string JSON |

---

## Table des Messages

| Categorie | Message | Direction | Description |
|-----------|---------|-----------|-------------|
| **Connexion** | welcome | Serveur → Unity | Accueil + attribution ID |
| | peer-connected | Serveur → Unity | Nouveau joueur connecte |
| | peer-disconnected | Serveur → Unity | Joueur deconnecte |
| **Salles** | room-available | Unity → Serveur | Creer une salle |
| | room-join | Unity → Serveur | Rejoindre une salle |
| | room-leave | Unity → Serveur | Quitter une salle |
| | room-closed | Serveur → Unity | Salle fermee |
| | room-list | Serveur → Unity | Liste des salles |
| **Position VR** | vr-position | Unity ↔ Serveur | Position/rotation joueur |
| **Tableau blanc** | whiteboard-batch | Unity ↔ Serveur | Traits dessines |
| | whiteboard-clear | Unity ↔ Serveur | Effacer le tableau |
| | whiteboard-request | Unity → Serveur | Demander l'etat |
| | whiteboard-state | Serveur → Unity | Etat du tableau |
| **Chat vocal** | webrtc-offer | Unity → Serveur | Offre WebRTC |
| | webrtc-answer | Unity → Serveur | Reponse WebRTC |
| | webrtc-ice-candidate | Unity ↔ Serveur | Candidat ICE |
| **Partage ecran** | screen-share-start | Unity → Serveur | Debut partage |
| | screen-share-frame | Unity → Serveur | Image ecran |
| | screen-share-stop | Unity → Serveur | Fin partage |
| **Fichiers** | file-announce | Unity → Serveur | Nouveau fichier |
| | file-list-request | Unity → Serveur | Demander liste |
| | file-list-response | Serveur → Unity | Liste fichiers |
| **Auth** | auth-register | Unity → Serveur | Inscription |
| | auth-login | Unity → Serveur | Connexion |
| | auth-register-response | Serveur → Unity | Reponse inscription |
| | auth-login-response | Serveur → Unity | Reponse connexion |

---

## 1. CONNEXION

### 1.1 welcome

**Direction :** Serveur → Unity

**Quand :** Immediatement apres qu'un client se connecte

**Ce que le serveur envoie :**
```json
{
    "type": "welcome",
    "senderId": "550e8400-e29b-41d4-a716-446655440000"
}
```

| Champ | Description |
|-------|-------------|
| `senderId` | L'ID unique attribue a ce client (UUID) |

**Ce que Unity fait :** Stocke cet ID comme `LocalId`, se considere connecte

---

### 1.2 peer-connected

**Direction :** Serveur → Unity (broadcast)

**Quand :** Un autre joueur vient de se connecter au serveur

**Ce que le serveur envoie :**
```json
{
    "type": "peer-connected",
    "senderId": "id-du-nouveau-joueur"
}
```

**Ce que Unity fait :** Note qu'un nouveau joueur existe (pour WebRTC voice)

---

### 1.3 peer-disconnected

**Direction :** Serveur → Unity (broadcast)

**Quand :** Un joueur s'est deconnecte du serveur

**Ce que le serveur envoie :**
```json
{
    "type": "peer-disconnected",
    "senderId": "id-du-joueur-parti"
}
```

**Ce que Unity fait :** Supprime l'avatar du joueur, ferme la connexion WebRTC

---

## 2. GESTION DES SALLES

### 2.1 room-available (Creer une salle)

**Direction :** Unity → Serveur

**Quand :** Un joueur cree une nouvelle salle

**Ce que Unity envoie :**
```json
{
    "type": "room-available",
    "senderId": "id-du-createur",
    "data": "{\"roomId\":\"ABC123\",\"roomName\":\"Ma Reunion\",\"roomType\":0,\"maxPlayers\":10}"
}
```

**Contenu de `data` :**
| Champ | Type | Description |
|-------|------|-------------|
| `roomId` | string | Code unique de la salle (6 caracteres) |
| `roomName` | string | Nom affiche de la salle |
| `roomType` | int | 0=Lobby, 1=MeetingRoomA, 2=MeetingRoomB |
| `maxPlayers` | int | Nombre max de joueurs |

**Ce que le serveur fait :**
1. Cree la salle dans sa liste
2. Met le createur dans cette salle
3. Broadcast `room-available` a tout le monde
4. Broadcast la nouvelle `room-list`

**Ce que le serveur renvoie (broadcast) :**
```json
{
    "type": "room-available",
    "senderId": "id-du-createur",
    "data": "{\"roomId\":\"ABC123\",\"hostId\":\"id-du-createur\",\"roomName\":\"Ma Reunion\",\"roomType\":0,\"playerCount\":1,\"maxPlayers\":10,\"createdAt\":1704067200000}"
}
```

---

### 2.2 room-join (Rejoindre une salle)

**Direction :** Unity → Serveur

**Quand :** Un joueur veut rejoindre une salle existante

**Ce que Unity envoie :**
```json
{
    "type": "room-join",
    "senderId": "id-du-joueur",
    "data": "{\"roomId\":\"ABC123\",\"playerName\":\"Jean\"}"
}
```

**Contenu de `data` :**
| Champ | Type | Description |
|-------|------|-------------|
| `roomId` | string | Code de la salle a rejoindre |
| `playerName` | string | Nom du joueur |

**Ce que le serveur fait :**
1. Verifie que la salle existe
2. Verifie que la salle n'est pas pleine
3. Ajoute le joueur a la salle
4. Broadcast `room-join` aux autres de la salle

**Ce que le serveur renvoie (aux autres de la salle) :**
```json
{
    "type": "room-join",
    "senderId": "id-du-nouveau",
    "data": "{\"roomId\":\"ABC123\",\"playerName\":\"Jean\"}"
}
```

**En cas d'erreur :**
```json
{
    "type": "error",
    "senderId": "server",
    "data": "Room ABC123 not found"
}
```

---

### 2.3 room-leave (Quitter une salle)

**Direction :** Unity → Serveur

**Quand :** Un joueur quitte volontairement une salle

**Ce que Unity envoie :**
```json
{
    "type": "room-leave",
    "senderId": "id-du-joueur",
    "data": "{\"roomId\":\"ABC123\"}"
}
```

**Ce que le serveur fait :**
1. Retire le joueur de la salle
2. Decremente le compteur de joueurs
3. Broadcast aux autres de la salle

**Ce que le serveur renvoie (aux autres de la salle) :**
```json
{
    "type": "room-leave",
    "senderId": "id-du-joueur-parti",
    "data": "{\"roomId\":\"ABC123\",\"playerId\":\"id-du-joueur-parti\"}"
}
```

---

### 2.4 room-closed (Salle fermee)

**Direction :** Serveur → Unity (broadcast)

**Quand :** L'hote de la salle s'est deconnecte

**Ce que le serveur envoie :**
```json
{
    "type": "room-closed",
    "senderId": "id-de-l-hote",
    "data": "{\"roomId\":\"ABC123\"}"
}
```

**Ce que Unity fait :** Ejecte tous les joueurs de la salle, retour au lobby

---

### 2.5 room-list (Liste des salles)

**Direction :** Serveur → Unity

**Quand :**
- A la connexion initiale
- Quand une salle est creee/fermee
- Quand un joueur rejoint/quitte

**Ce que le serveur envoie :**
```json
{
    "type": "room-list",
    "senderId": "server",
    "data": "{\"rooms\":[{\"roomId\":\"ABC123\",\"hostId\":\"xxx\",\"roomName\":\"Reunion 1\",\"roomType\":0,\"playerCount\":3,\"maxPlayers\":10},{\"roomId\":\"DEF456\",\"hostId\":\"yyy\",\"roomName\":\"Reunion 2\",\"roomType\":1,\"playerCount\":1,\"maxPlayers\":10}]}"
}
```

**Structure d'une salle dans la liste :**
| Champ | Type | Description |
|-------|------|-------------|
| `roomId` | string | Code unique |
| `hostId` | string | ID du createur |
| `roomName` | string | Nom affiche |
| `roomType` | int | Type de salle |
| `playerCount` | int | Joueurs actuels |
| `maxPlayers` | int | Maximum |

---

## 3. SYNCHRONISATION POSITION VR

### 3.1 vr-position

**Direction :** Unity ↔ Serveur ↔ Unity

**Quand :** 30 fois par seconde (30 Hz) quand le joueur bouge

**Ce que Unity envoie :**
```json
{
    "type": "vr-position",
    "senderId": "id-du-joueur",
    "data": "{\"roomId\":\"ABC123\",\"roomType\":0,\"posX\":1.5,\"posY\":0.0,\"posZ\":3.2,\"rotY\":45.0,\"headPosX\":1.5,\"headPosY\":1.7,\"headPosZ\":3.2,\"headRotX\":0.0,\"headRotY\":0.7,\"headRotZ\":0.0,\"headRotW\":0.7,\"leftHandPosX\":1.2,\"leftHandPosY\":1.0,\"leftHandPosZ\":3.0,\"leftHandRotX\":0.0,\"leftHandRotY\":0.0,\"leftHandRotZ\":0.0,\"leftHandRotW\":1.0,\"rightHandPosX\":1.8,\"rightHandPosY\":1.0,\"rightHandPosZ\":3.0,\"rightHandRotX\":0.0,\"rightHandRotY\":0.0,\"rightHandRotZ\":0.0,\"rightHandRotW\":1.0}"
}
```

**Contenu de `data` :**
| Champ | Type | Description |
|-------|------|-------------|
| `roomId` | string | Salle actuelle |
| `roomType` | int | Type de salle |
| `posX, posY, posZ` | float | Position du corps |
| `rotY` | float | Rotation du corps (Y seulement) |
| `headPosX, headPosY, headPosZ` | float | Position de la tete |
| `headRotX, headRotY, headRotZ, headRotW` | float | Rotation tete (Quaternion) |
| `leftHandPosX, Y, Z` | float | Position main gauche |
| `leftHandRotX, Y, Z, W` | float | Rotation main gauche |
| `rightHandPosX, Y, Z` | float | Position main droite |
| `rightHandRotX, Y, Z, W` | float | Rotation main droite |

**Ce que le serveur fait :**
- `broadcastToRoom()` - Renvoie aux autres joueurs de la MEME salle

**Ce que le serveur renvoie (aux autres de la salle) :**
Exactement le meme message, sans modification

**Ce que Unity fait a la reception :**
- Trouve l'avatar du joueur
- Met a jour sa position avec interpolation

---

## 4. TABLEAU BLANC (WHITEBOARD)

### 4.1 whiteboard-batch (Traits dessines)

**Direction :** Unity → Serveur → Unity (room)

**Quand :** Quand un joueur dessine sur le tableau (30 Hz)

**Ce que Unity envoie :**
```json
{
    "type": "whiteboard-batch",
    "senderId": "id-du-dessinateur",
    "data": "{\"whiteboardId\":\"whiteboard-main\",\"roomId\":\"ABC123\",\"r\":0.0,\"g\":0.0,\"b\":1.0,\"a\":1.0,\"penSize\":5,\"pointsFlat\":[0.5,0.5,0.52,0.51,0.54,0.52,0.56,0.53]}"
}
```

**Contenu de `data` :**
| Champ | Type | Description |
|-------|------|-------------|
| `whiteboardId` | string | ID du tableau |
| `roomId` | string | Salle actuelle |
| `r, g, b, a` | float | Couleur RGBA (0.0 - 1.0) |
| `penSize` | int | Taille du stylo en pixels |
| `pointsFlat` | float[] | Coordonnees UV [u1,v1, u2,v2, ...] |

**Ce que le serveur fait :** `broadcastToRoom()`

---

### 4.2 whiteboard-clear (Effacer)

**Direction :** Unity → Serveur → Unity (room)

**Ce que Unity envoie :**
```json
{
    "type": "whiteboard-clear",
    "senderId": "id-du-joueur",
    "data": "{\"whiteboardId\":\"whiteboard-main\",\"roomId\":\"ABC123\"}"
}
```

**Ce que le serveur fait :** `broadcastToRoom()`

---

### 4.3 whiteboard-request (Demander l'etat)

**Direction :** Unity → Serveur → Unity (room)

**Quand :** Un nouveau joueur rejoint et veut voir le dessin actuel

**Ce que Unity envoie :**
```json
{
    "type": "whiteboard-request",
    "senderId": "id-du-nouveau",
    "data": "{\"whiteboardId\":\"whiteboard-main\",\"roomId\":\"ABC123\"}"
}
```

**Ce que le serveur fait :** `broadcastToRoom()` - Un autre joueur repondra

---

### 4.4 whiteboard-state (Etat du tableau)

**Direction :** Unity → Serveur → Unity (cible)

**Quand :** En reponse a whiteboard-request

**Ce que Unity envoie :**
```json
{
    "type": "whiteboard-state",
    "senderId": "id-du-repondeur",
    "data": "{\"whiteboardId\":\"whiteboard-main\",\"roomId\":\"ABC123\",\"targetId\":\"id-du-demandeur\",\"textureData\":\"base64-de-l-image-png...\"}"
}
```

**Contenu de `data` :**
| Champ | Type | Description |
|-------|------|-------------|
| `targetId` | string | A qui envoyer (le demandeur) |
| `textureData` | string | Image PNG en base64 |

**Ce que le serveur fait :** Envoie SEULEMENT au `targetId`

---

## 5. CHAT VOCAL (WEBRTC)

Le serveur fait SEULEMENT le relais (signaling). La voix passe directement entre joueurs.

### 5.1 webrtc-offer

**Direction :** Unity A → Serveur → Unity B

**Quand :** Joueur A veut parler a Joueur B

**Ce que Unity A envoie :**
```json
{
    "type": "webrtc-offer",
    "senderId": "id-joueur-A",
    "data": "{\"targetId\":\"id-joueur-B\",\"sdp\":\"v=0\\r\\no=- 123456 2 IN IP4 127.0.0.1\\r\\n...\"}"
}
```

**Ce que le serveur fait :** Envoie a `targetId` (Joueur B)

---

### 5.2 webrtc-answer

**Direction :** Unity B → Serveur → Unity A

**Quand :** Joueur B accepte l'appel

**Ce que Unity B envoie :**
```json
{
    "type": "webrtc-answer",
    "senderId": "id-joueur-B",
    "data": "{\"targetId\":\"id-joueur-A\",\"sdp\":\"v=0\\r\\no=- 789012 2 IN IP4 127.0.0.1\\r\\n...\"}"
}
```

**Ce que le serveur fait :** Envoie a `targetId` (Joueur A)

---

### 5.3 webrtc-ice-candidate

**Direction :** Unity ↔ Serveur ↔ Unity

**Quand :** Echange d'infos reseau pour etablir la connexion

**Ce que Unity envoie :**
```json
{
    "type": "webrtc-ice-candidate",
    "senderId": "id-joueur",
    "data": "{\"targetId\":\"id-autre-joueur\",\"candidate\":\"candidate:842163049 1 udp ...\",\"sdpMid\":\"audio\",\"sdpMLineIndex\":0}"
}
```

**Ce que le serveur fait :** Envoie a `targetId`

---

## 6. PARTAGE D'ECRAN

### 6.1 screen-share-start

**Direction :** Unity → Serveur → Unity (room)

**Quand :** Un joueur commence a partager son ecran

**Ce que Unity envoie :**
```json
{
    "type": "screen-share-start",
    "senderId": "id-du-presentateur",
    "data": "{\"sharerId\":\"id-du-presentateur\",\"sharerName\":\"Jean\"}"
}
```

**Ce que le serveur fait :** `broadcastToRoom()`

---

### 6.2 screen-share-frame

**Direction :** Unity → Serveur → Unity (room)

**Quand :** 3 fois par seconde pendant le partage

**Ce que Unity envoie :**
```json
{
    "type": "screen-share-frame",
    "senderId": "id-du-presentateur",
    "data": "{\"sharerId\":\"id-du-presentateur\",\"sharerName\":\"Jean\",\"frameIndex\":42,\"jpegBase64Data\":\"/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAA...\"}"
}
```

**Contenu de `data` :**
| Champ | Type | Description |
|-------|------|-------------|
| `sharerId` | string | ID du presentateur |
| `sharerName` | string | Nom du presentateur |
| `frameIndex` | int | Numero de l'image |
| `jpegBase64Data` | string | Image JPEG en base64 (~30-50 KB) |

**Ce que le serveur fait :** `broadcastToRoom()`

---

### 6.3 screen-share-stop

**Direction :** Unity → Serveur → Unity (room)

**Quand :** Le presentateur arrete le partage

**Ce que Unity envoie :**
```json
{
    "type": "screen-share-stop",
    "senderId": "id-du-presentateur",
    "data": "{\"sharerId\":\"id-du-presentateur\"}"
}
```

**Ce que le serveur fait :** `broadcastToRoom()`

---

## 7. PARTAGE DE FICHIERS

### 7.1 file-announce

**Direction :** Unity → Serveur → Unity (room)

**Quand :** Un joueur partage un fichier

**Ce que Unity envoie :**
```json
{
    "type": "file-announce",
    "senderId": "id-du-joueur",
    "data": "{\"fileId\":\"file-123\",\"fileName\":\"presentation.pdf\",\"fileSize\":1048576,\"uploadedBy\":\"Jean\",\"uploadTime\":1704067200000}"
}
```

---

### 7.2 file-list-request

**Direction :** Unity → Serveur → Unity (room)

**Quand :** Un nouveau joueur veut voir les fichiers partages

**Ce que Unity envoie :**
```json
{
    "type": "file-list-request",
    "senderId": "id-du-nouveau",
    "data": "{\"roomId\":\"ABC123\"}"
}
```

---

### 7.3 file-list-response

**Direction :** Unity → Serveur → Unity (cible)

**Quand :** En reponse a file-list-request

**Ce que Unity envoie :**
```json
{
    "type": "file-list-response",
    "senderId": "id-du-repondeur",
    "data": "{\"targetId\":\"id-du-demandeur\",\"files\":[{\"fileId\":\"file-123\",\"fileName\":\"presentation.pdf\",\"fileSize\":1048576,\"uploadedBy\":\"Jean\"}]}"
}
```

---

## 8. AUTHENTIFICATION

### 8.1 auth-register (Inscription)

**Direction :** Unity → Serveur

**Ce que Unity envoie :**
```json
{
    "type": "auth-register",
    "senderId": "id-du-client",
    "data": "{\"username\":\"jean123\",\"email\":\"jean@email.com\",\"password\":\"motdepasse\",\"displayName\":\"Jean Dupont\"}"
}
```

---

### 8.2 auth-register-response

**Direction :** Serveur → Unity

**Succes :**
```json
{
    "type": "auth-register-response",
    "senderId": "server",
    "data": "{\"success\":true,\"userId\":42,\"username\":\"jean123\",\"displayName\":\"Jean Dupont\"}"
}
```

**Echec :**
```json
{
    "type": "auth-register-response",
    "senderId": "server",
    "data": "{\"success\":false,\"error\":\"Username already exists\"}"
}
```

---

### 8.3 auth-login (Connexion)

**Direction :** Unity → Serveur

**Ce que Unity envoie :**
```json
{
    "type": "auth-login",
    "senderId": "id-du-client",
    "data": "{\"username\":\"jean123\",\"password\":\"motdepasse\"}"
}
```

---

### 8.4 auth-login-response

**Direction :** Serveur → Unity

**Succes :**
```json
{
    "type": "auth-login-response",
    "senderId": "server",
    "data": "{\"success\":true,\"userId\":42,\"username\":\"jean123\",\"displayName\":\"Jean Dupont\",\"avatarColor\":\"#3498db\"}"
}
```

**Echec :**
```json
{
    "type": "auth-login-response",
    "senderId": "server",
    "data": "{\"success\":false,\"error\":\"Invalid password\"}"
}
```

---

## 9. RESUME VISUEL

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              FLUX DES MESSAGES                              │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  UNITY CLIENT                         SERVEUR                               │
│       │                                  │                                  │
│       │ ══════ CONNEXION ══════════════>│                                  │
│       │                                  │                                  │
│       │<────────── welcome ──────────────│  (ID attribue)                   │
│       │                                  │                                  │
│       │ ─────── room-join ──────────────>│                                  │
│       │                                  │──── room-join ───> Autres        │
│       │                                  │                                  │
│       │ ─────── vr-position (30Hz) ─────>│                                  │
│       │                                  │── vr-position ───> Autres (room) │
│       │<──────── vr-position ────────────│  (des autres)                    │
│       │                                  │                                  │
│       │ ─────── whiteboard-batch ───────>│                                  │
│       │                                  │─ whiteboard-batch -> Autres(room)│
│       │                                  │                                  │
│       │ ─────── webrtc-offer ───────────>│                                  │
│       │                                  │── webrtc-offer ──> Joueur cible  │
│       │<──────── webrtc-answer ──────────│  (de l'autre)                    │
│       │                                  │                                  │
│       │ ─────── room-leave ─────────────>│                                  │
│       │                                  │─── room-leave ───> Autres (room) │
│       │                                  │                                  │
│       │ ══════ DECONNEXION ════════════X │                                  │
│       │                                  │─ peer-disconnected -> Tous       │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 10. CE QUE LE SERVEUR DOIT FAIRE POUR CHAQUE MESSAGE

| Message recu | Action du serveur |
|--------------|-------------------|
| (connexion) | Generer UUID, envoyer `welcome`, broadcast `peer-connected` |
| `room-available` | Creer salle, `broadcast()` + `broadcastRoomList()` |
| `room-join` | Verifier salle, ajouter joueur, `broadcastToRoom()` |
| `room-leave` | Retirer joueur, `broadcastToRoom()` |
| `vr-position` | `broadcastToRoom()` (sans modification) |
| `whiteboard-batch` | `broadcastToRoom()` |
| `whiteboard-clear` | `broadcastToRoom()` |
| `whiteboard-request` | `broadcastToRoom()` |
| `whiteboard-state` | `sendToClient(targetId)` |
| `webrtc-offer` | `sendToClient(targetId)` |
| `webrtc-answer` | `sendToClient(targetId)` |
| `webrtc-ice-candidate` | `sendToClient(targetId)` |
| `screen-share-*` | `broadcastToRoom()` |
| `file-*` | `broadcastToRoom()` ou `sendToClient(targetId)` |
| `auth-register` | Inserer en BDD, `sendToClient()` reponse |
| `auth-login` | Verifier en BDD, `sendToClient()` reponse |
| (deconnexion) | Si hote: supprimer salle + `broadcast(room-closed)`, `broadcast(peer-disconnected)` |

---

## 11. REGLES IMPORTANTES

### 11.1 Portee des messages

| Type de broadcast | Quand l'utiliser | Fonction |
|-------------------|------------------|----------|
| **broadcast()** | Infos globales (peer-connected, room-list) | Tout le monde |
| **broadcastToRoom()** | Infos de gameplay (position, whiteboard) | Seulement la salle |
| **sendToClient()** | Messages directs (WebRTC, reponses auth) | Un seul joueur |

### 11.2 Le champ `data` est TOUJOURS une string

```javascript
// CORRECT
data: JSON.stringify({ roomId: "ABC123" })

// INCORRECT
data: { roomId: "ABC123" }
```

### 11.3 Frequence des messages

| Message | Frequence | Notes |
|---------|-----------|-------|
| `vr-position` | 30 Hz | Optimise avec seuil de mouvement |
| `whiteboard-batch` | 30 Hz | Pendant le dessin |
| `screen-share-frame` | 3 Hz | Images compressees JPEG |
| Autres | Evenementiel | A la demande |

---

*Document de reference pour le protocole de messages VR Meeting*
