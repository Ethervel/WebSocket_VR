# Guide de Test Local - VR Meeting Rooms

Guide pas-a-pas pour tester le deploiement complet (serveur WebSocket + coturn + Unity) en local avec deux PCs.

> **PC A (Serveur) :** `192.168.0.37`
> **Derniere mise a jour : 2026-02-02**

---

## Sommaire

1. [Vue d'ensemble](#vue-densemble)
2. [Prerequis](#prerequis)
3. [Methode 1 : WSL2 Mirrored (Windows 11)](#methode-1--wsl2-mirrored-windows-11)
4. [Methode 2 : VM Bridge (Windows 10 / Linux)](#methode-2--vm-bridge-windows-10--linux)
5. [Methode 3 : Deux PCs LAN (sans TURN)](#methode-3--deux-pcs-lan-sans-turn)
6. [Configuration Unity](#configuration-unity)
7. [Verification et tests](#verification-et-tests)
8. [Depannage](#depannage)

---

## Vue d'ensemble

### Architectures de test

```
PC A (192.168.0.37 - Serveur)                    PC B (Client)
┌─────────────────────────────┐                  ┌──────────────────┐
│                             │                  │                  │
│  WSL2 / VM Linux            │   LAN            │  Unity Editor    │
│  ├── node server.js  :8080  │ <─────────────>  │  ou Build        │
│  └── coturn          :3478  │  192.168.0.x     │                  │
│                             │                  │                  │
│  Unity Editor (client 1)    │                  │  (client 2)      │
│                             │                  │                  │
└─────────────────────────────┘                  └──────────────────┘
```

**Ce qui est teste :**

| Composant | ParrelSync (1 PC) | 2 PCs LAN | 2 PCs + coturn |
|-----------|-------------------|-----------|----------------|
| WebSocket connexion | Oui | Oui | Oui |
| Rooms (create/join/leave) | Oui | Oui | Oui |
| Position sync 30Hz | Oui | Oui | Oui |
| Whiteboard | Oui | Oui | Oui |
| File sharing | Oui | Oui | Oui |
| Voice chat WebRTC | Oui (loopback) | Oui (P2P direct) | Oui (P2P + TURN) |
| Latence reseau reelle | Non | Oui | Oui |
| TURN relay | Non | Non | Oui |

---

## Prerequis

### PC A (192.168.0.37 - Serveur + Client)

| Composant | Requis |
|-----------|--------|
| OS | Windows 11 23H2+ (methode 1) ou Windows 10 (methode 2) |
| Unity | 6000.2.14f1 (deja installe) |
| WSL2 | Pour methode 1 |
| VirtualBox/Hyper-V | Pour methode 2 |

### PC B (Client)

| Composant | Requis |
|-----------|--------|
| Unity | 6000.2.14f1 avec le projet |
| Reseau | Meme LAN que PC A (192.168.0.x) |

---

## Methode 1 : WSL2 Mirrored (Windows 11)

> **Requis :** Windows 11 version 23H2 ou superieur.
> Le mode mirrored fait partager l'IP Windows (192.168.0.37) a WSL2 - pas de port forward necessaire.

### Etape 1 : Activer le mode mirrored

Ouvrir **PowerShell en administrateur** et executer :

```powershell
Set-Content -Path "${env:USERPROFILE}\.wslconfig" -Value "[wsl2]`nnetworkingMode=mirrored" -Encoding UTF8
```

Redemarrer WSL :

```powershell
wsl --shutdown
```

### Etape 2 : Installer Ubuntu dans WSL2

```powershell
# Si pas encore installe
wsl --install -d Ubuntu-22.04

# Lancer WSL
wsl
```

### Etape 3 : Installer Node.js dans WSL2

```bash
# Mettre a jour le systeme
sudo apt update && sudo apt upgrade -y

# Installer Node.js 22 LTS
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs

# Verifier
node --version   # v22.x.x
npm --version    # 10.x.x
```

### Etape 4 : Installer et configurer coturn dans WSL2

```bash
sudo apt install -y coturn
```

Activer le service :

```bash
sudo sed -i 's/#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn
```

Editer la configuration coturn :

```bash
sudo nano /etc/turnserver.conf
```

Remplacer tout le contenu par :

```ini
# ===== CONFIGURATION COTURN - TEST LOCAL =====

# Ports
listening-port=3478
tls-listening-port=5349
min-port=49152
max-port=65535

# Reseau
listening-ip=0.0.0.0
external-ip=192.168.0.37
relay-ip=0.0.0.0

# Authentification (credentials fixes pour test local)
realm=local-test
lt-cred-mech
user=vrmeeting:testpassword123

# Securite
no-multicast-peers
no-cli

# Logs (verbose pour debug)
log-file=/var/log/turnserver.log
simple-log
verbose
```

> **Ctrl+O** pour sauvegarder, **Ctrl+X** pour quitter nano.

Demarrer coturn :

```bash
sudo systemctl enable coturn
sudo systemctl start coturn

# Verifier que ca tourne
sudo systemctl status coturn
```

> Si systemd n'est pas actif dans WSL2, voir la section [Depannage - WSL2 specifique](#wsl2-specifique).

### Etape 5 : Lancer le serveur WebSocket

```bash
# Acceder au dossier Server du projet
cd /mnt/d/Test_project/WebSocket_VR/Server

# Installer les dependances (premiere fois uniquement)
npm install

# Lancer le serveur
node server.js
```

> **Erreur "linux is NOT supported" ?** Le module `pdf-poppler` ne fonctionne que sous Windows. Voir la section [Depannage - pdf-poppler Linux](#pdf-poppler-linux).

Vous devriez voir :

```
============================================
  VR MEETING ROOMS - WebSocket Server
============================================
  Port: 8080
  Heartbeat: 30s
============================================
```

### Etape 6 : Ouvrir le firewall Windows

Executer en **PowerShell administrateur** sur le PC A :

```powershell
# WebSocket
netsh advfirewall firewall add rule name="VR Meeting - WebSocket" dir=in action=allow protocol=TCP localport=8080

# coturn signaling
netsh advfirewall firewall add rule name="VR Meeting - TURN TCP" dir=in action=allow protocol=TCP localport=3478
netsh advfirewall firewall add rule name="VR Meeting - TURN UDP" dir=in action=allow protocol=UDP localport=3478

# coturn TLS
netsh advfirewall firewall add rule name="VR Meeting - TURN TLS" dir=in action=allow protocol=TCP localport=5349

# coturn relay (UDP)
netsh advfirewall firewall add rule name="VR Meeting - TURN Relay" dir=in action=allow protocol=UDP localport=49152-65535
```

### Etape 7 : Configurer Unity et tester

Voir la section [Configuration Unity](#configuration-unity) ci-dessous.

---

## Methode 2 : VM Bridge (Windows 10 / Linux)

> Pour Windows 10, ou si le mode mirrored WSL2 n'est pas disponible.
> La VM recevra sa propre IP sur le LAN (ex: 192.168.0.XX).

### Etape 1 : Creer la VM

**VirtualBox :**

1. Telecharger Ubuntu Server 22.04 LTS (pas Desktop, plus leger)
2. Creer une VM : 1 CPU, 1 GB RAM, 10 GB disque
3. **Reseau : mode Bridge** (pas NAT)
   - Settings > Network > Attached to: **Bridged Adapter**
   - Selectionner votre adaptateur reseau physique
4. Installer Ubuntu Server

**Hyper-V :**

1. Creer un switch virtuel **externe** (Virtual Switch Manager)
2. Creer la VM avec ce switch
3. Installer Ubuntu Server

### Etape 2 : Installer les composants dans la VM

```bash
# Mettre a jour
sudo apt update && sudo apt upgrade -y

# Node.js 22 LTS
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs

# coturn
sudo apt install -y coturn
sudo sed -i 's/#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn
```

### Etape 3 : Recuperer l'IP de la VM

```bash
ip addr show
# Chercher l'IP sur l'interface principale (eth0 ou enp0s3)
# Elle sera sur le meme sous-reseau : 192.168.0.XX
```

### Etape 4 : Configurer coturn

```bash
sudo nano /etc/turnserver.conf
```

Meme contenu que la methode 1, mais **remplacer `external-ip` par l'IP de la VM** :

```ini
# ===== CONFIGURATION COTURN - TEST LOCAL =====

# Ports
listening-port=3478
tls-listening-port=5349
min-port=49152
max-port=65535

# Reseau - REMPLACER PAR L'IP DE LA VM
listening-ip=0.0.0.0
external-ip=192.168.0.XX
relay-ip=0.0.0.0

# Authentification
realm=local-test
lt-cred-mech
user=vrmeeting:testpassword123

# Securite
no-multicast-peers
no-cli

# Logs
log-file=/var/log/turnserver.log
simple-log
verbose
```

```bash
sudo systemctl enable coturn
sudo systemctl start coturn
```

### Etape 5 : Copier et lancer le serveur WebSocket

```bash
# Option A : dossier partage VirtualBox
# Monter le dossier partage et copier Server/

# Option B : scp depuis Windows (Git Bash ou PowerShell)
scp -r D:\Test_project\WebSocket_VR\Server user@192.168.0.XX:~/Server

# Option C : git clone
git clone <votre-repo>
```

```bash
cd ~/Server
npm install
node server.js
```

> **Note :** Pas besoin de firewall rules cote VM - Ubuntu Server n'a pas de firewall actif par defaut. Si ufw est actif : `sudo ufw allow 8080/tcp && sudo ufw allow 3478 && sudo ufw allow 49152:65535/udp`

> **Erreur "linux is NOT supported" ?** Voir la section [Depannage - pdf-poppler Linux](#pdf-poppler-linux).

### Etape 6 : Configurer Unity et tester

Voir la section [Configuration Unity](#configuration-unity) ci-dessous.

> **Important :** Pour la methode VM, utiliser l'IP de la VM (192.168.0.XX) au lieu de 192.168.0.37 dans la configuration Unity du PC B et du TURN.

---

## Methode 3 : Deux PCs LAN (sans TURN)

> Le plus simple. Teste le WebSocket et le WebRTC P2P direct (sans TURN).
> Le voice chat fonctionne car les deux PCs sont sur le meme LAN.

### Etape 1 : Lancer le serveur sur PC A (192.168.0.37)

```bash
cd D:\Test_project\WebSocket_VR\Server
npm install    # premiere fois
node server.js
```

### Etape 2 : Ouvrir le firewall sur PC A

```powershell
# PowerShell admin
netsh advfirewall firewall add rule name="VR Meeting - WebSocket" dir=in action=allow protocol=TCP localport=8080
```

### Etape 3 : Configurer Unity sur PC B

Changer `serverUrl` dans VRNetworkManager (Inspector) :
```
ws://192.168.0.37:8080
```

Pas besoin de configurer le TURN - le P2P direct fonctionne en LAN.

---

## Configuration Unity

### Sur le PC A (192.168.0.37 - client local)

Dans le Unity Inspector, selectionner le GameObject avec `VRNetworkManager` :

| Champ | Valeur |
|-------|--------|
| `serverUrl` | `ws://localhost:8080` |
| `enforceSecureConnection` | `false` |

Pour tester coturn depuis le PC A aussi, selectionner le GameObject avec `VoiceChatManager` :

| Champ | Valeur |
|-------|--------|
| `useCustomTurnServer` | `true` |
| `customTurnUrl` | `turn:192.168.0.37:3478` |
| `customTurnUsername` | `vrmeeting` |
| `customTurnCredential` | `testpassword123` |
| `enableTurnTcp` | `true` |

### Sur le PC B (client distant)

Meme projet Unity, mais avec l'IP du PC A :

| Champ (VRNetworkManager) | Valeur |
|---------------------------|--------|
| `serverUrl` | `ws://192.168.0.37:8080` |
| `enforceSecureConnection` | `false` |

| Champ (VoiceChatManager) | Valeur |
|---------------------------|--------|
| `useCustomTurnServer` | `true` |
| `customTurnUrl` | `turn:192.168.0.37:3478` |
| `customTurnUsername` | `vrmeeting` |
| `customTurnCredential` | `testpassword123` |
| `enableTurnTcp` | `true` |

### Changement d'URL en runtime

L'URL du serveur peut aussi etre changee depuis le menu in-game (Settings > Server URL) grace a `VRMenuPageSettings.cs`. Pas besoin de recompiler pour changer l'IP.

---

## Verification et tests

### 1. Tester la connexion WebSocket

Depuis le PC B, ouvrir un terminal :

```bash
# Installer wscat (une seule fois)
npm install -g wscat

# Tester la connexion
wscat -c ws://192.168.0.37:8080
```

Vous devriez recevoir un message `welcome` :

```json
{"type":"welcome","senderId":"xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"}
```

Taper pour tester :
```json
{"type":"room-list-request","data":""}
```

Reponse attendue :
```json
{"type":"room-list","senderId":"server","data":"{\"rooms\":[]}"}
```

### 2. Tester coturn

**Depuis le PC A (WSL2 ou VM) :**

```bash
# Verifier que coturn tourne
sudo systemctl status coturn

# Verifier les ports
sudo ss -tulnp | grep turnserver

# Sortie attendue (au minimum) :
# udp  UNCONN  0  0  0.0.0.0:3478   *  users:(("turnserver",...))
# tcp  LISTEN  0  0  0.0.0.0:3478   *  users:(("turnserver",...))
```

**Test avec turnutils (depuis la VM/WSL2) :**

```bash
sudo apt install -y coturn-utils

# Test local
turnutils_uclient -u vrmeeting -w testpassword123 127.0.0.1
```

**Test depuis le navigateur (PC B) :**

1. Ouvrir https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
2. Ajouter un serveur ICE :
   - URL : `turn:192.168.0.37:3478`
   - Username : `vrmeeting`
   - Credential : `testpassword123`
3. Cliquer "Gather candidates"
4. Vous devriez voir des candidats de type `relay` - ca confirme que coturn fonctionne

### 3. Tester le flux complet

1. **PC A (192.168.0.37)** : Lancer le serveur (`node server.js`)
2. **PC A** : Lancer Unity, creer une room
3. **PC B** : Lancer Unity, rejoindre la room avec le code
4. **Verifier :**
   - Les deux joueurs se voient (position sync)
   - Le voice chat fonctionne (push-to-talk : touche V en desktop)
   - Le whiteboard est synchronise
   - Le partage de fichiers fonctionne

### 4. Verifier les logs

**Serveur WebSocket :**

```
[Connect] Client a1b2c3d4...
[Connect] Client e5f6g7h8...
[Room] Created: XYZ789
[Room] Join: e5f6g7h8 -> XYZ789
[Status] 2 clients | 1 rooms
```

**coturn :**

```bash
sudo tail -f /var/log/turnserver.log
```

Vous devriez voir les allocations TURN quand le voice chat demarre.

---

## Depannage

### Connexion WebSocket echoue

| Symptome | Cause probable | Solution |
|----------|---------------|----------|
| "Welcome timeout" sur PC B | Firewall bloque le port 8080 | Verifier les regles firewall PC A |
| Connexion refuse | Serveur pas lance | Verifier `node server.js` tourne |
| ERR_CONNECTION_REFUSED | Mauvaise IP | Verifier avec `ipconfig` sur PC A |
| WSL2 inaccessible depuis PC B | Mode mirrored pas actif | Verifier `.wslconfig` et redemarrer WSL |

**Debug rapide :**

```powershell
# Depuis PC B, tester si le port est accessible
Test-NetConnection 192.168.0.37 -Port 8080
```

### Voice chat ne fonctionne pas

| Symptome | Cause probable | Solution |
|----------|---------------|----------|
| Pas d'audio entre les PCs | TURN pas configure dans Unity | Verifier VoiceChatManager Inspector |
| "ICE failed" dans les logs Unity | coturn pas accessible | Tester avec Trickle ICE (navigateur) |
| Audio unidirectionnel | Firewall bloque UDP | Ouvrir ports 49152-65535 UDP |
| Pas de candidat relay | coturn mal configure | Verifier `external-ip=192.168.0.37` dans turnserver.conf |

**Debug coturn :**

```bash
# Verifier que coturn tourne
sudo systemctl status coturn

# Lire les logs
sudo tail -50 /var/log/turnserver.log

# Tester en local
turnutils_uclient -u vrmeeting -w testpassword123 127.0.0.1

# Redemarrer si necessaire
sudo systemctl restart coturn
```

### pdf-poppler Linux

Le module `pdf-poppler` (utilise pour la conversion PDF dans le file sharing) ne fonctionne **que sous Windows**. Sur Linux (WSL2 ou VM), le serveur crashe au demarrage avec `linux is NOT supported`.

**Solution : renommer le fichier pour le desactiver temporairement**

```bash
cd /mnt/d/Test_project/WebSocket_VR/Server
mv filePresentation.js filePresentation.js.bak
node server.js
```

Le serveur demarrera normalement. Seule la fonctionnalite de presentation PDF sera desactivee. Toutes les autres fonctions (WebSocket, rooms, position sync, whiteboard, voice chat, file sharing basique) fonctionneront.

> **Pour reactiver** (quand le serveur tourne sous Windows) :
> ```bash
> mv filePresentation.js.bak filePresentation.js
> ```

### WSL2 specifique

| Symptome | Cause probable | Solution |
|----------|---------------|----------|
| IP WSL2 change au reboot | Normal en mode NAT | Utiliser le mode mirrored |
| Ports pas accessibles | Mode NAT au lieu de mirrored | Verifier `.wslconfig` |
| coturn ne demarre pas | systemd pas actif dans WSL2 | Voir ci-dessous |
| Erreur "알 수 없는 키" (cle inconnue) | BOM dans `.wslconfig` | Recreer avec PowerShell (etape 1) |

**Activer systemd dans WSL2** (si pas actif) :

```bash
# Verifier
ps -p 1 -o comm=
# Si "init" au lieu de "systemd", il faut l'activer
```

Editer `/etc/wsl.conf` :

```ini
[boot]
systemd=true
```

Puis redemarrer WSL :

```powershell
wsl --shutdown
wsl
```

**Alternative sans systemd** - lancer coturn manuellement :

```bash
sudo turnserver -c /etc/turnserver.conf
```

### Nettoyage des regles firewall (apres les tests)

```powershell
# PowerShell admin - supprimer toutes les regles creees
netsh advfirewall firewall delete rule name="VR Meeting - WebSocket"
netsh advfirewall firewall delete rule name="VR Meeting - TURN TCP"
netsh advfirewall firewall delete rule name="VR Meeting - TURN UDP"
netsh advfirewall firewall delete rule name="VR Meeting - TURN TLS"
netsh advfirewall firewall delete rule name="VR Meeting - TURN Relay"
```

---

## Resume des commandes

### Demarrage rapide (WSL2 Mirrored)

```bash
# Terminal 1 - WSL2 : coturn
sudo systemctl start coturn

# Terminal 2 - WSL2 : serveur WebSocket
cd /mnt/d/Test_project/WebSocket_VR/Server
node server.js
```

### Demarrage rapide (VM)

```bash
# Dans la VM :
sudo systemctl start coturn
cd ~/Server && node server.js
```

### Arret

```bash
# Serveur WebSocket : Ctrl+C dans le terminal

# coturn
sudo systemctl stop coturn
```

---

## Changelog

| Date | Version | Description |
|------|---------|-------------|
| 2026-02-02 | 1.1 | IP concrete 192.168.0.37, workaround pdf-poppler Linux, correction PowerShell encoding |
| 2026-02-02 | 1.0 | Guide initial : WSL2 mirrored, VM bridge, LAN simple, coturn, depannage |

---

## References

- [GUIDE_DEPLOIEMENT_ENTREPRISE.md](./GUIDE_DEPLOIEMENT_ENTREPRISE.md) - Guide deploiement production (FR)
- [ENTERPRISE_DEPLOYMENT_GUIDE.md](./ENTERPRISE_DEPLOYMENT_GUIDE.md) - Production deployment guide (EN)
- [SERVER_ARCHITECTURE.md](./SERVER_ARCHITECTURE.md) - Architecture serveur
- [CLAUDE.md](../CLAUDE.md) - Instructions projet
- [coturn GitHub](https://github.com/coturn/coturn) - Documentation officielle coturn
- [Trickle ICE](https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/) - Outil de test WebRTC
