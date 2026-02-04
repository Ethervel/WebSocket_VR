# Guide de Deploiement LAN - VR Meeting Rooms

## Test realiste avec VM Linux (Bridge) sur PC-A + PC-B client

---

## Architecture

```
PC-A (hote Windows)                          PC-B (client Windows)
+------------------------------------------+ +--------------------+
|                                          | |                    |
|  VM VirtualBox (Ubuntu Desktop)          | |  Unity Build .exe  |
|  +------------------------------------+  | |  (client 2)        |
|  |  Ubuntu Desktop 24.04 LTS          |  | |                    |
|  |  Node.js v20 LTS                   |  | +--------------------+
|  |  WebSocket server :8080            |  |
|  |  IP: 192.168.1.70 (bridge)        |  |
|  +------------------------------------+  |
|                                          |
|  Unity Build .exe (client 1)             |
|  IP: 192.168.1.50                        |
+------------------------------------------+

        Reseau LAN (192.168.1.x)
        La VM a sa propre IP comme un vrai PC
```

En mode **Bridge**, la VM recoit sa propre adresse IP du routeur.
PC-B la voit comme une machine independante sur le reseau.
C'est le scenario le plus proche d'un vrai serveur Linux en production.

---

## Partie 1 : Creer la VM Linux

### 1.1 Telecharger les outils

- **VirtualBox** : https://www.virtualbox.org/wiki/Downloads (installer la version Windows)
- **Ubuntu Desktop 24.04 LTS ISO** : https://ubuntu.com/download/desktop

### 1.2 Creer la VM dans VirtualBox

1. Ouvrir VirtualBox > **Nouvelle**
2. Remplir :
   - Nom : `VR-Server`
   - Type : **Linux**
   - Version : **Ubuntu (64-bit)**
3. Ressources :
   - RAM : **4096 Mo** (4 Go, necessaire pour l'interface graphique)
   - Processeurs : **2**
   - Disque dur : **Creer un disque virtuel**, **25 Go**, format VDI
4. Cliquer **Terminer**

### 1.3 Monter l'ISO Ubuntu

1. Selectionner la VM `VR-Server` > **Configuration**
2. **Stockage** > cliquer sur le CD vide > icone CD a droite > **Choisir un fichier**
3. Selectionner le fichier `ubuntu-24.04.x-desktop-amd64.iso` telecharge
4. OK

### 1.4 Configurer le reseau en Bridge (CRITIQUE)

C'est l'etape la plus importante. Sans ca, PC-B ne pourra pas voir la VM.

1. VM `VR-Server` > **Configuration** > **Reseau**
2. **Adaptateur 1** :
   - Cocher : **Activer la carte reseau**
   - Mode d'acces reseau : **Acces par pont (Bridged Adapter)**
   - Nom : selectionner votre **carte reseau physique**
     - Si branche en cable : choisir `Intel Ethernet` / `Realtek Ethernet` / etc.
     - Si en Wi-Fi : choisir votre adaptateur Wi-Fi
3. OK

```
AVANT (defaut) :                      APRES (bridge) :
+----------------------------+        +----------------------------+
| Mode : NAT                 |        | Mode : Acces par pont      |
|                            |        | Nom  : Intel Ethernet I219 |
+----------------------------+        +----------------------------+
```

> **Attention Wi-Fi** : Certains adaptateurs Wi-Fi ne supportent pas le bridge.
> Si ca ne marche pas en Wi-Fi, branchez PC-A en cable Ethernet.

### 1.5 Installer Ubuntu Desktop

1. Demarrer la VM
2. L'ecran GRUB s'affiche : selectionner **"Try or Install Ubuntu"** et appuyer sur Entree
3. L'installateur graphique se lance. Suivre les etapes :
   - Langue : **Francais** (ou English)
   - Clavier : **French**
   - Se connecter a internet : normalement le reseau est detecte automatiquement via le bridge
   - Cliquer **Installer Ubuntu**
   - Type d'installation : **Installation normale** (laisser par defaut)
   - Type d'installation du disque : **Effacer le disque et installer Ubuntu** (c'est la VM, pas votre vrai disque)
   - Fuseau horaire : choisir le votre
   - Creer un compte :
     - Nom : `VR Admin`
     - Nom de l'ordinateur : `vr-server`
     - Nom d'utilisateur : `vr-admin`
     - Mot de passe : choisir un mot de passe
4. Attendre la fin de l'installation (~10-15 min)
5. Cliquer **Redemarrer maintenant** quand demande
6. Si un message dit "Please remove the installation medium" → appuyer sur Entree
   (VirtualBox retire l'ISO automatiquement en general)

### 1.6 Premier demarrage et installation de SSH

Apres le reboot, Ubuntu Desktop se lance avec une interface graphique.

1. Se connecter avec le mot de passe choisi
2. Fermer les fenetres de bienvenue / mises a jour
3. Ouvrir un **Terminal** :
   - Clic droit sur le bureau > **Ouvrir un terminal**
   - Ou chercher "Terminal" dans les applications (icone grille en bas a gauche)
4. Installer le serveur SSH (necessaire pour copier les fichiers depuis PC-A) :

```bash
sudo apt update
sudo apt install -y openssh-server
sudo systemctl enable ssh
sudo systemctl start ssh
```

5. Verifier que SSH fonctionne :

```bash
sudo systemctl status ssh    # doit afficher "active (running)"
```

---

## Partie 2 : Configurer le serveur dans la VM

> Toutes les commandes de cette partie sont a executer dans le **Terminal** Ubuntu Desktop.
> Pour l'ouvrir : clic droit sur le bureau > "Ouvrir un terminal", ou raccourci `Ctrl+Alt+T`.

### 2.1 Verifier l'IP de la VM

```bash
ip addr show
```

Chercher la ligne `inet 192.168.x.x` sur l'interface `enp0s3` (ou similaire).

Exemple :

```
2: enp0s3: <BROADCAST,MULTICAST,UP,LOWER_UP>
    inet 192.168.1.70/24 brd 192.168.1.255 scope global dynamic enp0s3
```

**Notez cette IP** (ici `192.168.1.70`). C'est l'adresse du serveur.

> **Verifier le bridge** : l'IP doit etre dans le meme sous-reseau que PC-A et PC-B
> (ex: tous en 192.168.1.x). Si l'IP est en 10.0.2.x, le bridge n'est pas actif.

**Methode alternative (graphique)** : cliquer sur l'icone reseau en haut a droite de l'ecran >
Parametres filaires > l'adresse IPv4 est affichee.

### 2.2 Installer Node.js et poppler-utils

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo bash -
sudo apt install -y nodejs poppler-utils

# Verifier
node --version    # v20.x.x
npm --version
which pdftoppm    # /usr/bin/pdftoppm
```

### 2.3 Copier le dossier Server dans la VM

**Option A : Via SCP (depuis PC-A, PowerShell Windows)**

```powershell
scp -r "D:\Test_project\WebSocket_VR\Server" vr-admin@192.168.1.70:~/vr-meeting/
```

> Si SCP demande de confirmer le fingerprint, taper `yes`.
> Entrer le mot de passe de la VM quand demande.

**Option B : Dossier partage VirtualBox (glisser-deposer)**

1. Dans VirtualBox, menu **Peripheriques** > **Installer les Additions invite...**
2. Dans la VM, un CD apparait sur le bureau. L'ouvrir et lancer le script :

```bash
sudo apt install -y build-essential dkms linux-headers-$(uname -r)
cd /media/$USER/VBox_GAs_*
sudo ./VBoxLinuxAdditions.run
sudo reboot
```

3. Apres reboot, dans VirtualBox : **Peripheriques** > **Presse-papiers partage** > **Bidirectionnel**
4. VM eteinte > Configuration > **Dossiers partages** > cliquer le + :
   - Chemin du dossier : `D:\Test_project\WebSocket_VR\Server`
   - Nom du dossier : `server`
   - Cocher : **Montage automatique**
5. Ajouter l'utilisateur au groupe de partage, puis reboot :

```bash
sudo adduser $USER vboxsf
sudo reboot
```

6. Copier les fichiers :

```bash
cp -r /media/sf_server ~/vr-meeting/Server
```

**Option C : Via Git (si le repo est accessible)**

Ouvrir le Terminal ou Firefox dans la VM :

```bash
sudo apt install -y git
git clone <url-du-repo> ~/vr-meeting
```

### 2.4 Installer les dependances Node.js

```bash
cd ~/vr-meeting/Server
npm install
```

> Le package `pdf-poppler` peut afficher une erreur. C'est normal.
> Le code detecte Linux et utilise `pdftoppm` automatiquement.

### 2.5 Tester le lancement

```bash
cd ~/vr-meeting/Server
npm start
```

Vous devez voir :

```
============================================
  VR MEETING ROOMS - WebSocket Server
============================================
  Port: 8080
  Heartbeat: 30s
============================================
[PDFModule] pdftoppm found (Linux/macOS backend)
```

Arretez avec `Ctrl+C` apres verification.

### 2.6 Ouvrir le pare-feu de la VM

```bash
sudo ufw allow 8080/tcp
sudo ufw allow 22/tcp      # pour garder l'acces SSH
sudo ufw enable
sudo ufw status
```

### 2.7 Verifier l'acces depuis PC-A et PC-B

```powershell
# PowerShell sur PC-A (Windows, pas la VM)
Test-NetConnection -ComputerName 192.168.1.70 -Port 8080

# PowerShell sur PC-B
Test-NetConnection -ComputerName 192.168.1.70 -Port 8080
```

Les deux doivent afficher `TcpTestSucceeded : True`.

**Si PC-A reussit mais pas PC-B** → le pare-feu Windows de PC-A bloque peut-etre.
En mode Bridge ce n'est normalement pas le cas (la VM a sa propre IP),
mais si le reseau passe par PC-A physiquement, ouvrir aussi le pare-feu Windows :

```powershell
# PowerShell admin sur PC-A (seulement si necessaire)
netsh advfirewall firewall add rule name="VR Meeting Server VM" dir=in action=allow protocol=TCP localport=8080
```

---

## Partie 3 : Lancer le serveur en tant que service

Pour que le serveur tourne en arriere-plan et survive a la fermeture du terminal.

### 3.1 Creer le service systemd

```bash
sudo nano /etc/systemd/system/vr-meeting.service
```

Coller (adapter le user si different de `vr-admin`) :

```ini
[Unit]
Description=VR Meeting WebSocket Server
After=network.target

[Service]
Type=simple
User=vr-admin
WorkingDirectory=/home/vr-admin/vr-meeting/Server
ExecStart=/usr/bin/node server.js
Restart=always
RestartSec=5
Environment=PORT=8080

# Logs
StandardOutput=journal
StandardError=journal
SyslogIdentifier=vr-meeting

[Install]
WantedBy=multi-user.target
```

Sauvegarder : `Ctrl+O`, `Entree`, `Ctrl+X`.

### 3.2 Activer et demarrer

```bash
sudo systemctl daemon-reload
sudo systemctl enable vr-meeting      # demarrage automatique au boot de la VM
sudo systemctl start vr-meeting       # lancer maintenant
sudo systemctl status vr-meeting      # verifier : active (running)
```

### 3.3 Commandes utiles

```bash
sudo systemctl status vr-meeting      # etat du service
sudo systemctl stop vr-meeting        # arreter
sudo systemctl restart vr-meeting     # redemarrer
journalctl -u vr-meeting -f           # logs en temps reel
journalctl -u vr-meeting --since "1 hour ago"
```

---

## Partie 4 : Configurer Unity et builder

### 4.1 Changer l'adresse du serveur

1. Ouvrir le projet Unity sur PC-A
2. Ouvrir la scene `Assets/Scenes/Bootstrap.unity`
3. Selectionner le GameObject contenant `VRNetworkManager`
4. Dans l'Inspector, modifier le champ **Server Url** :

```
ws://192.168.1.70:8080
```

> Remplacez `192.168.1.70` par l'IP reelle de la VM (etape 2.1).

### 4.2 Builder le projet

1. **File > Build Settings**
2. Verifier l'ordre des scenes :
   - Scene 0 : `Bootstrap`
   - Scene 1 : `Meet`
3. **Platform : Windows**
4. Cliquer **Build**
5. Choisir un dossier de destination (ex : `Build/`)

### 4.3 Copier le build sur PC-B

Le build genere un dossier contenant :

```
Build/
  VRMeeting.exe
  VRMeeting_Data/
  UnityPlayer.dll
  MonoBleedingEdge/
```

**Copiez le dossier complet** sur PC-B via :
- Cle USB
- Partage reseau
- Ou tout autre moyen

> L'executable seul ne suffit pas. Tout le dossier est necessaire.

---

## Partie 5 : Lancer le test

### 5.1 Ordre de lancement

```
Etape 1 : VM Linux (sur PC-A)
    Le service tourne deja (systemd)
    Verifier : sudo systemctl status vr-meeting

Etape 2 : PC-A (Windows)
    Lancer VRMeeting.exe

Etape 3 : PC-B (Windows)
    Lancer VRMeeting.exe
```

### 5.2 Scenario de test pas a pas

```
PC-A (Windows)                          PC-B (Windows)
--------------                          --------------
1. Lancer VRMeeting.exe                1. Lancer VRMeeting.exe
2. Verifier la connexion au serveur    2. Verifier la connexion au serveur
   (pas d'erreur affichee)                (pas d'erreur affichee)

3. Creer une room
4. Noter le code (6 caracteres)
                                        3. Rejoindre avec le code room

5. Verifier : PC-B apparait            4. Verifier : PC-A apparait

6. Parler dans le micro                5. Confirmer : j'entends PC-A
                                        6. Parler dans le micro
7. Confirmer : j'entends PC-B

8. Dessiner sur le whiteboard          7. Verifier : le dessin apparait
                                        8. Dessiner sur le whiteboard
9. Verifier : le dessin apparait

10. Partager l'ecran                   9. Verifier l'affichage ecran

11. Partager un fichier                10. Verifier la reception

12. Activer le laser pointer           11. Verifier : le laser est visible

13. Fermer VRMeeting.exe               12. Verifier : PC-A disparait
14. Relancer VRMeeting.exe             13. Verifier : PC-A reapparait
```

### 5.3 Surveiller les logs du serveur pendant le test

**Option A : Directement dans la VM (plus simple avec Desktop)**

Ouvrir un Terminal dans Ubuntu Desktop (`Ctrl+Alt+T`) :

```bash
journalctl -u vr-meeting -f
```

> Laisser cette fenetre ouverte a cote pendant les tests.

**Option B : Via SSH depuis PC-A (PowerShell)**

```bash
ssh vr-admin@192.168.1.70
journalctl -u vr-meeting -f
```

Cela affiche en temps reel les connexions, messages et erreurs.

---

## Partie 6 : Checklist de validation

### VM et infrastructure

- [ ] VirtualBox installe sur PC-A
- [ ] VM creee avec Ubuntu Desktop 24.04
- [ ] Reseau VM en mode **Bridge** (pas NAT)
- [ ] VM a une IP en 192.168.x.x (meme sous-reseau que PC-A et PC-B)
- [ ] OpenSSH server installe dans la VM
- [ ] Node.js v20 installe dans la VM
- [ ] poppler-utils installe dans la VM
- [ ] Dossier Server copie dans la VM
- [ ] npm install execute sans erreur bloquante
- [ ] Service systemd cree et actif
- [ ] Port 8080 ouvert (ufw)
- [ ] PC-A peut atteindre la VM sur le port 8080
- [ ] PC-B peut atteindre la VM sur le port 8080

### Connexion

- [ ] PC-A se connecte au serveur (message "welcome" recu)
- [ ] PC-B se connecte au serveur (message "welcome" recu)
- [ ] Les logs serveur affichent 2 clients connectes

### Rooms

- [ ] Creation d'une room depuis PC-A
- [ ] PC-B rejoint la room avec le code 6 caracteres
- [ ] Les 2 joueurs se voient dans la room

### Synchronisation

- [ ] Les mouvements de tete sont synchronises
- [ ] Les mouvements de mains sont synchronises (mode VR)
- [ ] Les mouvements sont fluides (pas de saccades)
- [ ] La latence est acceptable (< 100ms sur LAN)

### Voice Chat (WebRTC)

- [ ] Le voice chat s'initialise correctement
- [ ] PC-A entend PC-B
- [ ] PC-B entend PC-A
- [ ] L'audio spatial fonctionne (son directionnel)
- [ ] Push-to-talk fonctionne (touche V en Desktop)

### Whiteboard

- [ ] PC-A dessine → PC-B voit le dessin
- [ ] PC-B dessine → PC-A voit le dessin
- [ ] Clear fonctionne des 2 cotes
- [ ] Late joiner recoit l'etat du whiteboard

### Partage d'ecran

- [ ] PC-A partage son ecran
- [ ] PC-B voit le partage sur le whiteboard (mode presentation)
- [ ] La qualite est lisible

### Partage de fichiers

- [ ] Envoi d'un fichier PDF
- [ ] Envoi d'une image (png/jpg)
- [ ] Le fichier est recu par l'autre client
- [ ] La presentation de fichier fonctionne

### Laser Pointer

- [ ] Le laser de PC-A est visible par PC-B
- [ ] Le laser de PC-B est visible par PC-A

### Robustesse

- [ ] Deconnexion propre : l'autre client est notifie
- [ ] Deconnexion brutale (kill process) : notification apres timeout heartbeat (30s)
- [ ] Reconnexion : le client peut rejoindre a nouveau
- [ ] Le serveur survit a la deconnexion de tous les clients
- [ ] Arret et redemarrage de la VM : le service redemarre automatiquement

---

## Partie 7 : Depannage

### La VM n'a pas d'IP en 192.168.x.x

Le mode Bridge n'est pas actif ou ne fonctionne pas.

```bash
# Dans la VM
ip addr show
```

Si l'IP est en `10.0.2.x` → c'est du NAT, pas du Bridge.

**Solutions :**
1. Eteindre la VM
2. Configuration > Reseau > verifier "Acces par pont"
3. Changer l'adaptateur (essayer Ethernet au lieu de Wi-Fi)
4. Redemarrer la VM

### PC-B ne peut pas atteindre la VM

```powershell
# Sur PC-B
ping 192.168.1.70
Test-NetConnection -ComputerName 192.168.1.70 -Port 8080
```

**Si le ping echoue :**
- PC-A et PC-B ne sont pas sur le meme reseau
- Le pare-feu d'entreprise bloque les communications entre postes

**Si le ping passe mais pas le port 8080 :**
- Le serveur n'est pas lance : `sudo systemctl status vr-meeting`
- Le pare-feu de la VM bloque : `sudo ufw status`
- Verifier que le port ecoute : `ss -tlnp | grep 8080`

### Le client Unity affiche une erreur de connexion

- Verifier le champ `serverUrl` dans VRNetworkManager : doit etre `ws://IP_VM:8080`
- Verifier que l'IP n'a pas change (DHCP peut reassigner)
- Pour fixer l'IP de la VM :

```bash
# Dans la VM, editer la config netplan
sudo nano /etc/netplan/00-installer-config.yaml
```

```yaml
network:
  version: 2
  ethernets:
    enp0s3:
      dhcp4: no
      addresses:
        - 192.168.1.70/24
      routes:
        - to: default
          via: 192.168.1.1
      nameservers:
        addresses:
          - 8.8.8.8
          - 8.8.4.4
```

```bash
sudo netplan apply
```

> Adaptez l'adresse IP et la gateway (`via`) a votre reseau.

### Le voice chat ne fonctionne pas

Le WebRTC utilise des serveurs STUN publics (Google, Cloudflare).
Les 2 PCs clients doivent avoir acces a internet.

**Si pas d'internet :**
Les ICE candidates locales seront utilisees en LAN. Ca devrait marcher,
mais testez-le.

**Si le son ne passe pas :**
- Verifier les autorisations microphone dans Windows
- Push-to-talk : touche V en mode Desktop
- Verifier les logs Unity pour des erreurs WebRTC

### Les PDFs ne se convertissent pas

```bash
# Dans la VM
which pdftoppm    # doit afficher /usr/bin/pdftoppm
journalctl -u vr-meeting | grep PDFModule
```

### Le serveur crash

```bash
journalctl -u vr-meeting --since "10 minutes ago"

# Le service redemarre automatiquement toutes les 5 secondes
sudo systemctl status vr-meeting
```

---

## Partie 8 : Simulation de latence reseau (optionnel)

Pour simuler un serveur distant (cloud) au lieu d'un LAN rapide.
A executer **dans la VM** :

```bash
# Trouver le nom de l'interface reseau
ip link show
# Generalement : enp0s3

# Ajouter 50ms de latence + 1% de perte de paquets
sudo tc qdisc add dev enp0s3 root netem delay 50ms loss 1%

# Verifier
tc qdisc show dev enp0s3

# Supprimer la simulation quand termine
sudo tc qdisc del dev enp0s3 root netem
```

**Valeurs de reference :**

| Scenario              | Latence  | Perte |
|-----------------------|----------|-------|
| LAN local             | 1-5ms    | 0%    |
| Serveur national      | 20-50ms  | 0.1%  |
| Serveur europeen      | 30-80ms  | 0.5%  |
| Serveur international | 100-200ms| 1-2%  |

---

## Partie 9 : Configuration production-like (nginx + SSL + coturn)

Cette partie transforme la VM en un serveur identique a la production.
Apres cette configuration, les clients se connectent en `wss://` (chiffre)
et le voice chat passe par un vrai serveur TURN.

```
Architecture finale :

Client Unity (PC-A ou PC-B)
    |
    | wss://meeting.local:443
    v
+---nginx (port 443, TLS)---+
|   Certificat SSL (mkcert)  |
|   → proxy vers Node.js    |
+----------------------------+
    |
    v
+---Node.js :8080-----------+
|   WebSocket server         |
+----------------------------+

+---coturn-------------------+
|   :3478  UDP/TCP (STUN)    |
|   :5349  TCP     (TURNS)   |
|   49152-65535 UDP (relay)  |
|   Certificat SSL (mkcert)  |
+----------------------------+
```

### 9.1 Installer mkcert (certificats SSL locaux de confiance)

mkcert cree des certificats SSL reconnus par les navigateurs et applications
sans les avertissements de securite des certificats auto-signes.

**Dans la VM (Terminal `Ctrl+Alt+T`) :**

```bash
# Installer mkcert
sudo apt install -y libnss3-tools
curl -JLO "https://dl.filippo.io/mkcert/latest?for=linux/amd64"
chmod +x mkcert-v*-linux-amd64
sudo mv mkcert-v*-linux-amd64 /usr/local/bin/mkcert

# Installer l'autorite de certification locale
mkcert -install

# Generer les certificats pour meeting.local
mkdir -p ~/certs
cd ~/certs
mkcert meeting.local 192.168.1.70 localhost 127.0.0.1
```

Cela cree 2 fichiers :
- `meeting.local+3.pem` (certificat)
- `meeting.local+3-key.pem` (cle privee)

**Copier le certificat racine sur les PCs clients (IMPORTANT) :**

Pour que les clients Unity acceptent le certificat, il faut installer
l'autorite de certification de mkcert sur chaque PC client.

```bash
# Dans la VM, trouver le certificat racine
mkcert -CAROOT
# Affiche le chemin, ex: /home/vr-admin/.local/share/mkcert
```

Copier le fichier `rootCA.pem` depuis ce dossier vers les PCs clients :

```bash
# Depuis PC-A ou PC-B (PowerShell)
scp vr-admin@192.168.1.70:~/.local/share/mkcert/rootCA.pem C:\Temp\rootCA.pem
```

Puis sur chaque PC client Windows :
1. Double-cliquer sur `rootCA.pem`
2. **Installer le certificat** > **Ordinateur local** > **Suivant**
3. **Placer dans le magasin** : **Autorites de certification racines de confiance**
4. **Terminer**

### 9.2 Installer et configurer nginx

```bash
sudo apt install -y nginx

# Copier les certificats pour nginx
sudo mkdir -p /etc/nginx/ssl
sudo cp ~/certs/meeting.local+3.pem /etc/nginx/ssl/cert.pem
sudo cp ~/certs/meeting.local+3-key.pem /etc/nginx/ssl/key.pem
sudo chmod 600 /etc/nginx/ssl/key.pem
```

Creer la config nginx :

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

Coller :

```nginx
server {
    listen 443 ssl;
    server_name meeting.local;

    ssl_certificate     /etc/nginx/ssl/cert.pem;
    ssl_certificate_key /etc/nginx/ssl/key.pem;

    # WebSocket proxy
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Timeouts pour WebSocket longue duree
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }
}

# Redirection HTTP → HTTPS
server {
    listen 80;
    server_name meeting.local;
    return 301 https://$host$request_uri;
}
```

Sauvegarder (`Ctrl+O`, `Entree`, `Ctrl+X`), puis activer :

```bash
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo rm /etc/nginx/sites-enabled/default
sudo nginx -t          # doit afficher "syntax is ok"
sudo systemctl restart nginx
sudo systemctl enable nginx
```

### 9.3 Installer et configurer coturn

```bash
sudo apt install -y coturn
```

Activer coturn comme service :

```bash
sudo nano /etc/default/coturn
```

Decommenter (enlever le #) la ligne :

```
TURNSERVER_ENABLED=1
```

Configurer coturn :

```bash
sudo nano /etc/turnserver.conf
```

Remplacer tout le contenu par :

```ini
# Nom du serveur
realm=meeting.local
server-name=meeting.local

# Ports
listening-port=3478
tls-listening-port=5349

# IP d'ecoute (remplacer par l'IP reelle de la VM)
listening-ip=0.0.0.0
relay-ip=192.168.1.70

# Plage de ports UDP pour le relay media
min-port=49152
max-port=65535

# Certificats SSL (memes que nginx)
cert=/etc/nginx/ssl/cert.pem
pkey=/etc/nginx/ssl/key.pem

# Authentification
lt-cred-mech
user=vrmeeting:TurnPassword123!

# Securite
fingerprint
no-cli
no-tlsv1
no-tlsv1_1

# Logs
log-file=/var/log/turnserver.log
verbose
```

> **Changez `192.168.1.70`** par l'IP reelle de votre VM.
> **Changez `TurnPassword123!`** par un mot de passe de votre choix.

Demarrer coturn :

```bash
sudo systemctl restart coturn
sudo systemctl enable coturn
sudo systemctl status coturn    # verifier : active (running)
```

Verifier que coturn ecoute :

```bash
ss -tlnp | grep turnserver
ss -ulnp | grep turnserver
```

Vous devez voir les ports 3478 et 5349.

### 9.4 Ouvrir les ports dans le pare-feu de la VM

```bash
# nginx
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# coturn
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 49152:65535/udp

# Verifier
sudo ufw status
```

### 9.5 Configurer le fichier hosts sur les PCs clients

Sur **chaque PC client** (PC-A et PC-B), ouvrir un **Bloc-notes en administrateur** :

1. Menu Demarrer > chercher "Bloc-notes" > **clic droit** > **Executer en tant qu'administrateur**
2. Fichier > Ouvrir > aller a `C:\Windows\System32\drivers\etc\`
3. En bas a droite, changer "Documents texte" en **"Tous les fichiers"**
4. Ouvrir le fichier `hosts`
5. Ajouter a la fin :

```
192.168.1.70    meeting.local
```

6. Sauvegarder

> Remplacez `192.168.1.70` par l'IP reelle de la VM.

Verifier depuis PowerShell :

```powershell
ping meeting.local
```

Doit repondre avec l'IP de la VM.

### 9.6 Configurer Unity pour le mode production

Dans la scene `Bootstrap.unity`, modifier les composants :

**VRNetworkManager (Inspector) :**

```
Server Url            = wss://meeting.local
Enforce Secure Conn.  = true      (optionnel, pour tester le blocage ws://)
```

**VoiceChatManager (Inspector) :**

```
Use Custom Turn Server    = true
Custom Turn Url           = turn:meeting.local:3478
Custom Turn Username      = vrmeeting
Custom Turn Credential    = TurnPassword123!
Enable Turn Tcp           = true
```

> Utilisez le meme user/password que dans `/etc/turnserver.conf`.

### 9.7 Verifier que tout fonctionne

**Depuis un PC client (PowerShell) :**

```powershell
# DNS local
ping meeting.local

# nginx HTTPS
Test-NetConnection -ComputerName meeting.local -Port 443

# coturn STUN
Test-NetConnection -ComputerName meeting.local -Port 3478
```

**Dans la VM :**

```bash
# Tous les services actifs
sudo systemctl status nginx
sudo systemctl status vr-meeting
sudo systemctl status coturn

# Logs en temps reel (ouvrir 3 terminaux)
journalctl -u vr-meeting -f          # Terminal 1 : Node.js
sudo tail -f /var/log/turnserver.log  # Terminal 2 : coturn
sudo tail -f /var/log/nginx/error.log # Terminal 3 : nginx
```

---

## Partie 10 : Securite (notes pour le vrai deploiement)

Ce guide simule la production en LAN. Pour le vrai deploiement sur internet :

| Element          | Test VM (ce guide)               | Production serveur entreprise      |
|------------------|----------------------------------|------------------------------------|
| Domaine          | `meeting.local` (fichier hosts)  | `meeting.entreprise.com` (DNS)     |
| Certificat SSL   | mkcert (local)                   | Let's Encrypt (auto-renouvele)     |
| nginx            | Identique                        | Identique + rate limiting          |
| coturn           | Identique                        | Identique + auth dynamique         |
| Authentification | Aucune                           | JWT + base de donnees (Phase 3)    |
| Port expose      | 443 via nginx                    | 443 via nginx                      |
| Firewall         | ufw                              | ufw + firewall entreprise          |
| Logs             | journalctl + fichiers            | Centralise (ELK, Grafana)          |

**Pour passer de ce test a la production, il suffit de :**
1. Remplacer `meeting.local` par le vrai domaine
2. Remplacer mkcert par Let's Encrypt (`sudo certbot --nginx`)
3. Ouvrir les ports sur le firewall de l'entreprise
4. Memes fichiers de config nginx et coturn

---

## Resume rapide des commandes

### Dans la VM Linux

```bash
# === Installation de base (une seule fois) ===
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo bash -
sudo apt install -y nodejs poppler-utils
cd ~/vr-meeting/Server && npm install

# === Service Node.js ===
sudo systemctl enable vr-meeting
sudo systemctl start vr-meeting

# === nginx + SSL ===
sudo apt install -y nginx
sudo systemctl enable nginx
sudo systemctl start nginx

# === coturn ===
sudo apt install -y coturn
sudo systemctl enable coturn
sudo systemctl start coturn

# === Pare-feu ===
sudo ufw allow 22/tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 49152:65535/udp
sudo ufw enable

# === Monitoring ===
sudo systemctl status nginx vr-meeting coturn
journalctl -u vr-meeting -f
sudo tail -f /var/log/turnserver.log
```

### Sur les PCs clients

```powershell
# Fichier hosts (Bloc-notes en admin)
# Ajouter dans C:\Windows\System32\drivers\etc\hosts :
# 192.168.1.70    meeting.local

# Tester
ping meeting.local
Test-NetConnection -ComputerName meeting.local -Port 443
Test-NetConnection -ComputerName meeting.local -Port 3478
```

### Dans Unity (Inspector)

```
VRNetworkManager :
    Server Url            = wss://meeting.local

VoiceChatManager :
    Use Custom Turn Server    = true
    Custom Turn Url           = turn:meeting.local:3478
    Custom Turn Username      = vrmeeting
    Custom Turn Credential    = TurnPassword123!
    Enable Turn Tcp           = true
```

### Checklist production-like (supplement)

- [ ] mkcert installe et certificats generes
- [ ] Certificat racine (rootCA.pem) installe sur PC-A et PC-B
- [ ] nginx installe, configure et actif
- [ ] coturn installe, configure et actif
- [ ] Ports 443, 3478, 5349, 49152-65535 ouverts dans ufw
- [ ] Fichier hosts configure sur PC-A et PC-B
- [ ] `ping meeting.local` repond depuis PC-A et PC-B
- [ ] Unity serverUrl = `wss://meeting.local`
- [ ] Unity TURN custom configure
- [ ] Connexion WSS fonctionne (pas d'erreur SSL)
- [ ] Voice chat passe par coturn (verifier logs turnserver)
- [ ] Les 2 clients communiquent via wss:// + TURN
