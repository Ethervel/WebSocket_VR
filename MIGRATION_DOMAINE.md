# Migration vers un domaine officiel

> **Contexte :** Serveur actuellement configuré avec `vrmeeting-test.duckdns.org`
> **Objectif :** Migrer vers le domaine officiel de l'entreprise

---

## Prérequis

- [ ] Accès au panneau DNS du domaine de l'entreprise
- [ ] Accès au routeur pour le port forwarding
- [ ] Nouveau domaine choisi (ex: `meeting.entreprise.com`)

---

## Étape 1 : Configuration DNS

Dans le panneau de contrôle DNS de votre registrar, créer :

| Type | Nom | Valeur | TTL |
|------|-----|--------|-----|
| A | meeting | 61.85.247.215 | 300 |

Vérifier la propagation (attendre quelques minutes) :

```bash
dig meeting.entreprise.com +short
# Doit afficher : 61.85.247.215
```

---

## Étape 2 : Port Forwarding sur le routeur

Accéder à l'interface du routeur (`192.168.0.1` ou `192.168.1.1`).

Configurer les redirections vers `192.168.0.55` :

| Port externe | Port interne | Protocole |
|--------------|--------------|-----------|
| 80 | 80 | TCP |
| 443 | 443 | TCP |
| 3478 | 3478 | TCP + UDP |
| 5349 | 5349 | TCP |
| 49152-65535 | 49152-65535 | UDP |

---

## Étape 3 : Nouveau certificat SSL

```bash
# Connexion au serveur
ssh vr-admin@192.168.0.55

# Obtenir le certificat (remplacer par votre domaine)
sudo /snap/bin/certbot certonly --nginx -d meeting.entreprise.com
```

---

## Étape 4 : Mettre à jour nginx

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

Remplacer tout le contenu :

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

    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;

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

---

## Étape 5 : Mettre à jour coturn

```bash
# Copier les nouveaux certificats
sudo cp /etc/letsencrypt/live/meeting.entreprise.com/fullchain.pem /etc/coturn/certs/
sudo cp /etc/letsencrypt/live/meeting.entreprise.com/privkey.pem /etc/coturn/certs/
sudo chown turnserver:turnserver /etc/coturn/certs/*.pem
sudo chmod 600 /etc/coturn/certs/*.pem

# Mettre à jour la config
sudo nano /etc/turnserver.conf
```

Modifier ces lignes :

```ini
realm=meeting.entreprise.com
server-name=meeting.entreprise.com
```

Redémarrer :

```bash
sudo systemctl restart coturn
```

---

## Étape 6 : Mettre à jour le hook de renouvellement

```bash
sudo nano /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

Changer la ligne DOMAIN :

```bash
DOMAIN="meeting.entreprise.com"
```

---

## Étape 7 : Configurer Unity

### VRNetworkManager

| Champ | Valeur |
|-------|--------|
| Server Url | `wss://meeting.entreprise.com` |

### VoiceChatManager

| Champ | Valeur |
|-------|--------|
| Use Custom Turn Server | `true` |
| Custom Turn Url | `turn:meeting.entreprise.com:3478` |
| Custom Turns Url | `turns:meeting.entreprise.com:5349` |
| Custom Turn Username | `vrmeeting` |
| Custom Turn Credential | `MotDePasseTURN123!` |
| Enable Turn Tcp | `true` |

---

## Étape 8 : Nettoyage

### Sur le serveur
DuckDNS peut être laissé tel quel ou supprimé sur duckdns.org.

### Sur votre PC Windows
Supprimer la ligne dans `C:\Windows\System32\drivers\etc\hosts` :
```
192.168.0.55 vrmeeting-test.duckdns.org
```

---

## Vérification finale

```bash
# Sur le serveur
pm2 status
sudo systemctl status nginx
sudo systemctl status coturn

# Tester SSL
curl -I https://meeting.entreprise.com

# Tester depuis l'extérieur (autre réseau)
# - Ouvrir https://meeting.entreprise.com/health
# - Doit afficher "OK"
```

---

## Résumé des fichiers modifiés

| Fichier | Action |
|---------|--------|
| `/etc/nginx/sites-available/vr-meeting` | Changer le domaine |
| `/etc/turnserver.conf` | Changer realm et server-name |
| `/etc/coturn/certs/*.pem` | Nouveaux certificats |
| `/etc/letsencrypt/renewal-hooks/deploy/coturn.sh` | Changer DOMAIN |
| Unity - VRNetworkManager | Nouvelle URL |
| Unity - VoiceChatManager | Nouvelles URLs TURN |

---

## En cas de problème

```bash
# Logs nginx
sudo tail -f /var/log/nginx/error.log

# Logs Node.js
pm2 logs vr-meeting

# Logs coturn
sudo tail -f /var/log/turnserver/turnserver.log

# Vérifier les ports
sudo ss -tlnp | grep -E '80|443|3478|5349'
```
