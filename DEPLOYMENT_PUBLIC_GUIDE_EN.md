# Public Deployment Guide - VR Meeting Rooms

> **Version:** Ubuntu 24.04 LTS + Node.js 22 LTS
> **Last updated:** February 2026
> **Objective:** Make the server accessible from any network (Internet)

---


### Server

| Element | Specification |
|---------|---------------|
| Type | VPS, cloud VM, or dedicated server |
| OS | Ubuntu 24.04 LTS (recommended) |
| RAM | 4 GB minimum, 8 GB recommended |
| CPU | 2 vCPU minimum |
| Storage | 25 GB SSD |
| Bandwidth | 100 Mbps minimum |
| IP | Static public IPv4 |


### Domain and DNS

- A domain name (e.g., `company.com`)
- Access to DNS to create A records
- Dedicated subdomain (e.g., `meeting.company.com`)

---

## Part 1: Server Preparation

### 1.1 Initial Connection

```bash
# From your local PC
ssh root@SERVER_IP
```

### 1.2 System Update

```bash
apt update && apt upgrade -y
```

### 1.3 Create a Non-Root User

```bash
# Create the user
adduser vr-admin
usermod -aG sudo vr-admin

# Copy SSH key (if using key-based auth)
mkdir -p /home/vr-admin/.ssh
cp ~/.ssh/authorized_keys /home/vr-admin/.ssh/
chown -R vr-admin:vr-admin /home/vr-admin/.ssh
chmod 700 /home/vr-admin/.ssh
chmod 600 /home/vr-admin/.ssh/authorized_keys

# Reconnect as vr-admin
exit
```

```bash
ssh vr-admin@SERVER_IP
```

### 1.4 Configure Hostname

```bash
sudo hostnamectl set-hostname vr-meeting-server
```

---

## Part 2: DNS Configuration

Before installing anything, configure DNS as Let's Encrypt requires it.

### 2.1 Create DNS Records

In your registrar/DNS control panel, add:

| Type | Name | Value | TTL |
|------|------|-------|-----|
| A | meeting | SERVER_IP | 300 |
| A | turn | SERVER_IP | 300 |

Example for `company.com`:
- `meeting.company.com` → `203.0.113.50`
- `turn.company.com` → `203.0.113.50`

### 2.2 Verify DNS Propagation

```bash
# Wait a few minutes then verify
dig meeting.company.com +short
dig turn.company.com +short
# Should display the server IP
```

Or use: https://dnschecker.org

---

## Part 3: Installing Components

### 3.1 Install Node.js 22 LTS

```bash
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs
node --version    # v22.x.x
npm --version
```

### 3.2 Install Required Tools

```bash
sudo apt install -y git nginx certbot python3-certbot-nginx poppler-utils ufw fail2ban
```

### 3.3 Copy the Server Project

**Option A: Via Git**

```bash
cd ~
git clone https://your-repo.git vr-meeting
cd vr-meeting/Server
npm install
```

**Option B: Via SCP from your local PC**

```bash
# From your Windows PC (PowerShell)
scp -r "D:\Test_project\WebSocket_VR\Server" vr-admin@SERVER_IP:~/vr-meeting/
```

Then on the server:

```bash
cd ~/vr-meeting/Server
npm install
```

### 3.4 Test Launch

```bash
cd ~/vr-meeting/Server
npm start
```

Verify the server starts correctly, then `Ctrl+C` to stop.

---

## Part 4: Configure Firewall (UFW)

### 4.1 Configure Rules

```bash
# SSH (IMPORTANT: don't lock yourself out!)
sudo ufw allow 22/tcp

# HTTP (for Let's Encrypt)
sudo ufw allow 80/tcp

# HTTPS (nginx + WebSocket)
sudo ufw allow 443/tcp

# STUN/TURN
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp

# TURN relay ports (WebRTC media)
sudo ufw allow 49152:65535/udp

# Enable firewall
sudo ufw enable

# Verify
sudo ufw status verbose
```

### 4.2 Expected Result

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

## Part 5: Configure nginx with Let's Encrypt

### 5.1 Create Initial nginx Configuration (HTTP)

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

Paste (replace `meeting.company.com` with your domain):

```nginx
server {
    listen 80;
    server_name meeting.company.com;

    # Temporary for Let's Encrypt
    location / {
        return 200 'VR Meeting Server - HTTP OK';
        add_header Content-Type text/plain;
    }
}
```

Enable the site:

```bash
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl restart nginx
```

### 5.2 Obtain Let's Encrypt Certificate

```bash
sudo certbot --nginx -d meeting.company.com
```

Answer the questions:
- Email: your email (for expiration notifications)
- Terms: Yes
- Share email: No (or Yes based on preference)
- HTTP→HTTPS redirect: 2 (Redirect)

Certbot automatically modifies the nginx config.

### 5.3 Update nginx Configuration for WebSocket

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

Replace all content with:

```nginx
# HTTP → HTTPS Redirect
server {
    listen 80;
    server_name meeting.company.com;
    return 301 https://$host$request_uri;
}

# Main HTTPS Server
server {
    listen 443 ssl http2;
    server_name meeting.company.com;

    # Let's Encrypt certificates (generated by certbot)
    ssl_certificate /etc/letsencrypt/live/meeting.company.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/meeting.company.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    # Additional security
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;

    # WebSocket proxy to Node.js
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Timeouts for long-lived WebSocket
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;

        # Buffers
        proxy_buffering off;
        proxy_buffer_size 4k;
    }

    # Health check endpoint (optional)
    location /health {
        return 200 'OK';
        add_header Content-Type text/plain;
    }
}
```

Apply:

```bash
sudo nginx -t
sudo systemctl restart nginx
```

### 5.4 Verify Automatic Renewal

```bash
# Test renewal (dry-run)
sudo certbot renew --dry-run
```

Automatic renewal is configured via a systemd timer or cron.

---

## Part 6: Configure coturn (TURN/STUN)

### 6.1 Install coturn

```bash
sudo apt install -y coturn
```

### 6.2 Enable coturn as a Service

```bash
sudo nano /etc/default/coturn
```

Uncomment (remove the #):

```
TURNSERVER_ENABLED=1
```

### 6.3 Generate Certificate for coturn

coturn needs its own certificate for TURNS (TLS).

```bash
# Copy Let's Encrypt certificates for coturn
sudo mkdir -p /etc/coturn/certs

# Script to copy certificates (needed because Let's Encrypt renews them)
sudo nano /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

Paste:

```bash
#!/bin/bash
DOMAIN="meeting.company.com"
cp /etc/letsencrypt/live/$DOMAIN/fullchain.pem /etc/coturn/certs/
cp /etc/letsencrypt/live/$DOMAIN/privkey.pem /etc/coturn/certs/
chown turnserver:turnserver /etc/coturn/certs/*.pem
chmod 600 /etc/coturn/certs/*.pem
systemctl restart coturn
```

Make executable and run once:

```bash
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
sudo /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

### 6.4 Configure coturn

```bash
sudo nano /etc/turnserver.conf
```

Replace all content with (adjust values):

```ini
# ===========================================
# coturn Configuration for VR Meeting
# ===========================================

# Server name
realm=meeting.company.com
server-name=meeting.company.com

# Listening ports
listening-port=3478
tls-listening-port=5349

# Listening IP
listening-ip=0.0.0.0
relay-ip=SERVER_PUBLIC_IP
external-ip=SERVER_PUBLIC_IP

# UDP port range for media relay
min-port=49152
max-port=65535

# SSL certificates
cert=/etc/coturn/certs/fullchain.pem
pkey=/etc/coturn/certs/privkey.pem

# Authentication
lt-cred-mech
user=vrmeeting:SecureTURNPassword_2024!

# Security
fingerprint
no-cli
no-tlsv1
no-tlsv1_1

# Limits (anti-abuse)
total-quota=100
stale-nonce=600
max-bps=1000000

# Logs
log-file=/var/log/turnserver/turnserver.log
simple-log
no-stdout-log

# Misc
proc-user=turnserver
proc-group=turnserver
```

**IMPORTANT: Replace:**
- `meeting.company.com` with your domain
- `SERVER_PUBLIC_IP` with the actual public IP
- `SecureTURNPassword_2024!` with a strong password

### 6.5 Create Log Directory

```bash
sudo mkdir -p /var/log/turnserver
sudo chown turnserver:turnserver /var/log/turnserver
```

### 6.6 Start coturn

```bash
sudo systemctl restart coturn
sudo systemctl enable coturn
sudo systemctl status coturn
```

### 6.7 Verify coturn is Listening

```bash
ss -tlnp | grep turnserver
ss -ulnp | grep turnserver
```

Expected:
```
tcp   LISTEN  0  128  0.0.0.0:3478   turnserver
tcp   LISTEN  0  128  0.0.0.0:5349   turnserver
udp   UNCONN  0  0    0.0.0.0:3478   turnserver
```

---

## Part 7: Process Management with PM2

### 7.1 Why PM2?

PM2 is a process manager for Node.js in production.

| Feature | Description |
|---------|-------------|
| **Auto-restart** | Automatically restarts on crash |
| **Cluster mode** | Uses all CPU cores |
| **Zero-downtime reload** | Update without disconnecting users |
| **Built-in monitoring** | Real-time CPU, RAM, logs dashboard |
| **Log management** | Centralized logs with rotation |
| **Auto-start** | Starts automatically on server boot |

### 7.2 Install PM2

```bash
# Install PM2 globally
sudo npm install -g pm2

# Verify installation
pm2 --version
```

### 7.3 Create PM2 Configuration File

```bash
cd ~/vr-meeting/Server
nano ecosystem.config.js
```

Paste:

```javascript
module.exports = {
  apps: [{
    // Application name (shown in pm2 list)
    name: 'vr-meeting',

    // Entry point
    script: 'server.js',

    // Working directory
    cwd: '/home/vr-admin/vr-meeting/Server',

    // Number of instances (1 = single, 'max' = all CPUs)
    instances: 1,

    // Restart if memory exceeds 500MB
    max_memory_restart: '500M',

    // Environment variables
    env: {
      NODE_ENV: 'production',
      PORT: 8080
    },

    // Auto-restart on crash
    autorestart: true,

    // Watch for file changes (disable in prod)
    watch: false,

    // Delay before restart after crash (ms)
    restart_delay: 5000,

    // Maximum restarts before stopping
    max_restarts: 10,

    // Log configuration
    log_file: '/home/vr-admin/vr-meeting/logs/combined.log',
    error_file: '/home/vr-admin/vr-meeting/logs/error.log',
    out_file: '/home/vr-admin/vr-meeting/logs/out.log',
    log_date_format: 'YYYY-MM-DD HH:mm:ss Z',

    // Merge logs from all instances
    merge_logs: true
  }]
};
```

### 7.4 Create Required Directories

```bash
mkdir -p ~/vr-meeting/logs
mkdir -p ~/vr-meeting/Server/uploads
mkdir -p ~/vr-meeting/Server/temp
```

### 7.5 Start Application with PM2

```bash
cd ~/vr-meeting/Server

# Start with config file
pm2 start ecosystem.config.js

# Check status
pm2 status
```

Expected result:

```
┌─────┬──────────────┬─────────┬─────────┬──────────┬────────┬──────────┐
│ id  │ name         │ mode    │ pid     │ uptime   │ status │ cpu │ mem│
├─────┼──────────────┼─────────┼─────────┼──────────┼────────┼──────────┤
│ 0   │ vr-meeting   │ fork    │ 12345   │ 0s       │ online │ 0%  │45MB│
└─────┴──────────────┴─────────┴─────────┴──────────┴────────┴──────────┘
```

### 7.6 Configure Auto-Start on Boot

**Critical step** - without this, the server won't restart after a reboot.

```bash
# Generate startup script
pm2 startup
```

PM2 will display a command like:

```
[PM2] To setup the Startup Script, copy/paste the following command:
sudo env PATH=$PATH:/usr/bin pm2 startup systemd -u vr-admin --hp /home/vr-admin
```

**Copy and execute this exact command** (it will be different on your system).

Then save the process list:

```bash
pm2 save
```

### 7.7 Verify Auto-Start Works

```bash
# Reboot the server
sudo reboot

# After reboot, verify
pm2 status
```

The vr-meeting process should be running.

### 7.8 Useful PM2 Commands

```bash
# === Status & Monitoring ===
pm2 status                    # List all processes
pm2 monit                     # Real-time monitoring dashboard
pm2 info vr-meeting           # Detailed app info

# === Logs ===
pm2 logs                      # View all logs (live)
pm2 logs vr-meeting           # Specific app logs
pm2 logs --lines 100          # Last 100 lines
pm2 flush                     # Clear all logs

# === Control ===
pm2 stop vr-meeting           # Stop
pm2 start vr-meeting          # Start
pm2 restart vr-meeting        # Restart (brief interruption)
pm2 reload vr-meeting         # Reload without interruption (graceful)

# === Updates ===
pm2 reload ecosystem.config.js    # Reload with modified config

# === Cleanup ===
pm2 delete vr-meeting         # Remove from PM2 list
pm2 kill                      # Stop PM2 completely
```

### 7.9 PM2 Log Rotation

Install automatic log rotation:

```bash
pm2 install pm2-logrotate

# Configure rotation
pm2 set pm2-logrotate:max_size 10M      # Rotate when file reaches 10MB
pm2 set pm2-logrotate:retain 7          # Keep 7 files
pm2 set pm2-logrotate:compress true     # Compress old logs
```

---

## Part 8: Additional Security

### 8.1 Configure fail2ban

fail2ban protects against SSH brute-force attacks.

```bash
sudo nano /etc/fail2ban/jail.local
```

Paste:

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

Restart:

```bash
sudo systemctl restart fail2ban
sudo systemctl enable fail2ban
```

### 8.2 Disable Root Password Authentication

```bash
sudo nano /etc/ssh/sshd_config
```

Verify/modify:

```
PermitRootLogin prohibit-password
PasswordAuthentication no
```

```bash
sudo systemctl restart sshd
```

### 8.3 Automatic Security Updates

```bash
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
# Choose "Yes"
```

---

## Part 9: Configure Unity

### 9.1 VRNetworkManager

In the `Bootstrap.unity` scene, select the GameObject with `VRNetworkManager`:

| Field | Value |
|-------|-------|
| Server Url | `wss://meeting.company.com` |

### 9.2 VoiceChatManager

| Field | Value |
|-------|-------|
| Use Custom Turn Server | `true` |
| Custom Turn Url | `turn:meeting.company.com:3478` |
| Custom Turns Url | `turns:meeting.company.com:5349` |
| Custom Turn Username | `vrmeeting` |
| Custom Turn Credential | `SecureTURNPassword_2024!` |
| Enable Turn Tcp | `true` |

> Use the same credentials as in `/etc/turnserver.conf`

### 9.3 Build and Distribution

1. **File > Build Settings**
2. Platform: **Windows** or **Android** (Quest)
3. **Build**
4. Distribute the build to users

**For Quest:**
- Upload the APK to Meta Quest Developer Hub
- Or use MDM (Mobile Device Management) for enterprise deployment

---

## Part 10: Verification and Testing

### 10.1 Verify All Services

```bash
# PM2 (Node.js)
pm2 status

# nginx and coturn
sudo systemctl status nginx coturn

# Open ports
sudo ss -tlnp | grep -E '(nginx|node|turn)'
```

### 10.2 Test from Outside

From a client PC (not on the same network):

```powershell
# DNS
nslookup meeting.company.com

# HTTPS
Test-NetConnection -ComputerName meeting.company.com -Port 443

# TURN
Test-NetConnection -ComputerName meeting.company.com -Port 3478
```

### 10.3 Test SSL Certificate

```bash
# From the server
curl -I https://meeting.company.com
```

Or visit https://www.ssllabs.com/ssltest/ and enter your domain.

### 10.4 Test TURN with Trickle ICE

1. Go to https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/
2. Add a TURN server:
   - URL: `turn:meeting.company.com:3478`
   - Username: `vrmeeting`
   - Credential: `SecureTURNPassword_2024!`
3. Click "Gather candidates"
4. Verify that `relay` candidates appear

### 10.5 Real-Time Logs

Open 3 SSH terminals:

```bash
# Terminal 1: Node.js (PM2)
pm2 logs vr-meeting

# Terminal 2: nginx
sudo tail -f /var/log/nginx/access.log /var/log/nginx/error.log

# Terminal 3: coturn
sudo tail -f /var/log/turnserver/turnserver.log
```

Or use the built-in PM2 dashboard:

```bash
pm2 monit
```

---

## Part 11: Monitoring and Maintenance

### 11.1 Daily Check Script

```bash
sudo nano /usr/local/bin/vr-meeting-check.sh
```

Paste:

```bash
#!/bin/bash

echo "=== VR Meeting Server Status ==="
echo "Date: $(date)"
echo ""

echo "--- Services ---"
systemctl is-active --quiet nginx && echo "nginx: OK" || echo "nginx: FAILED"
systemctl is-active --quiet coturn && echo "coturn: OK" || echo "coturn: FAILED"

# Check PM2
PM2_STATUS=$(su - vr-admin -c "pm2 jlist" 2>/dev/null | grep -o '"status":"online"' | wc -l)
if [ "$PM2_STATUS" -gt 0 ]; then
    echo "vr-meeting (PM2): OK"
else
    echo "vr-meeting (PM2): FAILED"
fi
echo ""

echo "--- SSL Certificate ---"
CERT_EXPIRY=$(sudo openssl x509 -enddate -noout -in /etc/letsencrypt/live/meeting.company.com/fullchain.pem | cut -d= -f2)
echo "Expiration: $CERT_EXPIRY"
echo ""

echo "--- Disk Space ---"
df -h / | tail -1
echo ""

echo "--- Memory ---"
free -h | grep Mem
echo ""

echo "--- Active Connections ---"
ss -tn state established | grep -c ":443" | xargs echo "Port 443:"
ss -tn state established | grep -c ":8080" | xargs echo "Port 8080:"
```

```bash
sudo chmod +x /usr/local/bin/vr-meeting-check.sh
```

### 11.2 Log Rotation

```bash
sudo nano /etc/logrotate.d/vr-meeting
```

Paste:

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

### 11.3 Configuration Backup

```bash
# Create backup script
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

# Keep 7 days of backups
find $BACKUP_DIR -name "*.tar.gz" -mtime +7 -delete

echo "Backup completed: $BACKUP_DIR/config-$DATE.tar.gz"
```

```bash
sudo chmod +x /usr/local/bin/vr-meeting-backup.sh

# Add to cron (daily backup at 2am)
(crontab -l 2>/dev/null; echo "0 2 * * * /usr/local/bin/vr-meeting-backup.sh") | crontab -
```

---

## Part 12: Quick Reference Commands

### PM2 Management (Node.js)

```bash
# === Status ===
pm2 status                    # Process list
pm2 info vr-meeting           # Detailed info

# === Control ===
pm2 stop vr-meeting           # Stop
pm2 start vr-meeting          # Start
pm2 restart vr-meeting        # Restart
pm2 reload vr-meeting         # Reload without interruption

# === Logs ===
pm2 logs                      # All logs (live)
pm2 logs vr-meeting           # Specific logs
pm2 logs --lines 100          # Last 100 lines
pm2 flush                     # Clear logs

# === Monitoring ===
pm2 monit                     # Real-time dashboard

# === After code update ===
cd ~/vr-meeting/Server
git pull
npm install
pm2 reload vr-meeting
```

### nginx and coturn Management

```bash
# Start/Stop/Restart
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

### SSL Certificates

```bash
# Check expiration
sudo certbot certificates

# Renew manually
sudo certbot renew

# Test renewal
sudo certbot renew --dry-run
```

### Network Debug

```bash
# Open ports
sudo ss -tlnp

# Established connections
sudo ss -tn state established

# Real-time traffic
sudo tcpdump -i any port 443 or port 3478
```


---

## Appendix A: Complete File Configurations

### /etc/nginx/sites-available/vr-meeting

```nginx
server {
    listen 80;
    server_name meeting.company.com;
    return 301 https://$host$request_uri;
}

server {
    listen 443 ssl http2;
    server_name meeting.company.com;

    ssl_certificate /etc/letsencrypt/live/meeting.company.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/meeting.company.com/privkey.pem;
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
realm=meeting.company.com
server-name=meeting.company.com
listening-port=3478
tls-listening-port=5349
listening-ip=0.0.0.0
relay-ip=PUBLIC_IP
external-ip=PUBLIC_IP
min-port=49152
max-port=65535
cert=/etc/coturn/certs/fullchain.pem
pkey=/etc/coturn/certs/privkey.pem
lt-cred-mech
user=vrmeeting:SecureTURNPassword_2024!
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
