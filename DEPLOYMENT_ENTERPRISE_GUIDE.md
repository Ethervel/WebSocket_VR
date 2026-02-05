# Guide de Deploiement Entreprise - VR Meeting Rooms

## Serveur accessible depuis n'importe quel reseau (Internet / multi-sites)

---

## Architecture

```
Clients (n'importe ou dans le monde)
    |
    | wss://meeting.entreprise.com:443
    v
+---Firewall Entreprise-----------+
|   NAT / Port forwarding         |
|   443, 3478, 5349, 49152-65535  |
+---------------------------------+
    |
    v
+---Serveur Linux (Ubuntu)--------+
|                                  |
|  +--nginx (port 443, TLS)-----+ |
|  |  Let's Encrypt auto-renew  | |
|  |  -> proxy vers Node.js     | |
|  +-----------------------------+ |
|                                  |
|  +--Node.js :8080-------------+ |
|  |  WebSocket server           | |
|  |  systemd managed            | |
|  +-----------------------------+ |
|                                  |
|  +--coturn--------------------+  |
|  |  :3478  UDP/TCP (STUN)     |  |
|  |  :5349  TCP     (TURNS)    |  |
|  |  49152-65535 UDP (relay)   |  |
|  |  Let's Encrypt certs       |  |
|  +-----------------------------+ |
+----------------------------------+
```

**Difference avec le deploiement LAN :**

| Element | LAN (VirtualBox) | Entreprise (ce guide) |
|---------|-------------------|-----------------------|
| Machine | VM sur un PC | Serveur dedie / VM entreprise |
| Domaine | `meeting.local` (fichier hosts) | `meeting.entreprise.com` (DNS public) |
| Certificat | mkcert (install manuelle par PC) | Let's Encrypt (reconnu partout) |
| IP | Privee LAN (192.168.x.x) | Publique ou NATtee |
| Fichier hosts clients | Oui | Non |
| Firewall | ufw seulement | ufw + firewall entreprise |

---

## Partie 1 : Pre-requis

### 1.1 Serveur

| Element | Minimum | Recommande |
|---------|---------|------------|
| OS | Ubuntu 22.04 LTS | Ubuntu 24.04 LTS |
| RAM | 1 Go | 2 Go |
| CPU | 1 vCPU | 2 vCPU |
| Disque | 10 Go | 20 Go |
| Reseau | 1 Mbps | 10 Mbps+ |
| IP | IP publique fixe (ou NAT avec ports ouverts) | IP publique fixe |

### 1.2 Nom de domaine

Vous devez disposer d'un nom de domaine qui pointe vers l'IP publique du serveur.

**Demander a votre equipe IT / hebergeur :**

```
Type : A
Nom  : meeting.entreprise.com
Valeur : IP_PUBLIQUE_DU_SERVEUR (ex: 203.0.113.50)
TTL  : 3600
```

> Si le serveur est derriere un NAT (ex: dans un datacenter prive), l'enregistrement DNS
> doit pointer vers l'IP publique du routeur/firewall, et les ports doivent etre rediriges
> vers le serveur interne.

**Verifier que le DNS fonctionne (depuis n'importe quel PC) :**

```bash
nslookup meeting.entreprise.com
# ou
ping meeting.entreprise.com
```

Doit repondre avec l'IP publique du serveur.

### 1.3 Ports a ouvrir

A communiquer a votre equipe reseau pour le firewall d'entreprise :

| Port | Protocole | Service | Pourquoi |
|------|-----------|---------|----------|
| 22 | TCP | SSH | Administration du serveur |
| 80 | TCP | HTTP | Renouvellement Let's Encrypt + redirection HTTPS |
| 443 | TCP | HTTPS / WSS | nginx reverse proxy (WebSocket chiffre) |
| 3478 | TCP + UDP | STUN / TURN | coturn - decouverte reseau et relay voix |
| 5349 | TCP | TURNS (TLS) | coturn - relay voix chiffre |
| 49152-65535 | UDP | Media relay | coturn - flux audio WebRTC |

> **Important :** si les ports 49152-65535 posent probleme (plage trop large),
> vous pouvez reduire a `49152-50000` dans la config coturn et n'ouvrir que cette plage.

---

## Partie 2 : Installation du serveur

> Toutes les commandes sont a executer sur le serveur Linux via SSH.

### 2.1 Se connecter au serveur

```bash
ssh admin@meeting.entreprise.com
# ou
ssh admin@203.0.113.50
```

### 2.2 Mise a jour du systeme

```bash
sudo apt update && sudo apt upgrade -y
```

### 2.3 Installer Node.js v20 LTS et poppler-utils

```bash
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo bash -
sudo apt install -y nodejs poppler-utils

# Verifier
node --version    # v20.x.x
npm --version
which pdftoppm    # /usr/bin/pdftoppm
```

### 2.4 Copier le dossier Server sur le serveur

**Option A : Via SCP (depuis votre PC Windows, PowerShell)**

```powershell
scp -r "D:\Test_project\WebSocket_VR\Server" admin@meeting.entreprise.com:~/vr-meeting/
```

**Option B : Via Git**

```bash
sudo apt install -y git
git clone <url-du-repo> ~/vr-meeting
```

### 2.5 Installer les dependances

```bash
cd ~/vr-meeting/Server
npm install
```

### 2.6 Tester le lancement

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
```

Arreter avec `Ctrl+C`.

---

## Partie 3 : Service systemd (Node.js)

### 3.1 Creer le service

```bash
sudo nano /etc/systemd/system/vr-meeting.service
```

Coller (adapter `User` et `WorkingDirectory` si besoin) :

```ini
[Unit]
Description=VR Meeting WebSocket Server
After=network.target

[Service]
Type=simple
User=admin
WorkingDirectory=/home/admin/vr-meeting/Server
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
sudo systemctl enable vr-meeting
sudo systemctl start vr-meeting
sudo systemctl status vr-meeting    # doit afficher "active (running)"
```

### 3.3 Commandes utiles

```bash
sudo systemctl status vr-meeting
sudo systemctl stop vr-meeting
sudo systemctl restart vr-meeting
journalctl -u vr-meeting -f                   # logs temps reel
journalctl -u vr-meeting --since "1 hour ago"
```

---

## Partie 4 : nginx + Let's Encrypt (HTTPS / WSS)

### 4.1 Installer nginx

```bash
sudo apt install -y nginx
sudo systemctl enable nginx
```

### 4.2 Creer la config nginx (HTTP d'abord)

Let's Encrypt a besoin que le domaine soit joignable en HTTP pour verifier la propriete.

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

Coller :

```nginx
server {
    listen 80;
    server_name meeting.entreprise.com;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }
}
```

Activer :

```bash
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl restart nginx
```

### 4.3 Installer Let's Encrypt avec certbot

```bash
sudo apt install -y certbot python3-certbot-nginx
```

### 4.4 Generer le certificat SSL

```bash
sudo certbot --nginx -d meeting.entreprise.com
```

Certbot va :
1. Verifier que le domaine pointe vers ce serveur (via HTTP)
2. Generer un certificat SSL gratuit
3. Modifier automatiquement la config nginx pour HTTPS
4. Configurer la redirection HTTP -> HTTPS

> **Si certbot echoue :** verifier que le port 80 est ouvert sur le firewall
> et que le DNS pointe bien vers ce serveur.

### 4.5 Verifier la config nginx generee

Apres certbot, la config devrait ressembler a ceci :

```bash
sudo cat /etc/nginx/sites-available/vr-meeting
```

```nginx
server {
    server_name meeting.entreprise.com;

    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
    }

    listen 443 ssl;
    ssl_certificate /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/meeting.entreprise.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;
}

server {
    if ($host = meeting.entreprise.com) {
        return 301 https://$host$request_uri;
    }

    listen 80;
    server_name meeting.entreprise.com;
    return 404;
}
```

> Certbot gere ca automatiquement. Vous n'avez rien a modifier.

### 4.6 Verifier le renouvellement automatique

Les certificats Let's Encrypt expirent tous les 90 jours. Certbot configure un timer systemd pour les renouveler automatiquement.

```bash
# Verifier que le timer est actif
sudo systemctl status certbot.timer

# Simuler un renouvellement (sans vraiment renouveler)
sudo certbot renew --dry-run
```

Si le dry-run reussit, les renouvellements se feront automatiquement.

---

## Partie 5 : coturn (TURN server pour le voice chat)

### 5.1 Pourquoi coturn est obligatoire en entreprise

En LAN, le WebRTC peut connecter les clients directement (P2P).
Quand les clients sont sur des reseaux differents (bureau, domicile, VPN), les firewalls
bloquent souvent les connexions P2P. coturn sert de **relais** pour les flux audio.

> **Sans coturn, 20-30% des utilisateurs en entreprise n'auront pas de voice chat.**

### 5.2 Installer coturn

```bash
sudo apt install -y coturn
```

### 5.3 Activer le service

```bash
sudo nano /etc/default/coturn
```

Decommenter la ligne :

```
TURNSERVER_ENABLED=1
```

### 5.4 Configurer coturn

```bash
sudo nano /etc/turnserver.conf
```

Remplacer tout le contenu par :

```ini
# === Identification ===
realm=meeting.entreprise.com
server-name=meeting.entreprise.com

# === Ports ===
listening-port=3478
tls-listening-port=5349

# === Adresses IP ===
listening-ip=0.0.0.0
external-ip=IP_PUBLIQUE_DU_SERVEUR
relay-ip=IP_PUBLIQUE_DU_SERVEUR

# Si le serveur est derriere un NAT, utiliser le format :
# external-ip=IP_PUBLIQUE/IP_PRIVEE
# Exemple : external-ip=203.0.113.50/10.0.1.5

# === Ports relay UDP ===
min-port=49152
max-port=65535
# Reduire si le firewall ne peut pas ouvrir toute la plage :
# min-port=49152
# max-port=50000

# === Certificats SSL (Let's Encrypt) ===
cert=/etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem
pkey=/etc/letsencrypt/live/meeting.entreprise.com/privkey.pem

# === Authentification ===
lt-cred-mech
user=vrmeeting:VOTRE_MOT_DE_PASSE_TURN

# === Securite ===
fingerprint
no-cli
no-tlsv1
no-tlsv1_1
no-multicast-peers

# Bloquer l'acces aux reseaux prives via le relay
denied-peer-ip=10.0.0.0-10.255.255.255
denied-peer-ip=172.16.0.0-172.31.255.255
denied-peer-ip=192.168.0.0-192.168.255.255
denied-peer-ip=127.0.0.0-127.255.255.255

# === Limites ===
total-quota=100
stale-nonce=600
max-bps=1048576

# === Logs ===
log-file=/var/log/turnserver.log
simple-log
```

> **Remplacer :**
> - `IP_PUBLIQUE_DU_SERVEUR` par l'IP publique reelle (ex: `203.0.113.50`)
> - `meeting.entreprise.com` par votre vrai domaine
> - `VOTRE_MOT_DE_PASSE_TURN` par un mot de passe fort

### 5.5 Donner acces aux certificats Let's Encrypt a coturn

Par defaut, les certificats Let's Encrypt ne sont lisibles que par root.
coturn tourne sous l'utilisateur `turnserver`.

```bash
# Ajouter turnserver au groupe qui peut lire les certs
sudo chmod 750 /etc/letsencrypt/live/
sudo chmod 750 /etc/letsencrypt/archive/
sudo chown root:turnserver /etc/letsencrypt/live/ /etc/letsencrypt/archive/

# Verifier
sudo -u turnserver cat /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem > /dev/null && echo "OK" || echo "ERREUR"
```

**Alternative** (si les permissions posent probleme) : copier les certs dans un dossier dedie et configurer un hook certbot pour les recopier au renouvellement :

```bash
sudo mkdir -p /etc/coturn/certs

# Script de copie
sudo nano /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

```bash
#!/bin/bash
cp /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem /etc/coturn/certs/cert.pem
cp /etc/letsencrypt/live/meeting.entreprise.com/privkey.pem /etc/coturn/certs/key.pem
chown turnserver:turnserver /etc/coturn/certs/*.pem
chmod 600 /etc/coturn/certs/*.pem
systemctl restart coturn
```

```bash
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
sudo /etc/letsencrypt/renewal-hooks/deploy/coturn.sh   # executer une premiere fois
```

Puis dans `/etc/turnserver.conf`, remplacer les chemins :

```ini
cert=/etc/coturn/certs/cert.pem
pkey=/etc/coturn/certs/key.pem
```

### 5.6 Demarrer coturn

```bash
sudo systemctl restart coturn
sudo systemctl enable coturn
sudo systemctl status coturn    # doit afficher "active (running)"
```

### 5.7 Verifier que coturn ecoute

```bash
ss -tlnp | grep turnserver    # ports TCP 3478, 5349
ss -ulnp | grep turnserver    # ports UDP 3478
```

---

## Partie 6 : Pare-feu du serveur (ufw)

```bash
# SSH
sudo ufw allow 22/tcp

# nginx (HTTP pour Let's Encrypt + HTTPS pour les clients)
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# coturn
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp
sudo ufw allow 49152:65535/udp

# Activer
sudo ufw enable

# Verifier
sudo ufw status verbose
```

> **Rappel :** ces regles concernent le serveur Linux lui-meme.
> Les memes ports doivent aussi etre ouverts sur le **firewall d'entreprise / routeur**
> (voir partie 1.3).

---

## Partie 7 : Configurer Unity

### 7.1 Changer l'adresse du serveur

1. Ouvrir le projet Unity
2. Ouvrir la scene `Assets/Scenes/Bootstrap.unity`
3. Selectionner le GameObject contenant `VRNetworkManager`
4. Dans l'Inspector :

```
Server Url = wss://meeting.entreprise.com
```

### 7.2 Configurer le TURN server

1. Selectionner le GameObject contenant `VoiceChatManager`
2. Dans l'Inspector :

```
Use Custom Turn Server    = true
Custom Turn Url           = turn:meeting.entreprise.com:3478
Custom Turn Username      = vrmeeting
Custom Turn Credential    = VOTRE_MOT_DE_PASSE_TURN
Enable Turn Tcp           = true
```

> Memes identifiants que dans `/etc/turnserver.conf`.

### 7.3 Builder le projet

1. **File > Build Settings**
2. Verifier l'ordre des scenes :
   - Scene 0 : `Bootstrap`
   - Scene 1 : `Meet`
3. **Platform : Windows** (ou Android pour Quest)
4. **Build**

### 7.4 Distribuer le build

Le build genere un dossier contenant :

```
Build/
  VRMeeting.exe
  VRMeeting_Data/
  UnityPlayer.dll
  MonoBleedingEdge/
```

Distribuer le dossier **complet** aux utilisateurs via :
- Partage reseau interne
- Lien de telechargement intranet
- Cle USB

> L'executable seul ne suffit pas. Tout le dossier est necessaire.
> Aucune installation de certificat n'est requise sur les postes clients
> (Let's Encrypt est reconnu nativement).

---

## Partie 8 : Verification complete

### 8.1 Depuis n'importe quel PC client

```powershell
# Le domaine repond
ping meeting.entreprise.com

# nginx HTTPS repond
Test-NetConnection -ComputerName meeting.entreprise.com -Port 443

# coturn repond
Test-NetConnection -ComputerName meeting.entreprise.com -Port 3478
```

### 8.2 Sur le serveur

```bash
# Les 3 services tournent
sudo systemctl status vr-meeting nginx coturn

# Node.js ecoute sur 8080
ss -tlnp | grep 8080

# nginx ecoute sur 80 et 443
ss -tlnp | grep nginx

# coturn ecoute sur 3478 et 5349
ss -tlnp | grep turnserver
```

### 8.3 Tester le WebSocket manuellement

Depuis un navigateur sur n'importe quel PC, ouvrir la console (F12) :

```javascript
const ws = new WebSocket('wss://meeting.entreprise.com');
ws.onopen = () => console.log('CONNECTE');
ws.onmessage = (e) => console.log('Message:', e.data);
ws.onerror = (e) => console.error('ERREUR:', e);
```

Vous devez voir `CONNECTE` puis un message `welcome` du serveur.

### 8.4 Tester coturn

Ouvrir https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/

Ajouter un serveur :
- URL : `turn:meeting.entreprise.com:3478`
- Username : `vrmeeting`
- Credential : votre mot de passe

Cliquer **Gather candidates**. Vous devez voir des candidats de type `relay`.

---

## Partie 9 : Monitoring et maintenance

### 9.1 Logs en temps reel

```bash
# Node.js (WebSocket)
journalctl -u vr-meeting -f

# nginx
sudo tail -f /var/log/nginx/access.log
sudo tail -f /var/log/nginx/error.log

# coturn
sudo tail -f /var/log/turnserver.log
```

### 9.2 Verifier l'etat des services

```bash
sudo systemctl status vr-meeting nginx coturn
```

### 9.3 Mettre a jour le serveur Node.js

Quand vous modifiez le code du serveur :

```bash
cd ~/vr-meeting/Server
git pull                                    # si via Git
# ou re-copier les fichiers via SCP

npm install                                 # si package.json a change
sudo systemctl restart vr-meeting
sudo systemctl status vr-meeting
```

### 9.4 Mettre a jour le systeme

```bash
sudo apt update && sudo apt upgrade -y

# Reboot si le kernel a ete mis a jour
sudo reboot
```

### 9.5 Rotation des logs coturn

Pour eviter que le fichier de log de coturn grossisse indefiniment :

```bash
sudo nano /etc/logrotate.d/coturn
```

```
/var/log/turnserver.log {
    daily
    rotate 7
    compress
    delaycompress
    missingok
    notifempty
    postrotate
        systemctl restart coturn
    endscript
}
```

---

## Partie 10 : Depannage

### Le certificat Let's Encrypt ne se genere pas

```bash
# Verifier que le domaine pointe vers ce serveur
dig meeting.entreprise.com +short
# Doit afficher l'IP publique de ce serveur

# Verifier que le port 80 est ouvert depuis l'exterieur
# (depuis un autre PC)
Test-NetConnection -ComputerName meeting.entreprise.com -Port 80

# Verifier les logs certbot
sudo certbot --nginx -d meeting.entreprise.com -v
```

**Causes frequentes :**
- Port 80 bloque par le firewall entreprise
- DNS pas encore propage (attendre quelques minutes/heures)
- Un autre processus ecoute sur le port 80

### Le client Unity affiche une erreur de connexion WSS

- Verifier que le certificat SSL est valide : `sudo certbot certificates`
- Verifier nginx : `sudo nginx -t && sudo systemctl status nginx`
- Verifier que le serveur Node.js tourne : `sudo systemctl status vr-meeting`
- Tester depuis un navigateur (voir partie 8.3)

### Le voice chat ne fonctionne pas entre reseaux differents

C'est le probleme le plus courant. Causes possibles :

1. **coturn ne tourne pas** : `sudo systemctl status coturn`
2. **Ports bloques** : verifier 3478 et 49152-65535 sur le firewall entreprise
3. **Mauvaise `external-ip`** dans coturn : doit etre l'IP publique
4. **Certificats illisibles** par coturn : verifier les permissions (partie 5.5)
5. **Mauvais identifiants** dans Unity : verifier qu'ils correspondent a `/etc/turnserver.conf`

**Diagnostic :**

```bash
# Verifier les logs coturn pour les connexions
sudo tail -50 /var/log/turnserver.log

# Verifier que coturn ecoute
ss -tulnp | grep turnserver

# Tester depuis l'exterieur
# Sur un PC client, PowerShell :
Test-NetConnection -ComputerName meeting.entreprise.com -Port 3478
```

### coturn : `external-ip` quand le serveur est derriere un NAT

Si le serveur a une IP privee (ex: `10.0.1.5`) mais est expose via une IP publique
(ex: `203.0.113.50`), utiliser le format avec les deux :

```ini
external-ip=203.0.113.50/10.0.1.5
```

### nginx : erreur 502 Bad Gateway

Le serveur Node.js ne tourne pas ou a crashe :

```bash
sudo systemctl status vr-meeting
journalctl -u vr-meeting --since "5 minutes ago"

# Redemarrer si besoin
sudo systemctl restart vr-meeting
```

### Les PDFs ne se convertissent pas

```bash
which pdftoppm                               # doit afficher /usr/bin/pdftoppm
journalctl -u vr-meeting | grep PDFModule
```

### Le serveur a redemarre, tout est-il revenu ?

Les 3 services sont configures avec `enable`, ils demarrent au boot :

```bash
sudo systemctl status vr-meeting nginx coturn
```

---

## Partie 11 : Securite (recommandations supplementaires)

### 11.1 Rate limiting nginx

Ajouter en haut du fichier nginx (avant le bloc `server`) :

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

```nginx
# Ajouter avant le bloc server {
limit_req_zone $binary_remote_addr zone=ws:10m rate=10r/s;
```

Et dans le bloc `location /` :

```nginx
location / {
    limit_req zone=ws burst=20 nodelay;
    # ... reste de la config identique
}
```

```bash
sudo nginx -t && sudo systemctl reload nginx
```

### 11.2 Fail2ban pour SSH

```bash
sudo apt install -y fail2ban
sudo systemctl enable fail2ban
sudo systemctl start fail2ban
```

### 11.3 Mises a jour automatiques de securite

```bash
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
```

### 11.4 Changement du mot de passe TURN

Si vous changez le mot de passe dans `/etc/turnserver.conf` :
1. Mettre a jour le `Custom Turn Credential` dans Unity (VoiceChatManager)
2. Rebuilder et redistribuer le client
3. `sudo systemctl restart coturn`

---

## Partie 12 : Checklist de validation

### Infrastructure

- [ ] Serveur Ubuntu installe et accessible en SSH
- [ ] Node.js v20 installe
- [ ] poppler-utils installe
- [ ] Dossier Server copie et `npm install` execute
- [ ] Service systemd `vr-meeting` actif
- [ ] DNS `meeting.entreprise.com` pointe vers l'IP du serveur
- [ ] nginx installe et actif
- [ ] Let's Encrypt certificat genere (`sudo certbot certificates`)
- [ ] coturn installe et actif
- [ ] coturn a acces aux certificats SSL
- [ ] ufw configure (22, 80, 443, 3478, 5349, 49152-65535)
- [ ] Firewall entreprise : ports ouverts (443, 3478, 5349, 49152-65535)

### Connectivite (depuis un PC client externe)

- [ ] `ping meeting.entreprise.com` repond
- [ ] Port 443 accessible (nginx)
- [ ] Port 3478 accessible (coturn)
- [ ] Test WebSocket navigateur : message `welcome` recu
- [ ] Test Trickle ICE : candidats `relay` trouves

### Application Unity

- [ ] `serverUrl` = `wss://meeting.entreprise.com`
- [ ] TURN custom configure avec les bons identifiants
- [ ] Build genere et distribue

### Test fonctionnel (2 clients sur des reseaux differents)

- [ ] Les 2 clients se connectent au serveur
- [ ] Creation et rejoindre une room fonctionne
- [ ] Les avatars se voient et bougent
- [ ] Le voice chat fonctionne (via TURN)
- [ ] Le whiteboard se synchronise
- [ ] Le partage d'ecran fonctionne
- [ ] Le partage de fichiers fonctionne
- [ ] Le laser pointer est visible
- [ ] Deconnexion / reconnexion fonctionnent

### Maintenance

- [ ] `sudo certbot renew --dry-run` reussit
- [ ] Les 3 services redemarrent apres un reboot serveur
- [ ] Log rotation coturn configure
- [ ] Fail2ban actif (optionnel)

---

## Resume rapide des commandes

### Installation complete (une seule fois)

```bash
# === Systeme ===
sudo apt update && sudo apt upgrade -y
curl -fsSL https://deb.nodesource.com/setup_20.x | sudo bash -
sudo apt install -y nodejs poppler-utils nginx coturn certbot python3-certbot-nginx fail2ban

# === Application ===
cd ~/vr-meeting/Server && npm install

# === Service Node.js ===
sudo systemctl enable vr-meeting
sudo systemctl start vr-meeting

# === SSL ===
sudo certbot --nginx -d meeting.entreprise.com

# === coturn ===
# Editer /etc/default/coturn -> TURNSERVER_ENABLED=1
# Editer /etc/turnserver.conf (voir partie 5.4)
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
```

### Operations courantes

```bash
# Etat des services
sudo systemctl status vr-meeting nginx coturn

# Logs temps reel
journalctl -u vr-meeting -f
sudo tail -f /var/log/turnserver.log
sudo tail -f /var/log/nginx/error.log

# Mise a jour du serveur Node.js
cd ~/vr-meeting/Server && git pull && npm install
sudo systemctl restart vr-meeting

# Renouvellement SSL (automatique, mais pour forcer)
sudo certbot renew
```

### Config Unity (Inspector)

```
VRNetworkManager :
    Server Url = wss://meeting.entreprise.com

VoiceChatManager :
    Use Custom Turn Server    = true
    Custom Turn Url           = turn:meeting.entreprise.com:3478
    Custom Turn Username      = vrmeeting
    Custom Turn Credential    = VOTRE_MOT_DE_PASSE_TURN
    Enable Turn Tcp           = true
```
