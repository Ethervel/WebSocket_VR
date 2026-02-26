# Configuration Client Unity - Guide Rapide

> **Serveur:** vr-meeting-server (192.168.0.55 / vrmeeting-test.duckdns.org)

---

## Option A : Connexion LAN (meme reseau local)

Utiliser cette option si le PC client est sur le **meme reseau Wi-Fi/Ethernet** que le serveur.

### Configuration Unity

1. Ouvrir le projet Unity
2. Ouvrir la scene `Assets/Scenes/Bootstrap.unity`
3. Selectionner le GameObject avec **VRNetworkManager**

| Champ | Valeur |
|-------|--------|
| **Server Url** | `ws://192.168.0.55:8080` |

4. Selectionner le GameObject avec **VoiceChatManager**

| Champ | Valeur |
|-------|--------|
| Use Custom Turn Server | `true` |
| Custom Turn Url | `turn:192.168.0.55:3478` |
| Custom Turns Url | *(laisser vide)* |
| Custom Turn Username | `vrmeeting` |
| Custom Turn Credential | `JJkk2812` |
| Enable Turn Tcp | `true` |

### Test de connexion (PowerShell)

```powershell
Test-NetConnection -ComputerName 192.168.0.55 -Port 8080
Test-NetConnection -ComputerName 192.168.0.55 -Port 3478
```

Les deux doivent afficher `TcpTestSucceeded : True`.

---

## Option B : Connexion Internet (SSL/TLS)

Utiliser cette option si le PC client est sur un **reseau different** (internet, autre bureau, etc.)

### Configuration Unity

1. Ouvrir le projet Unity
2. Ouvrir la scene `Assets/Scenes/Bootstrap.unity`
3. Selectionner le GameObject avec **VRNetworkManager**

| Champ | Valeur |
|-------|--------|
| **Server Url** | `wss://vrmeeting-test.duckdns.org` |

4. Selectionner le GameObject avec **VoiceChatManager**

| Champ | Valeur |
|-------|--------|
| Use Custom Turn Server | `true` |
| Custom Turn Url | `turn:vrmeeting-test.duckdns.org:3478` |
| Custom Turns Url | `turns:vrmeeting-test.duckdns.org:5349` |
| Custom Turn Username | `vrmeeting` |
| Custom Turn Credential | `JJkk2812` |
| Enable Turn Tcp | `true` |

### Test de connexion (PowerShell)

```powershell
Test-NetConnection -ComputerName vrmeeting-test.duckdns.org -Port 443
Test-NetConnection -ComputerName vrmeeting-test.duckdns.org -Port 3478
```

---

## Resume des differences

| Element | LAN (Option A) | Internet (Option B) |
|---------|----------------|---------------------|
| WebSocket | `ws://192.168.0.55:8080` | `wss://vrmeeting-test.duckdns.org` |
| TURN | `turn:192.168.0.55:3478` | `turn:vrmeeting-test.duckdns.org:3478` |
| TURNS | *(non utilise)* | `turns:vrmeeting-test.duckdns.org:5349` |
| Securite | Non chiffre | Chiffre (TLS) |
| Use case | Dev, tests locaux | Production, acces externe |

---

## Verifier les logs serveur

Sur le serveur Linux :

```bash
# Logs WebSocket (PM2)
pm2 logs

# Logs TURN
sudo tail -f /var/log/turnserver/turnserver.log
```

---

## Depannage

| Probleme | Solution |
|----------|----------|
| `Connection refused` | Verifier que le serveur tourne : `pm2 status` |
| `Timeout` | Verifier le pare-feu : `sudo ufw status` |
| `SSL error` (Option B) | Verifier le certificat : `sudo certbot certificates` |
| `Voice chat ne marche pas` | Verifier coturn : `sudo systemctl status coturn` |
| `Pas de son` | Verifier micro Windows + touche V (push-to-talk) |

---

## Credentials serveur

```
TURN Username: vrmeeting
TURN Password: JJkk2812
```

> **Note:** Ces credentials sont dans `/etc/turnserver.conf` sur le serveur.
