# Guide de Déploiement - VR Meeting Rooms

## Vue d'ensemble du projet

Application de réunion en réalité virtuelle permettant à plusieurs utilisateurs de se retrouver dans un espace virtuel partagé avec :
- Communication audio spatiale (WebRTC)
- Tableau blanc collaboratif
- Partage d'écran
- Partage de fichiers
- Avatars personnalisables

---

## Architecture actuelle (Développement local)

```
┌──────────────┐         ┌──────────────┐
│ Client Unity │◄───────►│ Serveur Node │
│  (VR / PC)   │  ws://  │  localhost   │
└──────────────┘         └──────────────┘
```

**Problème :** Fonctionne uniquement en local. Pour un accès externe, il faut déployer sur un serveur accessible depuis Internet.

---

## Ce qui est nécessaire pour la production

### 1. Serveur d'hébergement

**Besoin :** Un serveur Linux accessible depuis Internet.

| Spécification | Minimum | Recommandé |
|---------------|---------|------------|
| OS | Ubuntu 20.04+ / Debian 11+ | Ubuntu 22.04 LTS |
| RAM | 2 Go | 4 Go |
| CPU | 1 vCPU | 2 vCPU |
| Stockage | 20 Go SSD | 40 Go SSD |
| Réseau | IP publique fixe | IP publique fixe |

**Pourquoi :** Le serveur Node.js doit être accessible par tous les clients VR depuis n'importe quel réseau.

---

### 2. Nom de domaine + Certificat SSL

**Besoin :** Un sous-domaine pointant vers le serveur (ex: `meeting.entreprise.com`)

**Pourquoi :**
- Les casques VR (Quest) **bloquent** les connexions WebSocket non sécurisées (`ws://`)
- Le protocole `wss://` (WebSocket Secure) nécessite un certificat SSL
- Let's Encrypt fournit des certificats SSL gratuits

---

### 3. Ports réseau à ouvrir

| Port | Protocole | Service | Obligatoire |
|------|-----------|---------|-------------|
| **443** | TCP | HTTPS / WebSocket sécurisé | ✅ Oui |
| **3478** | TCP + UDP | TURN (traversée NAT pour audio) | ✅ Oui |
| **5349** | TCP | TURN over TLS | Recommandé |
| **49152-65535** | UDP | Flux audio WebRTC | ✅ Oui |

**Pourquoi :**
- **Port 443 :** Communication principale entre clients et serveur
- **Ports TURN :** Permettent la communication audio même derrière des firewalls d'entreprise. Sans TURN, l'audio ne fonctionnera pas dans certains réseaux restrictifs.

---

### 4. Logiciels à installer

| Logiciel | Rôle | Licence |
|----------|------|---------|
| **Node.js** (v18+) | Exécute le serveur WebSocket | Open source (MIT) |
| **Nginx** | Reverse proxy + terminaison SSL | Open source (BSD) |
| **Coturn** | Serveur TURN pour l'audio VR | Open source (BSD) |
| **MariaDB** | Base de données utilisateurs | Open source (GPL) |
| **PM2** | Gestionnaire de processus Node | Open source (AGPL) |

**Pourquoi chaque composant :**

- **Node.js :** Le serveur est écrit en JavaScript, nécessite Node.js pour s'exécuter
- **Nginx :** Gère le SSL et redirige le trafic vers Node.js de manière sécurisée
- **Coturn :** Indispensable pour que l'audio WebRTC fonctionne à travers les NAT/firewalls
- **MariaDB :** Stocke les comptes utilisateurs (login, mot de passe hashé, profil)
- **PM2 :** Redémarre automatiquement le serveur en cas de crash

---

## Fichiers fournis

```
Server/
├── server.js          # Serveur WebSocket (gestion des rooms, sync positions, signaling)
├── auth.js            # Authentification (register, login, bcrypt)
├── db.js              # Configuration connexion MariaDB
├── package.json       # Liste des dépendances Node.js
└── package-lock.json  # Versions exactes des dépendances
```

**Taille totale :** ~50 Ko (hors node_modules)

---

## Flux de données

```
┌─────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  CLIENT VR/PC                     SERVEUR                           │
│  ────────────                     ───────                           │
│                                                                     │
│  ┌─────────────┐    wss://443    ┌─────────────┐                   │
│  │ Application │◄───────────────►│   Nginx     │                   │
│  │   Unity     │                 │   (SSL)     │                   │
│  └──────┬──────┘                 └──────┬──────┘                   │
│         │                               │                           │
│         │                        ┌──────▼──────┐                   │
│         │                        │   Node.js   │◄──► MariaDB       │
│         │                        │   (8080)    │     (users)       │
│         │                        └─────────────┘                   │
│         │                                                           │
│         │  Audio WebRTC          ┌─────────────┐                   │
│         └───────────────────────►│   Coturn    │                   │
│            (TURN relay)          │   (3478)    │                   │
│                                  └─────────────┘                   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Pourquoi TURN est indispensable

### Le problème du NAT

La plupart des réseaux (entreprise, domicile) utilisent du NAT. Les clients ne peuvent pas se connecter directement entre eux pour l'audio.

```
Sans TURN :
Client A ──────X────── Client B
         Firewall bloque

Avec TURN :
Client A ───► TURN Server ◄─── Client B
              (relais)
```

### Impact

| Situation | Sans TURN | Avec TURN |
|-----------|-----------|-----------|
| Réseau domestique simple | ✅ Marche (~80%) | ✅ Marche |
| Réseau entreprise | ❌ Échoue souvent | ✅ Marche |
| 4G/5G mobile | ⚠️ Aléatoire | ✅ Marche |
| VPN | ❌ Échoue souvent | ✅ Marche |

**Conclusion :** TURN garantit que l'audio fonctionne dans 100% des cas.

---

## Base de données

### Table des utilisateurs

```sql
CREATE TABLE users (
    id INT PRIMARY KEY AUTO_INCREMENT,
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(255) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,    -- Hashé avec bcrypt
    display_name VARCHAR(100),
    avatar_color VARCHAR(20),
    last_login TIMESTAMP,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
);
```

### Données stockées

| En base de données | En mémoire serveur (volatile) |
|-------------------|-------------------------------|
| Comptes utilisateurs | Rooms actives |
| Mots de passe (hashés) | Joueurs connectés |
| Profils (nom, couleur) | État du whiteboard |
| Historique connexions | Fichiers partagés temporaires |

**Note :** Les rooms et l'état en temps réel sont perdus si le serveur redémarre.

---

## Estimation des ressources

### Bande passante

| Flux | Par utilisateur | 10 utilisateurs |
|------|-----------------|-----------------|
| Position VR (30 Hz) | ~3 Ko/s | ~30 Ko/s |
| Audio WebRTC | ~20-50 Ko/s | Via TURN si nécessaire |
| Whiteboard | ~5 Ko/s (si dessin) | ~50 Ko/s |
| Screen share | ~100 Ko/s (si actif) | ~100 Ko/s |

**Total estimé :** ~200-500 Ko/s pour une réunion de 10 personnes

### Connexions simultanées

Un serveur Node.js peut gérer facilement **plusieurs centaines** de connexions WebSocket simultanées avec les spécifications recommandées.

---

## Sécurité

| Mesure | Implémentation |
|--------|----------------|
| Chiffrement transport | TLS 1.3 via Nginx (wss://) |
| Mots de passe | Hashés avec bcrypt (10 rounds) |
| Reconnexion | Exponential backoff (anti-flood) |
| Rate limiting | 60 messages/seconde max par client |

---

## Coûts estimés

| Élément | Coût mensuel |
|---------|--------------|
| VPS (2-4 Go RAM) | 10-30 €/mois |
| Nom de domaine | ~1 €/mois (12€/an) |
| Certificat SSL | Gratuit (Let's Encrypt) |
| Logiciels | Gratuit (open source) |
| **Total** | **~15-35 €/mois** |

---

## Questions pour décision

1. **Serveur :** Utiliser un serveur existant ou créer un nouveau VPS ?
2. **Domaine :** Sous-domaine de l'entreprise ou nouveau domaine ?
3. **Base de données :** MariaDB dédié ou instance existante ?
4. **Maintenance :** Qui sera responsable des mises à jour ?

---

## Prochaines étapes

Une fois les décisions prises, je peux fournir :
- Scripts d'installation automatisés
- Configuration détaillée de chaque service
- Procédures de test
- Documentation de maintenance

---

## Contact

Pour toute question technique sur l'architecture ou le code :
- Dossier serveur : `Server/`
- Documentation code : `CLAUDE.md`
