# Guide de Deploiement - Serveur d'Entreprise

Ce document explique comment deployer l'application VR Meeting sur un serveur d'entreprise.

---

## Table des Matieres

1. [Pre-requis - Ce qu'il faut avoir](#1-pre-requis---ce-quil-faut-avoir)
2. [Pre-requis - Ce qu'il faut savoir](#2-pre-requis---ce-quil-faut-savoir)
3. [Architecture du systeme](#3-architecture-du-systeme)
4. [Etape 1 : Preparer le serveur](#4-etape-1--preparer-le-serveur)
5. [Etape 2 : Installer Node.js](#5-etape-2--installer-nodejs)
6. [Etape 3 : Installer MariaDB](#6-etape-3--installer-mariadb)
7. [Etape 4 : Deployer le code serveur](#7-etape-4--deployer-le-code-serveur)
8. [Etape 5 : Configurer le pare-feu](#8-etape-5--configurer-le-pare-feu)
9. [Etape 6 : Configurer HTTPS (SSL)](#9-etape-6--configurer-https-ssl)
10. [Etape 7 : Modifier le client Unity](#10-etape-7--modifier-le-client-unity)
11. [Etape 8 : Tester la connexion](#11-etape-8--tester-la-connexion)
12. [Maintenance et surveillance](#12-maintenance-et-surveillance)
13. [Depannage](#13-depannage)
14. [Checklist de deploiement](#14-checklist-de-deploiement)

---

## 1. Pre-requis - Ce qu'il faut avoir

### Materiel / Infrastructure

| Element | Minimum | Recommande | Notes |
|---------|---------|------------|-------|
| **Serveur** | VPS 2 CPU, 4 Go RAM | VPS 4 CPU, 8 Go RAM | Linux Ubuntu 22.04 LTS recommande |
| **Stockage** | 20 Go SSD | 50 Go SSD | Pour les fichiers partages |
| **Bande passante** | 100 Mbps | 1 Gbps | Critique pour le temps reel |
| **IP publique** | 1 IP fixe | 1 IP fixe | Obligatoire |
| **Nom de domaine** | Optionnel | Recommande | Pour HTTPS (ex: vr.entreprise.com) |

### Acces necessaires

| Acces | Pourquoi |
|-------|----------|
| **SSH root** ou sudo | Pour installer les logiciels |
| **Acces au pare-feu** | Pour ouvrir les ports |
| **Acces DNS** (si domaine) | Pour pointer le domaine vers le serveur |
| **Certificat SSL** (si HTTPS) | Let's Encrypt gratuit ou certificat entreprise |

### Ports a ouvrir

| Port | Protocole | Usage |
|------|-----------|-------|
| **22** | TCP | SSH (acces au serveur) |
| **80** | TCP | HTTP (redirection vers HTTPS) |
| **443** | TCP | HTTPS / WSS (WebSocket securise) |
| **8080** | TCP | WebSocket direct (si pas de reverse proxy) |
| **3306** | TCP | MariaDB (seulement en interne !) |

---

## 2. Pre-requis - Ce qu'il faut savoir

### Competences requises

| Competence | Niveau | Pourquoi |
|------------|--------|----------|
| **Linux (ligne de commande)** | Intermediaire | Navigation, edition de fichiers, permissions |
| **SSH** | Basique | Se connecter au serveur |
| **Reseau** | Basique | Comprendre IP, ports, DNS, pare-feu |
| **Base de donnees** | Basique | Creer une base, un utilisateur |
| **Node.js** | Debutant | Lancer `npm install` et `node server.js` |

### Commandes Linux essentielles a connaitre

```bash
# Navigation
cd /chemin/vers/dossier    # Aller dans un dossier
ls -la                      # Lister les fichiers
pwd                         # Afficher le chemin actuel

# Edition de fichiers
nano fichier.txt            # Editeur simple
cat fichier.txt             # Afficher le contenu

# Permissions
chmod +x script.sh          # Rendre executable
chown user:group fichier    # Changer le proprietaire

# Services
systemctl start service     # Demarrer un service
systemctl status service    # Voir l'etat
systemctl enable service    # Demarrer au boot

# Reseau
curl http://localhost:8080  # Tester une URL
netstat -tlnp               # Voir les ports ouverts
```

---

## 3. Architecture du systeme

```
                                    SERVEUR D'ENTREPRISE
                                    ┌─────────────────────────────────────────┐
                                    │                                         │
INTERNET                            │   ┌─────────────┐                       │
    │                               │   │   nginx     │                       │
    │                               │   │ (reverse    │                       │
    │   HTTPS (port 443)            │   │  proxy)     │                       │
    │ ══════════════════════════════│══>│             │                       │
    │                               │   │ :443 → :8080│                       │
    │                               │   └──────┬──────┘                       │
    │                               │          │                              │
    │                               │          ▼                              │
    │                               │   ┌─────────────┐    ┌─────────────┐   │
    │                               │   │  Node.js    │    │  MariaDB    │   │
    │                               │   │  server.js  │◄──►│  (port 3306)│   │
    │                               │   │ (port 8080) │    │             │   │
    │                               │   └─────────────┘    └─────────────┘   │
    │                               │                                         │
    │                               └─────────────────────────────────────────┘
    │
    │
┌───┴───┐     ┌───────┐     ┌───────┐
│Client │     │Client │     │Client │
│Unity 1│     │Unity 2│     │Unity 3│
└───────┘     └───────┘     └───────┘
```

### Flux de communication

```
1. Client Unity se connecte a wss://vr.entreprise.com
2. nginx recoit la connexion HTTPS sur le port 443
3. nginx transmet a Node.js sur le port 8080 (en interne)
4. Node.js gere la logique (salles, messages, etc.)
5. Node.js communique avec MariaDB pour l'authentification
```

---

## 4. Etape 1 : Preparer le serveur

### 4.1 Se connecter au serveur

```bash
# Depuis votre ordinateur
ssh utilisateur@IP_DU_SERVEUR

# Exemple
ssh admin@192.168.1.100
# ou
ssh admin@vr.entreprise.com
```

### 4.2 Mettre a jour le systeme

```bash
# Mettre a jour la liste des paquets
sudo apt update

# Mettre a jour les paquets installes
sudo apt upgrade -y

# Installer les outils de base
sudo apt install -y curl wget git nano ufw
```

### 4.3 Creer un utilisateur dedie (securite)

```bash
# Creer un utilisateur pour l'application
sudo adduser vrmeeting

# Lui donner les droits sudo (optionnel)
sudo usermod -aG sudo vrmeeting

# Se connecter avec cet utilisateur
su - vrmeeting
```

---

## 5. Etape 2 : Installer Node.js

### 5.1 Installer Node.js 18 LTS

```bash
# Ajouter le depot NodeSource
curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -

# Installer Node.js
sudo apt install -y nodejs

# Verifier l'installation
node --version   # Doit afficher v18.x.x
npm --version    # Doit afficher 9.x.x ou plus
```

### 5.2 Installer PM2 (gestionnaire de processus)

PM2 permet de garder le serveur en marche meme apres une deconnexion ou un redemarrage.

```bash
# Installer PM2 globalement
sudo npm install -g pm2

# Verifier
pm2 --version
```

---

## 6. Etape 3 : Installer MariaDB

### 6.1 Installer MariaDB

```bash
# Installer MariaDB
sudo apt install -y mariadb-server mariadb-client

# Demarrer le service
sudo systemctl start mariadb
sudo systemctl enable mariadb  # Demarrer au boot

# Verifier
sudo systemctl status mariadb
```

### 6.2 Securiser MariaDB

```bash
# Lancer l'assistant de securisation
sudo mysql_secure_installation

# Repondre aux questions :
# - Enter current password for root: [Entree] (vide par defaut)
# - Switch to unix_socket authentication? [n]
# - Change the root password? [Y] -> entrer un mot de passe FORT
# - Remove anonymous users? [Y]
# - Disallow root login remotely? [Y]
# - Remove test database? [Y]
# - Reload privilege tables? [Y]
```

**IMPORTANT : Notez le mot de passe root de MariaDB !**

### 6.3 Creer la base de donnees et l'utilisateur

```bash
# Se connecter a MariaDB
sudo mysql -u root -p
# Entrer le mot de passe root
```

Dans le shell MariaDB :

```sql
-- Creer la base de donnees
CREATE DATABASE vr_meeting CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

-- Creer un utilisateur dedie (NE PAS utiliser root !)
CREATE USER 'vr_user'@'localhost' IDENTIFIED BY 'MOT_DE_PASSE_FORT_ICI';

-- Donner les droits sur la base
GRANT ALL PRIVILEGES ON vr_meeting.* TO 'vr_user'@'localhost';

-- Appliquer les changements
FLUSH PRIVILEGES;

-- Verifier
SHOW DATABASES;
SELECT User, Host FROM mysql.user;

-- Quitter
EXIT;
```

### 6.4 Creer la table des utilisateurs

```bash
# Se connecter avec le nouvel utilisateur
mysql -u vr_user -p vr_meeting
# Entrer le mot de passe de vr_user
```

```sql
-- Creer la table users
CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,
    display_name VARCHAR(100),
    avatar_color VARCHAR(20) DEFAULT '#3498db',
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP NULL,
    INDEX idx_username (username),
    INDEX idx_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- Verifier
DESCRIBE users;

-- Quitter
EXIT;
```

---

## 7. Etape 4 : Deployer le code serveur

### 7.1 Creer le dossier de l'application

```bash
# Creer le dossier
sudo mkdir -p /opt/vr-meeting-server
sudo chown vrmeeting:vrmeeting /opt/vr-meeting-server

# Aller dans le dossier
cd /opt/vr-meeting-server
```

### 7.2 Transferer les fichiers

**Option A : Depuis votre ordinateur (avec scp)**

```bash
# Sur votre ordinateur local, pas sur le serveur !
scp -r LocalServ/Server/* vrmeeting@IP_SERVEUR:/opt/vr-meeting-server/
```

**Option B : Avec Git (si le projet est sur un depot)**

```bash
# Sur le serveur
cd /opt/vr-meeting-server
git clone https://votre-depot.git .
```

**Option C : Copier-coller manuellement**

Creez les fichiers sur le serveur :

```bash
cd /opt/vr-meeting-server
nano server.js   # Coller le contenu de server.js
nano auth.js     # Coller le contenu de auth.js
nano db.js       # Coller le contenu de db.js
nano package.json
```

Contenu de `package.json` :

```json
{
  "name": "vr-meeting-server",
  "version": "1.0.0",
  "description": "WebSocket server for VR Meeting",
  "main": "server.js",
  "scripts": {
    "start": "node server.js"
  },
  "dependencies": {
    "ws": "^8.14.2",
    "uuid": "^9.0.0",
    "mysql2": "^3.6.0",
    "bcrypt": "^5.1.1"
  }
}
```

### 7.3 Installer les dependances

```bash
cd /opt/vr-meeting-server
npm install
```

### 7.4 Configurer les variables d'environnement

Creez un fichier `.env` :

```bash
nano /opt/vr-meeting-server/.env
```

Contenu :

```bash
# Port du serveur WebSocket
PORT=8080

# Configuration de la base de donnees
DB_HOST=localhost
DB_PORT=3306
DB_USER=vr_user
DB_PASSWORD=MOT_DE_PASSE_FORT_ICI
DB_NAME=vr_meeting

# Environnement
NODE_ENV=production
```

**IMPORTANT : Securisez ce fichier !**

```bash
chmod 600 /opt/vr-meeting-server/.env
```

### 7.5 Modifier db.js pour utiliser les variables d'environnement

Assurez-vous que `db.js` charge le fichier `.env` :

```bash
nano /opt/vr-meeting-server/db.js
```

Ajoutez au debut du fichier :

```javascript
require('dotenv').config();
```

Puis installez dotenv :

```bash
npm install dotenv
```

### 7.6 Tester le serveur manuellement

```bash
cd /opt/vr-meeting-server
node server.js
```

Vous devriez voir :

```
[DB] Connected to MariaDB
[SERVER] WebSocket server started on port 8080
```

Appuyez sur `Ctrl+C` pour arreter.

### 7.7 Lancer avec PM2 (production)

```bash
# Demarrer avec PM2
pm2 start server.js --name vr-meeting

# Verifier le statut
pm2 status

# Voir les logs
pm2 logs vr-meeting

# Configurer le demarrage automatique au boot
pm2 startup
# Suivre les instructions affichees, puis :
pm2 save
```

**Commandes PM2 utiles :**

```bash
pm2 status              # Voir l'etat des applications
pm2 logs vr-meeting     # Voir les logs en temps reel
pm2 restart vr-meeting  # Redemarrer
pm2 stop vr-meeting     # Arreter
pm2 delete vr-meeting   # Supprimer
pm2 monit               # Surveillance en temps reel
```

---

## 8. Etape 5 : Configurer le pare-feu

### 8.1 Avec UFW (Ubuntu)

```bash
# Activer le pare-feu
sudo ufw enable

# Autoriser SSH (IMPORTANT : ne pas s'enfermer dehors !)
sudo ufw allow 22/tcp

# Autoriser HTTP et HTTPS
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Autoriser le port WebSocket (si pas de reverse proxy)
sudo ufw allow 8080/tcp

# Verifier les regles
sudo ufw status verbose
```

### 8.2 Avec iptables (si pas UFW)

```bash
# Autoriser SSH
sudo iptables -A INPUT -p tcp --dport 22 -j ACCEPT

# Autoriser HTTP/HTTPS
sudo iptables -A INPUT -p tcp --dport 80 -j ACCEPT
sudo iptables -A INPUT -p tcp --dport 443 -j ACCEPT

# Autoriser WebSocket
sudo iptables -A INPUT -p tcp --dport 8080 -j ACCEPT

# Sauvegarder
sudo iptables-save > /etc/iptables.rules
```

### 8.3 Pare-feu d'entreprise

Si votre entreprise a un pare-feu externe (Fortinet, Palo Alto, etc.), demandez a l'equipe reseau d'ouvrir :

- Port **443 TCP** (entrant) vers votre serveur
- Ou port **8080 TCP** si vous n'utilisez pas HTTPS

---

## 9. Etape 6 : Configurer HTTPS (SSL)

### Pourquoi HTTPS ?

- **Securite** : Les donnees sont chiffrees
- **Compatibilite** : Certains navigateurs/appareils bloquent les connexions non securisees
- **WebSocket securise** : `wss://` au lieu de `ws://`

### 9.1 Installer nginx

```bash
sudo apt install -y nginx
sudo systemctl start nginx
sudo systemctl enable nginx
```

### 9.2 Configurer nginx comme reverse proxy

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

Contenu :

```nginx
# Redirection HTTP vers HTTPS
server {
    listen 80;
    server_name vr.entreprise.com;  # CHANGEZ par votre domaine
    return 301 https://$server_name$request_uri;
}

# Configuration HTTPS
server {
    listen 443 ssl http2;
    server_name vr.entreprise.com;  # CHANGEZ par votre domaine

    # Certificats SSL (seront crees par Certbot)
    ssl_certificate /etc/letsencrypt/live/vr.entreprise.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/vr.entreprise.com/privkey.pem;

    # Parametres SSL securises
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
    ssl_prefer_server_ciphers off;

    # Headers de securite
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;

    # Proxy vers Node.js
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;

        # IMPORTANT pour WebSocket
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";

        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Timeouts pour WebSocket (connexions longues)
        proxy_connect_timeout 60s;
        proxy_send_timeout 60s;
        proxy_read_timeout 3600s;  # 1 heure
    }
}
```

### 9.3 Activer la configuration

```bash
# Creer le lien symbolique
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/

# Supprimer la config par defaut
sudo rm /etc/nginx/sites-enabled/default

# Tester la configuration
sudo nginx -t

# Si OK, recharger nginx
sudo systemctl reload nginx
```

### 9.4 Obtenir un certificat SSL avec Let's Encrypt

```bash
# Installer Certbot
sudo apt install -y certbot python3-certbot-nginx

# Obtenir le certificat (REMPLACEZ par votre domaine)
sudo certbot --nginx -d vr.entreprise.com

# Suivre les instructions :
# - Entrer votre email
# - Accepter les conditions
# - Choisir de rediriger HTTP vers HTTPS (recommande)
```

Le certificat sera renouvele automatiquement.

### 9.5 Alternative : Certificat d'entreprise

Si votre entreprise a son propre certificat SSL :

```bash
# Copier les certificats
sudo cp votre_certificat.crt /etc/ssl/certs/vr-meeting.crt
sudo cp votre_cle_privee.key /etc/ssl/private/vr-meeting.key

# Modifier nginx pour utiliser ces certificats
sudo nano /etc/nginx/sites-available/vr-meeting
```

Remplacer les lignes ssl_certificate par :

```nginx
ssl_certificate /etc/ssl/certs/vr-meeting.crt;
ssl_certificate_key /etc/ssl/private/vr-meeting.key;
```

---

## 10. Etape 7 : Modifier le client Unity

### 10.1 Changer l'URL du serveur

Dans Unity, ouvrez le fichier `Assets/Scrips/Network/VRNetworkManager.cs` :

**AVANT (developpement local) :**
```csharp
public string serverUrl = "ws://localhost:8080";
```

**APRES (production avec HTTPS) :**
```csharp
public string serverUrl = "wss://vr.entreprise.com";
```

**OU sans HTTPS (moins securise) :**
```csharp
public string serverUrl = "ws://IP_DU_SERVEUR:8080";
```

### 10.2 Build et distribution

```
1. Dans Unity : File > Build Settings
2. Selectionner la plateforme (Windows, Android/Quest, etc.)
3. Cliquer sur "Build"
4. Distribuer l'executable aux utilisateurs
```

---

## 11. Etape 8 : Tester la connexion

### 11.1 Test depuis le serveur

```bash
# Verifier que Node.js ecoute
sudo netstat -tlnp | grep 8080
# Doit afficher : tcp 0 0 0.0.0.0:8080 ... node

# Verifier que nginx ecoute
sudo netstat -tlnp | grep 443
# Doit afficher : tcp 0 0 0.0.0.0:443 ... nginx

# Tester WebSocket localement
curl -i -N -H "Connection: Upgrade" -H "Upgrade: websocket" http://localhost:8080
```

### 11.2 Test depuis l'exterieur

```bash
# Depuis votre ordinateur (pas le serveur)
curl -I https://vr.entreprise.com
# Doit retourner HTTP/2 200 ou une reponse WebSocket
```

### 11.3 Test avec le client Unity

1. Lancez le jeu Unity
2. Verifiez la console pour "Connected" ou "Assigned ID"
3. Testez la creation/rejoindre une salle
4. Testez avec 2 clients

---

## 12. Maintenance et surveillance

### 12.1 Logs

```bash
# Logs de l'application Node.js
pm2 logs vr-meeting

# Logs nginx
sudo tail -f /var/log/nginx/access.log
sudo tail -f /var/log/nginx/error.log

# Logs systeme
sudo journalctl -u nginx -f
```

### 12.2 Surveillance

```bash
# Surveillance PM2 en temps reel
pm2 monit

# Utilisation CPU/RAM
htop

# Espace disque
df -h

# Connexions actives
ss -tuln
```

### 12.3 Sauvegardes

```bash
# Sauvegarder la base de donnees (a faire regulierement !)
mysqldump -u vr_user -p vr_meeting > backup_$(date +%Y%m%d).sql

# Automatiser avec cron
crontab -e
# Ajouter : 0 3 * * * mysqldump -u vr_user -pMOT_DE_PASSE vr_meeting > /backups/vr_meeting_$(date +\%Y\%m\%d).sql
```

### 12.4 Mises a jour

```bash
# Mettre a jour le code
cd /opt/vr-meeting-server
git pull  # Si vous utilisez Git

# Reinstaller les dependances si necessaire
npm install

# Redemarrer
pm2 restart vr-meeting
```

---

## 13. Depannage

### Probleme : "Connection refused"

```bash
# Verifier que le serveur tourne
pm2 status

# Verifier les ports
sudo netstat -tlnp | grep -E '8080|443'

# Verifier le pare-feu
sudo ufw status
```

### Probleme : "SSL certificate error"

```bash
# Verifier le certificat
sudo certbot certificates

# Renouveler manuellement
sudo certbot renew

# Verifier la config nginx
sudo nginx -t
```

### Probleme : "Database connection failed"

```bash
# Verifier que MariaDB tourne
sudo systemctl status mariadb

# Tester la connexion
mysql -u vr_user -p vr_meeting -e "SELECT 1;"

# Verifier les identifiants dans .env
cat /opt/vr-meeting-server/.env
```

### Probleme : "WebSocket timeout"

```bash
# Verifier les timeouts nginx
# Dans /etc/nginx/sites-available/vr-meeting
# proxy_read_timeout doit etre grand (3600s)

# Verifier le heartbeat dans server.js
# HEARTBEAT_INTERVAL = 30000 (30 secondes)
```

### Voir les erreurs en temps reel

```bash
# Terminal 1 : Logs application
pm2 logs vr-meeting

# Terminal 2 : Logs nginx
sudo tail -f /var/log/nginx/error.log
```

---

## 14. Checklist de deploiement

Utilisez cette checklist pour verifier que tout est fait :

### Preparation

- [ ] Serveur Linux disponible avec IP fixe
- [ ] Acces SSH root ou sudo
- [ ] Nom de domaine pointe vers le serveur (si utilise)
- [ ] Ports 80, 443, 8080 autorises dans le pare-feu entreprise

### Installation

- [ ] Systeme mis a jour (`apt update && apt upgrade`)
- [ ] Node.js 18 installe (`node --version`)
- [ ] PM2 installe (`pm2 --version`)
- [ ] MariaDB installe et securise
- [ ] Base de donnees `vr_meeting` creee
- [ ] Utilisateur `vr_user` cree avec les droits
- [ ] Table `users` creee

### Deploiement

- [ ] Code serveur copie dans `/opt/vr-meeting-server`
- [ ] Fichier `.env` configure avec les bons identifiants
- [ ] `npm install` execute
- [ ] Test manuel `node server.js` OK
- [ ] PM2 configure (`pm2 start`, `pm2 save`, `pm2 startup`)

### Securite

- [ ] Pare-feu UFW active
- [ ] nginx installe et configure
- [ ] Certificat SSL installe (Let's Encrypt ou entreprise)
- [ ] HTTPS fonctionne (`curl -I https://domaine.com`)
- [ ] Fichier `.env` protege (`chmod 600`)

### Client Unity

- [ ] URL changee vers `wss://domaine.com`
- [ ] Build effectue
- [ ] Test de connexion OK
- [ ] Test multi-utilisateurs OK

### Documentation

- [ ] Identifiants documentes (dans un endroit securise !)
- [ ] Procedure de sauvegarde en place
- [ ] Contact support identifie

---

## Informations de connexion (A REMPLIR ET SECURISER)

**NE PAS LAISSER CE FICHIER ACCESSIBLE !**

```
Serveur : ____________________
IP : ____________________
SSH User : ____________________
SSH Port : 22

MariaDB Root Password : ____________________
MariaDB vr_user Password : ____________________

Domaine : ____________________
Certificat SSL expire le : ____________________

PM2 App Name : vr-meeting
Chemin application : /opt/vr-meeting-server
```

---

## Contact support

En cas de probleme :

- **Equipe IT** : [email/telephone]
- **Developpeur** : [email/telephone]
- **Documentation** : Ce fichier + NETWORKING_GUIDE.md

---

*Document cree pour le deploiement de VR Meeting Server*
*Derniere mise a jour : [DATE]*
