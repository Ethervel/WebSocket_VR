# Demande de Configuration MariaDB

## Projet VR Meeting Room

**Date:** Janvier 2026
**Demandeur:** [Votre nom]
**Projet:** Application VR de réunion collaborative
**Environnement cible:** Production entreprise

---

## 1. Contexte

Nous développons une application de réunion en réalité virtuelle (Unity) qui nécessite une base de données pour :
- Authentification des utilisateurs
- Persistance des profils utilisateurs
- Historique des réunions (conformité GDPR)
- Métadonnées des fichiers partagés

### Architecture technique

```
┌─────────────────┐     WebSocket      ┌─────────────────┐      TCP/TLS      ┌─────────────────┐
│  Unity Client   │ ◄────────────────► │  Node.js Server │ ◄───────────────► │    MariaDB      │
│  (VR/Desktop)   │     Port 8080      │   (Backend)     │     Port 3306     │   (Entreprise)  │
└─────────────────┘                    └─────────────────┘                    └─────────────────┘
```

**Important:** Le client Unity ne se connecte JAMAIS directement à la base de données. Toutes les requêtes passent par le serveur Node.js.

---

## 2. Ce dont nous avons besoin

### 2.1 Base de données

| Élément | Valeur demandée |
|---------|-----------------|
| **Nom de la base** | `vr_meeting_db` (ou selon convention entreprise) |
| **Charset** | `utf8mb4` |
| **Collation** | `utf8mb4_unicode_ci` |

### 2.2 Utilisateur applicatif

| Élément | Valeur demandée |
|---------|-----------------|
| **Username** | `vr_app_user` (ou selon convention) |
| **Permissions** | `SELECT, INSERT, UPDATE, DELETE` sur `vr_meeting_db.*` |
| **Host autorisé** | IP du serveur Node.js (voir section 4) |

**Note:** L'utilisateur n'a PAS besoin des droits `CREATE`, `DROP`, `ALTER` en production. Les migrations seront gérées séparément.

### 2.3 Connexion

| Élément | Question |
|---------|----------|
| **Host** | Quelle est l'adresse du serveur MariaDB ? |
| **Port** | 3306 par défaut, ou autre ? |
| **TLS requis** | La connexion doit-elle être chiffrée ? (recommandé) |
| **Certificat CA** | Si TLS, avez-vous un certificat CA à fournir ? |

---

## 3. Schéma de base de données

Voici les tables que nous devons créer. **Pouvez-vous valider ce schéma ?**

```sql
-- ============================================
-- TABLE: users
-- Stocke les comptes utilisateurs
-- ============================================
CREATE TABLE users (
    id INT AUTO_INCREMENT PRIMARY KEY,
    username VARCHAR(50) UNIQUE NOT NULL,
    email VARCHAR(100) UNIQUE NOT NULL,
    password_hash VARCHAR(255) NOT NULL,      -- bcrypt hash (jamais en clair)
    avatar_color INT DEFAULT 0,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    last_login TIMESTAMP NULL,
    is_active BOOLEAN DEFAULT TRUE,

    INDEX idx_email (email),
    INDEX idx_username (username)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================
-- TABLE: rooms
-- Salles de réunion persistantes
-- ============================================
CREATE TABLE rooms (
    id INT AUTO_INCREMENT PRIMARY KEY,
    room_code VARCHAR(6) UNIQUE NOT NULL,     -- Code d'accès (ex: "ABC123")
    room_name VARCHAR(100),
    host_user_id INT,
    room_type ENUM('Lobby', 'MeetingRoomA', 'MeetingRoomB') DEFAULT 'Lobby',
    is_persistent BOOLEAN DEFAULT FALSE,
    created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (host_user_id) REFERENCES users(id) ON DELETE SET NULL,
    INDEX idx_room_code (room_code)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================
-- TABLE: shared_files
-- Métadonnées des fichiers partagés (pas le contenu)
-- ============================================
CREATE TABLE shared_files (
    id INT AUTO_INCREMENT PRIMARY KEY,
    room_id INT,
    uploader_id INT,
    filename VARCHAR(255) NOT NULL,
    file_path VARCHAR(500),                   -- Chemin stockage fichier
    file_size INT,                            -- Taille en octets
    mime_type VARCHAR(100),
    uploaded_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
    FOREIGN KEY (uploader_id) REFERENCES users(id) ON DELETE SET NULL,
    INDEX idx_room_files (room_id, uploaded_at)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================
-- TABLE: meeting_logs
-- Historique pour conformité GDPR
-- ============================================
CREATE TABLE meeting_logs (
    id INT AUTO_INCREMENT PRIMARY KEY,
    room_id INT,
    user_id INT,
    action ENUM('join', 'leave', 'share_screen', 'upload_file', 'present') NOT NULL,
    timestamp TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

    FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE SET NULL,
    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL,
    INDEX idx_user_logs (user_id, timestamp),
    INDEX idx_room_logs (room_id, timestamp)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- ============================================
-- TABLE: user_consents (GDPR)
-- Consentements utilisateurs
-- ============================================
CREATE TABLE user_consents (
    id INT AUTO_INCREMENT PRIMARY KEY,
    user_id INT NOT NULL,
    consent_type VARCHAR(50) NOT NULL,        -- 'data_processing', 'analytics', etc.
    granted BOOLEAN DEFAULT FALSE,
    granted_at TIMESTAMP NULL,
    revoked_at TIMESTAMP NULL,
    ip_address VARCHAR(45),                   -- IPv4 ou IPv6

    FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE,
    UNIQUE KEY unique_consent (user_id, consent_type)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
```

---

## 4. Informations à nous fournir

Merci de nous communiquer les informations suivantes une fois la configuration effectuée :

| Information | Valeur |
|-------------|--------|
| **Host MariaDB** | `_____________________________` |
| **Port** | `_____________________________` |
| **Nom de la base** | `_____________________________` |
| **Username** | `_____________________________` |
| **Password** | (à transmettre de manière sécurisée) |
| **TLS requis** | Oui / Non |
| **Certificat CA** | (si applicable, fichier joint) |

### IP du serveur Node.js à autoriser

| Environnement | IP |
|---------------|-----|
| **Développement** | `127.0.0.1` (local) |
| **Staging** | `_____________________________` |
| **Production** | `_____________________________` |

---

## 5. Estimations de volume

| Métrique | Estimation |
|----------|------------|
| **Utilisateurs** | ~50-200 initialement, jusqu'à 1000 |
| **Réunions/jour** | ~10-50 |
| **Fichiers partagés/mois** | ~100-500 (métadonnées uniquement) |
| **Logs/mois** | ~5000-20000 entrées |
| **Taille DB estimée** | < 1 GB première année |

---

## 6. Considérations GDPR

Notre application doit être conforme au RGPD. Nous aurons besoin de :

1. **Droit à l'effacement** - Suppression des données utilisateur sur demande
2. **Export des données** - Export de toutes les données d'un utilisateur
3. **Rétention limitée** - Suppression automatique des logs > 90 jours

**Question:** Y a-t-il des procédures entreprise existantes pour ces aspects ?

---

## 7. Backup et haute disponibilité

**Questions pour l'équipe infrastructure :**

- [ ] Les backups sont-ils automatiques ? Quelle fréquence ?
- [ ] Y a-t-il une réplication (master/slave) ?
- [ ] Quel est le RPO/RTO garanti ?
- [ ] Procédure de restauration en cas d'incident ?

---

## 8. Environnements demandés

| Environnement | Usage | Priorité |
|---------------|-------|----------|
| **Développement** | Tests locaux | Basse (peut être local) |
| **Staging** | Tests d'intégration | Moyenne |
| **Production** | Utilisateurs finaux | Haute |

---

## 9. Timeline souhaitée

| Étape | Date souhaitée |
|-------|----------------|
| Validation du schéma | __________ |
| Création DB staging | __________ |
| Création DB production | __________ |
| Tests de connexion | __________ |

---

## 10. Contact

**Pour toute question technique :**

- **Nom:** [Votre nom]
- **Email:** [Votre email]
- **Téléphone:** [Votre téléphone]

---

## Annexe A: Exemple de connexion Node.js

Voici comment notre serveur se connectera (pour information) :

```javascript
const mariadb = require('mariadb');

const pool = mariadb.createPool({
    host: process.env.DB_HOST,
    port: process.env.DB_PORT || 3306,
    user: process.env.DB_USER,
    password: process.env.DB_PASSWORD,
    database: process.env.DB_DATABASE,
    connectionLimit: 10,
    // Si TLS requis :
    ssl: {
        ca: fs.readFileSync('/path/to/ca-cert.pem')
    }
});
```

## Annexe B: Requêtes types

Voici les types de requêtes que l'application effectuera :

```sql
-- Authentification (fréquent)
SELECT id, password_hash FROM users WHERE username = ?;

-- Mise à jour last_login (fréquent)
UPDATE users SET last_login = NOW() WHERE id = ?;

-- Création de room (modéré)
INSERT INTO rooms (room_code, room_name, host_user_id) VALUES (?, ?, ?);

-- Logs meeting (fréquent)
INSERT INTO meeting_logs (room_id, user_id, action) VALUES (?, ?, ?);

-- Nettoyage GDPR (scheduled, rare)
DELETE FROM meeting_logs WHERE timestamp < DATE_SUB(NOW(), INTERVAL 90 DAY);
```

---

*Document généré pour le projet VR Meeting Room - Janvier 2026*
