# Guide de Deploiement Public - VR Meeting Rooms

> **Version:** Ubuntu 24.04 LTS + Node.js 22 LTS
> **Derniere mise a jour:** Fevrier 2026
> **Objectif:** Rendre le serveur accessible depuis n'importe quel reseau (Internet)

---

## Architecture

```
                        INTERNET
                           |
        +------------------+------------------+
        |                  |                  |
   [Site Paris]       [Teletravail]      [Site Lyon]
   Quest + PC         PC portable        Quest + PC
        |                  |                  |
        +------------------+------------------+
                           |
                    wss://meeting.entreprise.com:443
                           |
                           v
+----------------------------------------------------------+
|                  SERVEUR CLOUD / VPS                      |
|  +-----------------------------------------------------+  |
|  |  Ubuntu 24.04 LTS                                   |  |
|  |                                                     |  |
|  |  +-------------+  +-------------+  +-------------+  |  |
|  |  |   nginx     |  |  Node.js    |  |   coturn    |  |  |
|  |  |   :443      |->|   :8080     |  |  :3478/5349 |  |  |
|  |  | Let's Encrypt| |  WebSocket  |  |  STUN/TURN  |  |  |
|  |  +-------------+  +-------------+  +-------------+  |  |
|  |                                                     |  |
|  +-----------------------------------------------------+  |
|                                                          |
|  Firewall: 22, 80, 443, 3478, 5349, 49152-65535         |
+----------------------------------------------------------+
```

---

## Prerequis

### Serveur

| Element | Specification |
|---------|---------------|
| Type | VPS, VM cloud, ou serveur dedie |
| OS | Ubuntu 24.04 LTS (recommande) |
| RAM | 4 Go minimum, 8 Go recommande |
| CPU | 2 vCPU minimum |
| Stockage | 25 Go SSD |
| Bande passante | 100 Mbps minimum |
| IP | IPv4 publique fixe |

### Fournisseurs cloud recommandes

| Fournisseur | Offre adaptee | Prix estimatif |
|-------------|---------------|----------------|
| OVH | VPS Starter | ~6€/mois |
| Scaleway | DEV1-M | ~8€/mois |
| Hetzner | CX22 | ~4€/mois |
| DigitalOcean | Basic Droplet | ~12$/mois |
| AWS | t3.small | ~15$/mois |

### Domaine et DNS

- Un nom de domaine (ex: `entreprise.com`)
- Acces au DNS pour creer des enregistrements A
- Sous-domaine dedie (ex: `meeting.entreprise.com`)

---

## Partie 1 : Preparation du serveur

### 1.1 Connexion initiale

```bash
# Depuis votre PC local
ssh root@IP_DU_SERVEUR
```

### 1.2 Mise a jour du systeme

```bash
apt update && apt upgrade -y
```

### 1.3 Creer un utilisateur non-root

```bash
# Creer l'utilisateur
adduser vr-admin
usermod -aG sudo vr-admin

# Copier la cle SSH (si vous utilisez une cle)
mkdir -p /home/vr-admin/.ssh
cp ~/.ssh/authorized_keys /home/vr-admin/.ssh/
chown -R vr-admin:vr-admin /home/vr-admin/.ssh
chmod 700 /home/vr-admin/.ssh
chmod 600 /home/vr-admin/.ssh/authorized_keys

# Se reconnecter en tant que vr-admin
exit
```

```bash
ssh vr-admin@IP_DU_SERVEUR
```

### 1.4 Configurer le hostname

```bash
sudo hostnamectl set-hostname vr-meeting-server
```

---

## Partie 2 : Configuration DNS

Avant d'installer quoi que ce soit, configurez le DNS car Let's Encrypt en a besoin.

### 2.1 Creer les enregistrements DNS

Dans le panneau de controle de votre registrar/DNS, ajouter :

| Type | Nom | Valeur | TTL |
|------|-----|--------|-----|
| A | meeting | IP_DU_SERVEUR | 300 |
| A | turn | IP_DU_SERVEUR | 300 |

Exemple pour `entreprise.com` :
- `meeting.entreprise.com` → `203.0.113.50`
- `turn.entreprise.com` → `203.0.113.50`

### 2.2 Verifier la propagation DNS

```bash
# Attendre quelques minutes puis verifier
dig meeting.entreprise.com +short
dig turn.entreprise.com +short
# Doit afficher l'IP du serveur
```

Ou utiliser : https://dnschecker.org

---

## Partie 3 : Installation des composants

### 3.1 Installer Node.js 22 LTS

```bash
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs
node --version    # v22.x.x
npm --version
```

### 3.2 Installer les outils necessaires

```bash
sudo apt install -y git nginx certbot python3-certbot-nginx poppler-utils ufw fail2ban
```

### 3.3 Copier le projet serveur

**Option A : Via Git**

```bash
cd ~
git clone https://votre-repo.git vr-meeting
cd vr-meeting/Server
npm install
```

**Option B : Via SCP depuis votre PC local**

```bash
# Depuis votre PC Windows (PowerShell)
scp -r "D:\Test_project\WebSocket_VR\Server" vr-admin@IP_DU_SERVEUR:~/vr-meeting/
```

Puis sur le serveur :

```bash
cd ~/vr-meeting/Server
npm install
```

### 3.4 Tester le lancement

```bash
cd ~/vr-meeting/Server
npm start
```

Verifier que le serveur demarre correctement, puis `Ctrl+C` pour arreter.

---

## Partie 4 : Configurer le pare-feu (UFW)

### 4.1 Configurer les regles

```bash
# SSH (IMPORTANT : ne pas s'enfermer dehors !)
sudo ufw allow 22/tcp

# HTTP (pour Let's Encrypt)
sudo ufw allow 80/tcp

# HTTPS (nginx + WebSocket)
sudo ufw allow 443/tcp

# STUN/TURN
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp

# Ports relay TURN (WebRTC media)
sudo ufw allow 49152:65535/udp

# Activer le pare-feu
sudo ufw enable

# Verifier
sudo ufw status verbose
```

### 4.2 Resultat attendu

```
Status: active

To                         Action      From
--                         ------      ----
22/tcp                     ALLOW       Anywhere
80/tcp                     ALLOW       Anywhere
443/tcp                    ALLOW       Anywhere
3478/tcp                   ALLOW       Anywhere
3478/udp                   ALLOW       Anywhere
5349/tcp                   ALLOW       Anywhere
49152:65535/udp            ALLOW       Anywhere
```

---

## Partie 5 : Configurer nginx avec Let's Encrypt

### 5.1 Creer la configuration nginx initiale (HTTP)

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

Coller (remplacer `meeting.entreprise.com` par votre domaine) :

```nginx
server {
      listen 80;
      server_name vrmeeting-test.duckdns.org;

      location / {
          return 200 'VR Meeting Server - HTTP OK';
          add_header Content-Type text/plain;
      }
}
```

Activer le site :

```bash
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl restart nginx
```

### 5.2 Obtenir le certificat Let's Encrypt

```bash
sudo certbot --nginx -d meeting.entreprise.com
```

Repondre aux questions :
- Email : votre email (pour les notifications d'expiration)
- Conditions : Yes
- Partager email : No (ou Yes selon preference)
- Redirection HTTP→HTTPS : 2 (Redirect)

Certbot modifie automatiquement la config nginx.

### 5.3 Mettre a jour la configuration nginx pour WebSocket

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```
Certificate is saved at: /etc/letsencrypt/live/vrmeeting-test.duckdns.org/fullchain.pem
Key is saved at:         /etc/letsencrypt/live/vrmeeting-test.duckdns.org/privkey.pem
Remplacer tout le contenu par :

```nginx
# Redirection HTTP → HTTPS
server {
    listen 80;
    server_name meeting.entreprise.com;
    return 301 https://$host$request_uri;
}

# Serveur HTTPS principal
server {
    listen 443 ssl http2;
    server_name meeting.entreprise.com;

    # Certificats Let's Encrypt (generes par certbot)
    ssl_certificate /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/meeting.entreprise.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    # Securite supplementaire
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;

    # WebSocket proxy vers Node.js
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

        # Buffers
        proxy_buffering off;
        proxy_buffer_size 4k;
    }

    # Health check endpoint (optionnel)
    location /health {
        return 200 'OK';
        add_header Content-Type text/plain;
    }
}
```

Appliquer :

```bash
sudo nginx -t
sudo systemctl restart nginx
```

### 5.4 Verifier le renouvellement automatique

```bash
# Tester le renouvellement (dry-run)
sudo certbot renew --dry-run
```

Le renouvellement automatique est configure via un timer systemd ou cron.

---

## Partie 6 : Configurer coturn (TURN/STUN)

### 6.1 Installer coturn

```bash
sudo apt install -y coturn
```

### 6.2 Activer coturn comme service

```bash
sudo nano /etc/default/coturn
```

Decommenter (enlever le #) :

```
TURNSERVER_ENABLED=1
```

### 6.3 Generer un certificat pour coturn

coturn a besoin de son propre certificat pour TURNS (TLS).

```bash
# Copier les certificats Let's Encrypt pour coturn
sudo mkdir -p /etc/coturn/certs

# Script pour copier les certificats (necessaire car Let's Encrypt les renouvelle)
sudo nano /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

Coller :

```bash
#!/bin/bash
DOMAIN="meeting.entreprise.com"
cp /etc/letsencrypt/live/$DOMAIN/fullchain.pem /etc/coturn/certs/
cp /etc/letsencrypt/live/$DOMAIN/privkey.pem /etc/coturn/certs/
chown turnserver:turnserver /etc/coturn/certs/*.pem
chmod 600 /etc/coturn/certs/*.pem
systemctl restart coturn
```

Rendre executable et lancer une premiere fois :

```bash
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
sudo /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

### 6.4 Configurer coturn

```bash
sudo nano /etc/turnserver.conf
```

Remplacer tout le contenu par (adapter les valeurs) :

```ini
# ===========================================
# Configuration coturn pour VR Meeting
# ===========================================

# Nom du serveur
realm=meeting.entreprise.com
server-name=meeting.entreprise.com

# Ports d'ecoute
listening-port=3478
tls-listening-port=5349

# IP d'ecoute
listening-ip=0.0.0.0
relay-ip=IP_PUBLIQUE_DU_SERVEUR
external-ip=IP_PUBLIQUE_DU_SERVEUR

# Plage de ports UDP pour le relay media
min-port=49152
max-port=65535

# Certificats SSL
cert=/etc/coturn/certs/fullchain.pem
pkey=/etc/coturn/certs/privkey.pem

# Authentification
lt-cred-mech
user=vrmeeting:MotDePasseTURN_Securise_2024!

# Securite
fingerprint
no-cli
no-tlsv1
no-tlsv1_1

# Limites (anti-abus)
total-quota=100
stale-nonce=600
max-bps=1000000

# Logs
log-file=/var/log/turnserver/turnserver.log
simple-log
no-stdout-log

# Divers
proc-user=turnserver
proc-group=turnserver
```

**IMPORTANT : Remplacer :**
- `meeting.entreprise.com` par votre domaine
- `IP_PUBLIQUE_DU_SERVEUR` par l'IP publique reelle
- `MotDePasseTURN_Securise_2024!` par un mot de passe fort

### 6.5 Creer le dossier de logs

```bash
sudo mkdir -p /var/log/turnserver
sudo chown turnserver:turnserver /var/log/turnserver
```

### 6.6 Demarrer coturn

```bash
sudo systemctl restart coturn
sudo systemctl enable coturn
sudo systemctl status coturn
```

### 6.7 Verifier que coturn ecoute

```bash
ss -tlnp | grep turnserver
ss -ulnp | grep turnserver
```

Attendu :
```
tcp   LISTEN  0  128  0.0.0.0:3478   turnserver
tcp   LISTEN  0  128  0.0.0.0:5349   turnserver
udp   UNCONN  0  0    0.0.0.0:3478   turnserver
```

---

## Partie 7 : Gestion du processus avec PM2

### 7.1 Pourquoi PM2 ?

PM2 est un gestionnaire de processus pour Node.js en production.

| Fonctionnalite | Description |
|----------------|-------------|
| **Auto-restart** | Redemarre automatiquement en cas de crash |
| **Cluster mode** | Utilise tous les coeurs CPU |
| **Zero-downtime reload** | Mise a jour sans deconnexion des utilisateurs |
| **Monitoring integre** | Dashboard temps reel CPU, RAM, logs |
| **Gestion des logs** | Logs centralises avec rotation |
| **Demarrage auto** | Demarre automatiquement au boot du serveur |

### 7.2 Installer PM2

```bash
# Installer PM2 globalement
sudo npm install -g pm2

# Verifier l'installation
pm2 --version
```

### 7.3 Creer le fichier de configuration PM2

```bash
cd ~/vr-meeting/Server
nano ecosystem.config.js
```

Coller :

```javascript
module.exports = {
  apps: [{
    // Nom de l'application (affiche dans pm2 list)
    name: 'vr-meeting',

    // Point d'entree
    script: 'server.js',

    // Repertoire de travail
    cwd: '/home/vr-admin/vr-meeting/Server',

    // Nombre d'instances (1 = simple, 'max' = tous les CPU)
    instances: 1,

    // Redemarrer si memoire depasse 500MB
    max_memory_restart: '500M',

    // Variables d'environnement
    env: {
      NODE_ENV: 'production',
      PORT: 8080
    },

    // Auto-restart en cas de crash
    autorestart: true,

    // Surveiller les changements de fichiers (desactiver en prod)
    watch: false,

    // Delai avant restart apres crash (ms)
    restart_delay: 5000,

    // Maximum de restarts avant arret
    max_restarts: 10,

    // Configuration des logs
    log_file: '/home/vr-admin/vr-meeting/logs/combined.log',
    error_file: '/home/vr-admin/vr-meeting/logs/error.log',
    out_file: '/home/vr-admin/vr-meeting/logs/out.log',
    log_date_format: 'YYYY-MM-DD HH:mm:ss Z',

    // Fusionner les logs de toutes les instances
    merge_logs: true
  }]
};
```

### 7.4 Creer les dossiers necessaires

```bash
mkdir -p ~/vr-meeting/logs
mkdir -p ~/vr-meeting/Server/uploads
mkdir -p ~/vr-meeting/Server/temp
```

### 7.5 Demarrer l'application avec PM2

```bash
cd ~/vr-meeting/Server

# Demarrer avec le fichier de config
pm2 start ecosystem.config.js

# Verifier le statut
pm2 status
```

Resultat attendu :

```
┌─────┬──────────────┬─────────┬─────────┬──────────┬────────┬──────────┐
│ id  │ name         │ mode    │ pid     │ uptime   │ status │ cpu │ mem│
├─────┼──────────────┼─────────┼─────────┼──────────┼────────┼──────────┤
│ 0   │ vr-meeting   │ fork    │ 12345   │ 0s       │ online │ 0%  │45MB│
└─────┴──────────────┴─────────┴─────────┴──────────┴────────┴──────────┘
```

### 7.6 Configurer le demarrage automatique au boot

**Etape critique** - sans cela, le serveur ne redemarrera pas apres un reboot.

```bash
# Generer le script de demarrage
pm2 startup
```

PM2 affichera une commande comme :

```
[PM2] To setup the Startup Script, copy/paste the following command:
sudo env PATH=$PATH:/usr/bin pm2 startup systemd -u vr-admin --hp /home/vr-admin
```

**Copiez et executez cette commande exacte** (elle sera differente sur votre systeme).

Puis sauvegardez la liste des processus :

```bash
pm2 save
```

### 7.7 Verifier que le demarrage auto fonctionne

```bash
# Redemarrer le serveur
sudo reboot

# Apres le reboot, verifier
pm2 status
```

Le processus vr-meeting doit etre en cours d'execution.

### 7.8 Commandes PM2 utiles

```bash
# === Statut & Monitoring ===
pm2 status                    # Liste tous les processus
pm2 monit                     # Dashboard monitoring temps reel
pm2 info vr-meeting           # Infos detaillees sur l'app

# === Logs ===
pm2 logs                      # Voir tous les logs (live)
pm2 logs vr-meeting           # Logs d'une app specifique
pm2 logs --lines 100          # Dernières 100 lignes
pm2 flush                     # Effacer tous les logs

# === Controle ===
pm2 stop vr-meeting           # Arreter
pm2 start vr-meeting          # Demarrer
pm2 restart vr-meeting        # Redemarrer (breve interruption)
pm2 reload vr-meeting         # Reload sans interruption (graceful)

# === Mises a jour ===
pm2 reload ecosystem.config.js    # Recharger avec config modifiee

# === Nettoyage ===
pm2 delete vr-meeting         # Supprimer de la liste PM2
pm2 kill                      # Arreter completement PM2
```

### 7.9 Rotation des logs PM2

Installer la rotation automatique des logs :

```bash
pm2 install pm2-logrotate

# Configurer la rotation
pm2 set pm2-logrotate:max_size 10M      # Rotation quand fichier atteint 10MB
pm2 set pm2-logrotate:retain 7          # Garder 7 fichiers
pm2 set pm2-logrotate:compress true     # Compresser les anciens logs
```

---

## Partie 8 : Securite supplementaire

### 8.1 Configurer fail2ban

fail2ban protege contre les attaques brute-force SSH.

```bash
sudo nano /etc/fail2ban/jail.local
```

Coller :

```ini
[DEFAULT]
bantime = 1h
findtime = 10m
maxretry = 5

[sshd]
enabled = true
port = ssh
filter = sshd
logpath = /var/log/auth.log
maxretry = 3
```

Redemarrer :

```bash
sudo systemctl restart fail2ban
sudo systemctl enable fail2ban
```

### 8.2 Desactiver l'authentification root par mot de passe

```bash
sudo nano /etc/ssh/sshd_config
```

Verifier/modifier :

```
PermitRootLogin prohibit-password
PasswordAuthentication no
```

```bash
sudo systemctl restart sshd
```

### 8.3 Mises a jour automatiques de securite

```bash
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
# Choisir "Yes"
```

---

## Partie 9 : Configurer Unity

### 9.1 VRNetworkManager

Dans la scene `Bootstrap.unity`, selectionner le GameObject avec `VRNetworkManager` :

| Champ | Valeur |
|-------|--------|
| Server Url | `wss://meeting.entreprise.com` |

### 9.2 VoiceChatManager

| Champ | Valeur |
|-------|--------|
| Use Custom Turn Server | `true` |
| Custom Turn Url | `turn:meeting.entreprise.com:3478` |
| Custom Turns Url | `turns:meeting.entreprise.com:5349` |
| Custom Turn Username | `vrmeeting` |
| Custom Turn Credential | `MotDePasseTURN_Securise_2024!` |
| Enable Turn Tcp | `true` |

> Utilisez les memes credentials que dans `/etc/turnserver.conf`

### 9.3 Build et distribution

1. **File > Build Settings**
2. Platform : **Windows** ou **Android** (Quest)
3. **Build**
4. Distribuer le build aux utilisateurs

**Pour Quest :**
- Uploader l'APK sur Meta Quest Developer Hub
- Ou utiliser un MDM (Mobile Device Management) pour deploiement entreprise

---

## Partie 10 : Verification et tests

### 10.1 Verifier tous les services

```bash
# PM2 (Node.js)
pm2 status

# nginx et coturn
sudo systemctl status nginx coturn

# Ports ouverts
sudo ss -tlnp | grep -E '(nginx|node|turn)'
```

### 10.2 Tester depuis l'exterieur

Depuis un PC client (pas sur le meme reseau) :

```powershell
# DNS
nslookup meeting.entreprise.com

# HTTPS
Test-NetConnection -ComputerName meeting.entreprise.com -Port 443

# TURN
Test-NetConnection -ComputerName meeting.entreprise.com -Port 3478
```

### 10.3 Tester le certificat SSL

```bash
# Depuis le serveur
curl -I https://meeting.entreprise.com
```

Ou visiter https://www.ssllabs.com/ssltest/ et entrer votre domaine.

### 10.4 Tester TURN avec Trickle ICE

1. Aller sur https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
2. Ajouter un serveur TURN :
   - URL : `turn:meeting.entreprise.com:3478`
   - Username : `vrmeeting`
   - Credential : `MotDePasseTURN_Securise_2024!`
3. Cliquer "Gather candidates"
4. Verifier que des candidats `relay` apparaissent

### 10.5 Logs en temps reel

Ouvrir 3 terminaux SSH :

```bash
# Terminal 1 : Node.js (PM2)
pm2 logs vr-meeting

# Terminal 2 : nginx
sudo tail -f /var/log/nginx/access.log /var/log/nginx/error.log

# Terminal 3 : coturn
sudo tail -f /var/log/turnserver/turnserver.log
```

Ou utiliser le dashboard PM2 integre :

```bash
pm2 monit
```

---

## Partie 11 : Monitoring et maintenance

### 11.1 Script de verification quotidienne

```bash
sudo nano /usr/local/bin/vr-meeting-check.sh
```

Coller :

```bash
#!/bin/bash

echo "=== VR Meeting Server Status ==="
echo "Date: $(date)"
echo ""

echo "--- Services ---"
systemctl is-active --quiet nginx && echo "nginx: OK" || echo "nginx: FAILED"
systemctl is-active --quiet coturn && echo "coturn: OK" || echo "coturn: FAILED"

# Verifier PM2
PM2_STATUS=$(su - vr-admin -c "pm2 jlist" 2>/dev/null | grep -o '"status":"online"' | wc -l)
if [ "$PM2_STATUS" -gt 0 ]; then
    echo "vr-meeting (PM2): OK"
else
    echo "vr-meeting (PM2): FAILED"
fi
echo ""

echo "--- Certificat SSL ---"
CERT_EXPIRY=$(sudo openssl x509 -enddate -noout -in /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem | cut -d= -f2)
echo "Expiration: $CERT_EXPIRY"
echo ""

echo "--- Espace disque ---"
df -h / | tail -1
echo ""

echo "--- Memoire ---"
free -h | grep Mem
echo ""

echo "--- Connexions actives ---"
ss -tn state established | grep -c ":443" | xargs echo "Port 443:"
ss -tn state established | grep -c ":8080" | xargs echo "Port 8080:"
```

```bash
sudo chmod +x /usr/local/bin/vr-meeting-check.sh
```

### 11.2 Rotation des logs

```bash
sudo nano /etc/logrotate.d/vr-meeting
```

Coller :

```
/var/log/turnserver/*.log {
    daily
    rotate 7
    compress
    delaycompress
    missingok
    notifempty
    postrotate
        systemctl reload coturn > /dev/null 2>&1 || true
    endscript
}
```

### 11.3 Backup de la configuration

```bash
# Creer un script de backup
sudo nano /usr/local/bin/vr-meeting-backup.sh
```

```bash
#!/bin/bash
BACKUP_DIR="/home/vr-admin/backups"
DATE=$(date +%Y%m%d)

mkdir -p $BACKUP_DIR

# Configurations
tar -czf $BACKUP_DIR/config-$DATE.tar.gz \
    /etc/nginx/sites-available/vr-meeting \
    /etc/turnserver.conf \
    /home/vr-admin/vr-meeting/Server/ecosystem.config.js \
    /home/vr-admin/vr-meeting/Server/

# Garder 7 jours de backups
find $BACKUP_DIR -name "*.tar.gz" -mtime +7 -delete

echo "Backup completed: $BACKUP_DIR/config-$DATE.tar.gz"
```

```bash
sudo chmod +x /usr/local/bin/vr-meeting-backup.sh

# Ajouter au cron (backup quotidien a 2h du matin)
(crontab -l 2>/dev/null; echo "0 2 * * * /usr/local/bin/vr-meeting-backup.sh") | crontab -
```

---

## Partie 12 : Commandes de reference rapide

### Gestion de PM2 (Node.js)

```bash
# === Statut ===
pm2 status                    # Liste des processus
pm2 info vr-meeting           # Infos detaillees

# === Controle ===
pm2 stop vr-meeting           # Arreter
pm2 start vr-meeting          # Demarrer
pm2 restart vr-meeting        # Redemarrer
pm2 reload vr-meeting         # Reload sans interruption

# === Logs ===
pm2 logs                      # Tous les logs (live)
pm2 logs vr-meeting           # Logs specifiques
pm2 logs --lines 100          # Dernieres 100 lignes
pm2 flush                     # Effacer les logs

# === Monitoring ===
pm2 monit                     # Dashboard temps reel

# === Apres mise a jour du code ===
cd ~/vr-meeting/Server
git pull
npm install
pm2 reload vr-meeting
```

### Gestion de nginx et coturn

```bash
# Demarrer/Arreter/Redemarrer
sudo systemctl start nginx coturn
sudo systemctl stop nginx coturn
sudo systemctl restart nginx coturn

# Status
sudo systemctl status nginx coturn
```

### Logs

```bash
# Node.js (PM2)
pm2 logs vr-meeting

# nginx
sudo tail -f /var/log/nginx/error.log

# coturn
sudo tail -f /var/log/turnserver/turnserver.log
```

### Certificats SSL

```bash
# Verifier expiration
sudo certbot certificates

# Renouveler manuellement
sudo certbot renew

# Tester renouvellement
sudo certbot renew --dry-run
```

### Debug reseau

```bash
# Ports ouverts
sudo ss -tlnp

# Connexions etablies
sudo ss -tn state established

# Trafic en temps reel
sudo tcpdump -i any port 443 or port 3478
```

---

## Partie 13 : Checklist de deploiement

### Infrastructure

- [ ] VPS/serveur provisionne avec Ubuntu 24.04 LTS
- [ ] IP publique fixe obtenue
- [ ] Acces SSH configure (cle SSH recommandee)
- [ ] Utilisateur non-root cree (vr-admin)

### DNS

- [ ] Enregistrement A pour `meeting.entreprise.com`
- [ ] Enregistrement A pour `turn.entreprise.com` (optionnel, peut etre le meme)
- [ ] Propagation DNS verifiee

### Logiciels

- [ ] Node.js v22 LTS installe
- [ ] nginx installe
- [ ] certbot installe
- [ ] coturn installe
- [ ] poppler-utils installe
- [ ] Code serveur copie et npm install execute

### Certificats SSL

- [ ] Certificat Let's Encrypt obtenu pour le domaine
- [ ] Certificat copie pour coturn
- [ ] Hook de renouvellement configure

### Services

- [ ] Service vr-meeting cree et actif
- [ ] nginx configure et actif
- [ ] coturn configure et actif
- [ ] Tous les services en demarrage automatique

### Securite

- [ ] UFW configure et actif
- [ ] fail2ban configure
- [ ] Authentification SSH par cle uniquement
- [ ] Mises a jour automatiques activees

### Tests

- [ ] HTTPS accessible depuis l'exterieur
- [ ] WebSocket fonctionne (wss://)
- [ ] TURN fonctionne (test Trickle ICE)
- [ ] Client Unity se connecte
- [ ] Voice chat fonctionne entre 2 clients sur reseaux differents
- [ ] Whiteboard synchronise
- [ ] Partage d'ecran fonctionne

---

## Partie 14 : Depannage

### Le client Unity ne se connecte pas

```bash
# Verifier que le serveur ecoute
sudo ss -tlnp | grep 8080

# Verifier PM2
pm2 status
pm2 logs --lines 20

# Verifier nginx
sudo nginx -t
sudo systemctl status nginx

# Verifier les logs nginx
sudo tail -20 /var/log/nginx/error.log
```

### Erreur SSL / certificat

```bash
# Verifier le certificat
sudo certbot certificates

# Renouveler si expire
sudo certbot renew

# Verifier la config nginx
sudo nginx -t
```

### Voice chat ne fonctionne pas

```bash
# Verifier coturn
sudo systemctl status coturn
sudo tail -50 /var/log/turnserver/turnserver.log

# Tester les ports
nc -zv IP_DU_SERVEUR 3478
nc -zv IP_DU_SERVEUR 5349

# Verifier le firewall
sudo ufw status
```

### Latence elevee / deconnexions

```bash
# Verifier la charge CPU
top

# Verifier la memoire
free -h

# Verifier les connexions
ss -tn state established | wc -l

# Verifier les logs PM2 pour des erreurs
pm2 logs --lines 100 | grep -i error

# Verifier le nombre de restarts
pm2 info vr-meeting | grep restart
```

### Certificat Let's Encrypt ne se renouvelle pas

```bash
# Verifier le timer
sudo systemctl status certbot.timer

# Tester manuellement
sudo certbot renew --dry-run

# Verifier les logs
sudo journalctl -u certbot
```

---

## Partie 15 : Mise a jour du serveur

### Mettre a jour le code Node.js

```bash
# Mettre a jour le code
cd ~/vr-meeting
git pull origin main

# Reinstaller les dependances si necessaire
cd Server
npm install

# Reload sans interruption (zero-downtime)
pm2 reload vr-meeting

# Verifier
pm2 status
pm2 logs --lines 10
```

**Note :** Avec PM2, pas besoin d'arreter le service. `pm2 reload` fait un reload graceful sans deconnecter les utilisateurs.

### Mettre a jour le systeme

```bash
sudo apt update
sudo apt upgrade -y

# Redemarrer si necessaire (kernel update)
sudo reboot
```

---

## Annexe A : Configuration complete des fichiers

### /etc/nginx/sites-available/vr-meeting

```nginx
server {
    listen 80;
    server_name meeting.entreprise.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name meeting.entreprise.com;

    ssl_certificate /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/meeting.entreprise.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;

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
        proxy_buffering off;
    }
}
```

### /etc/turnserver.conf

```ini
realm=meeting.entreprise.com
server-name=meeting.entreprise.com
listening-port=3478
tls-listening-port=5349
listening-ip=0.0.0.0
relay-ip=IP_PUBLIQUE
external-ip=IP_PUBLIQUE
min-port=49152
max-port=65535
cert=/etc/coturn/certs/fullchain.pem
pkey=/etc/coturn/certs/privkey.pem
lt-cred-mech
user=vrmeeting:MotDePasseTURN_Securise_2024!
fingerprint
no-cli
no-tlsv1
no-tlsv1_1
total-quota=100
stale-nonce=600
log-file=/var/log/turnserver/turnserver.log
simple-log
```

### /home/vr-admin/vr-meeting/Server/ecosystem.config.js

```javascript
module.exports = {
  apps: [{
    name: 'vr-meeting',
    script: 'server.js',
    cwd: '/home/vr-admin/vr-meeting/Server',
    instances: 1,
    max_memory_restart: '500M',
    env: {
      NODE_ENV: 'production',
      PORT: 8080
    },
    autorestart: true,
    watch: false,
    restart_delay: 5000,
    max_restarts: 10,
    log_file: '/home/vr-admin/vr-meeting/logs/combined.log',
    error_file: '/home/vr-admin/vr-meeting/logs/error.log',
    out_file: '/home/vr-admin/vr-meeting/logs/out.log',
    log_date_format: 'YYYY-MM-DD HH:mm:ss Z',
    merge_logs: true
  }]
};
```

---

## Annexe B : Estimation des couts mensuels

| Element | Cout estimatif |
|---------|----------------|
| VPS (4Go RAM, 2 vCPU) | 5-15€/mois |
| Domaine (.com) | ~12€/an (~1€/mois) |
| Certificat SSL (Let's Encrypt) | Gratuit |
| Bande passante incluse | Generalement 1-5 To |
| **Total** | **~6-16€/mois** |

---

*Document genere le: Fevrier 2026*
*Version: 1.0*
*Compatibilite: Ubuntu 24.04 LTS, Node.js 22 LTS*
