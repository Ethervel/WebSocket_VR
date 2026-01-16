# Documentation VR Meeting Platform

## Contenu

| Fichier | Description |
|---------|-------------|
| `SERVER_DOCUMENTATION.md` | Documentation complete du serveur (architecture, messages, deploiement) |
| `SEQUENCE_DIAGRAMS.md` | Diagrammes de sequence des flux principaux |
| `schema.sql` | Script SQL pour initialiser la base de donnees MariaDB |

## Quick Start

### 1. Base de donnees
```bash
mysql -u root -p < schema.sql
```

### 2. Serveur Node.js
```bash
cd Server
npm install
cp .env.example .env  # Configurer les variables
npm start
```

### 3. Client Unity
- Ouvrir le projet dans Unity 6000.2.14f1
- Configurer `VRNetworkManager.serverUrl` avec l'adresse du serveur
- Build pour Quest/PCVR/Desktop

## Architecture Resumee

```
Client Unity ◄──── WebSocket ────► Serveur Node.js ◄────► MariaDB
     │                                    │
     └──────── WebRTC P2P (voix) ────────┘
```

## Ports

| Service | Port | Protocol |
|---------|------|----------|
| WebSocket Server | 8080 | WS/WSS |
| MariaDB | 3306 | TCP |
| STUN (Google) | 19302 | UDP |
