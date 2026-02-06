# VR Meeting Room Server - Deployment Guide

This guide covers deploying the WebSocket server for VR Meeting Rooms to a production environment.

---

## Table of Contents

1. [Prerequisites](#prerequisites)
2. [Server Setup](#server-setup)
3. [Installation](#installation)
4. [Configuration](#configuration)
5. [Process Management with PM2](#process-management-with-pm2)
6. [Firewall Configuration](#firewall-configuration)
7. [SSL/TLS with Nginx Reverse Proxy](#ssltls-with-nginx-reverse-proxy)
8. [Unity Client Configuration](#unity-client-configuration)
9. [Monitoring and Maintenance](#monitoring-and-maintenance)
10. [Troubleshooting](#troubleshooting)

---

## Prerequisites

### Server Requirements

| Component | Minimum | Recommended |
|-----------|---------|-------------|
| OS | Ubuntu 20.04 / Debian 11 / CentOS 8 | Ubuntu 22.04 LTS |
| CPU | 1 core | 2+ cores |
| RAM | 1 GB | 2+ GB |
| Node.js | 16.x | 18.x LTS |
| Network | Open port 8080 | Ports 80, 443 (with Nginx) |

### Software Dependencies

- Node.js 18.x or higher
- npm 9.x or higher
- Git (for deployment via repository)
- PM2 (process manager)
- Nginx (optional, for SSL/reverse proxy)

---

## Server Setup

### Step 1: Install Node.js

**Ubuntu/Debian:**

```bash
# Update system packages
sudo apt update && sudo apt upgrade -y

# Install Node.js 18.x
curl -fsSL https://deb.nodesource.com/setup_18.x | sudo -E bash -
sudo apt-get install -y nodejs

# Verify installation
node --version   # Should output v18.x.x
npm --version    # Should output 9.x.x or higher
```

**CentOS/RHEL:**

```bash
# Install Node.js 18.x
curl -fsSL https://rpm.nodesource.com/setup_18.x | sudo bash -
sudo yum install -y nodejs

# Verify installation
node --version
npm --version
```

### Step 2: Create Application Directory

```bash
# Create directory for the application
sudo mkdir -p /opt/vr-meeting
sudo chown $USER:$USER /opt/vr-meeting
```

---

## Installation

### Option A: Deploy via Git (Recommended)

```bash
# Clone the repository
cd /opt/vr-meeting
git clone <your-repository-url> .

# Navigate to server directory
cd Server

# Install production dependencies
npm install --production
```

### Option B: Deploy via File Transfer

From your local machine:

```bash
# Copy server files via SCP
scp -r ./Server user@your-server-ip:/opt/vr-meeting/Server

# Or via rsync (preserves permissions)
rsync -avz ./Server/ user@your-server-ip:/opt/vr-meeting/Server/
```

On the server:

```bash
cd /opt/vr-meeting/Server
npm install --production
```

---

## Configuration

### Environment Variables

Create a `.env` file for environment-specific configuration:

```bash
nano /opt/vr-meeting/Server/.env
```

**Contents:**

```env
# Server Configuration
PORT=8080
NODE_ENV=production

# WebSocket Settings
WS_HEARTBEAT_INTERVAL=30000

# Future Database Configuration (Phase 3)
# DB_HOST=localhost
# DB_PORT=3306
# DB_USER=vrmeeting
# DB_PASSWORD=your_secure_password
# DB_NAME=vrmeeting

# Future Authentication (Phase 3)
# JWT_SECRET=your_jwt_secret_key
# JWT_EXPIRY=24h
```

### Security Considerations

- Never commit `.env` files to version control
- Use strong passwords for database credentials
- Rotate JWT secrets periodically
- Restrict database access to localhost only

---

## Process Management with PM2

PM2 is a production process manager that keeps your application running 24/7.

### Install PM2

```bash
sudo npm install -g pm2
```

### Start the Application

```bash
cd /opt/vr-meeting/Server

# Start with PM2
pm2 start npm --name "vr-meeting" -- run start

# Or start directly with node
pm2 start src/index.js --name "vr-meeting"
```

### Configure Auto-Restart on Boot

```bash
# Generate startup script
pm2 startup

# Follow the instructions provided, then save the process list
pm2 save
```

### PM2 Commands Reference

| Command | Description |
|---------|-------------|
| `pm2 status` | Show status of all processes |
| `pm2 logs vr-meeting` | View application logs |
| `pm2 logs vr-meeting --lines 100` | View last 100 log lines |
| `pm2 restart vr-meeting` | Restart the application |
| `pm2 stop vr-meeting` | Stop the application |
| `pm2 delete vr-meeting` | Remove from PM2 |
| `pm2 monit` | Real-time monitoring dashboard |
| `pm2 reload vr-meeting` | Zero-downtime reload |

### PM2 Ecosystem File (Optional)

Create `ecosystem.config.js` for advanced configuration:

```javascript
module.exports = {
  apps: [{
    name: 'vr-meeting',
    script: 'src/index.js',
    cwd: '/opt/vr-meeting/Server',
    instances: 1,
    autorestart: true,
    watch: false,
    max_memory_restart: '500M',
    env: {
      NODE_ENV: 'production',
      PORT: 8080
    },
    error_file: '/var/log/vr-meeting/error.log',
    out_file: '/var/log/vr-meeting/output.log',
    log_date_format: 'YYYY-MM-DD HH:mm:ss Z'
  }]
};
```

Start with ecosystem file:

```bash
pm2 start ecosystem.config.js
```

---

## Firewall Configuration

### UFW (Ubuntu/Debian)

```bash
# Allow WebSocket port
sudo ufw allow 8080/tcp

# If using Nginx with SSL
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp

# Verify rules
sudo ufw status
```

### Firewalld (CentOS/RHEL)

```bash
# Allow WebSocket port
sudo firewall-cmd --permanent --add-port=8080/tcp

# If using Nginx with SSL
sudo firewall-cmd --permanent --add-service=http
sudo firewall-cmd --permanent --add-service=https

# Apply changes
sudo firewall-cmd --reload

# Verify rules
sudo firewall-cmd --list-all
```

---

## SSL/TLS with Nginx Reverse Proxy

Using Nginx as a reverse proxy provides SSL termination, load balancing, and additional security.

### Install Nginx and Certbot

```bash
# Ubuntu/Debian
sudo apt install nginx certbot python3-certbot-nginx -y

# CentOS/RHEL
sudo yum install nginx certbot python3-certbot-nginx -y
```

### Configure Nginx

Create a new site configuration:

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

**Configuration:**

```nginx
# Upstream WebSocket server
upstream vr_meeting_backend {
    server 127.0.0.1:8080;
    keepalive 64;
}

# HTTP to HTTPS redirect
server {
    listen 80;
    listen [::]:80;
    server_name vr.yourcompany.com;

    location / {
        return 301 https://$server_name$request_uri;
    }
}

# HTTPS server
server {
    listen 443 ssl http2;
    listen [::]:443 ssl http2;
    server_name vr.yourcompany.com;

    # SSL certificates (managed by Certbot)
    ssl_certificate /etc/letsencrypt/live/vr.yourcompany.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/vr.yourcompany.com/privkey.pem;

    # SSL configuration
    ssl_protocols TLSv1.2 TLSv1.3;
    ssl_ciphers ECDHE-ECDSA-AES128-GCM-SHA256:ECDHE-RSA-AES128-GCM-SHA256;
    ssl_prefer_server_ciphers off;
    ssl_session_cache shared:SSL:10m;
    ssl_session_timeout 1d;

    # Security headers
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;

    # WebSocket proxy
    location / {
        proxy_pass http://vr_meeting_backend;
        proxy_http_version 1.1;

        # WebSocket headers
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";

        # Forwarding headers
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Timeouts for WebSocket
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;
        proxy_connect_timeout 60s;

        # Buffering
        proxy_buffering off;
        proxy_cache off;
    }

    # Health check endpoint
    location /health {
        proxy_pass http://vr_meeting_backend;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
    }
}
```

### Enable the Site

```bash
# Create symbolic link
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/

# Remove default site (optional)
sudo rm /etc/nginx/sites-enabled/default

# Test configuration
sudo nginx -t

# Reload Nginx
sudo systemctl reload nginx
```

### Obtain SSL Certificate

```bash
# Get certificate from Let's Encrypt
sudo certbot --nginx -d vr.yourcompany.com

# Test auto-renewal
sudo certbot renew --dry-run
```

---

## Unity Client Configuration

After deploying the server, update the Unity client to connect to the production server.

### Update VRNetworkManager.cs

Locate the server URL configuration and update it:

```csharp
// Development (local)
// private string serverUrl = "ws://localhost:8080";

// Production WITHOUT SSL (direct connection)
// private string serverUrl = "ws://vr.yourcompany.com:8080";

// Production WITH SSL (via Nginx - Recommended)
private string serverUrl = "wss://vr.yourcompany.com";
```

### Build Settings

When building for production:

1. Set the correct server URL
2. Disable debug logging if not needed
3. Build with IL2CPP for better performance (optional)

---

## Monitoring and Maintenance

### View Real-Time Logs

```bash
# PM2 logs
pm2 logs vr-meeting

# Follow logs in real-time
pm2 logs vr-meeting --follow

# Nginx access logs
sudo tail -f /var/log/nginx/access.log

# Nginx error logs
sudo tail -f /var/log/nginx/error.log
```

### Monitor System Resources

```bash
# PM2 monitoring
pm2 monit

# System resources
htop

# Disk usage
df -h

# Memory usage
free -m
```

### Update the Application

```bash
cd /opt/vr-meeting

# Pull latest changes
git pull origin main

# Install new dependencies
cd Server
npm install --production

# Restart with zero downtime
pm2 reload vr-meeting
```

### Backup Procedures

```bash
# Backup configuration
cp /opt/vr-meeting/Server/.env /backup/vr-meeting-env-$(date +%Y%m%d).bak

# Backup logs
cp -r /var/log/vr-meeting /backup/vr-meeting-logs-$(date +%Y%m%d)/
```

### Log Rotation

Create `/etc/logrotate.d/vr-meeting`:

```
/var/log/vr-meeting/*.log {
    daily
    missingok
    rotate 14
    compress
    delaycompress
    notifempty
    create 0640 www-data www-data
    sharedscripts
    postrotate
        pm2 reloadLogs
    endscript
}
```

---

## Troubleshooting

### Common Issues

#### Application Won't Start

```bash
# Check PM2 status
pm2 status

# View error logs
pm2 logs vr-meeting --err --lines 50

# Check Node.js version
node --version

# Verify dependencies
cd /opt/vr-meeting/Server
npm install
```

#### WebSocket Connection Refused

```bash
# Check if server is running
pm2 status

# Check if port is listening
sudo netstat -tlnp | grep 8080
# or
sudo ss -tlnp | grep 8080

# Check firewall
sudo ufw status
```

#### SSL Certificate Issues

```bash
# Check certificate status
sudo certbot certificates

# Renew certificate manually
sudo certbot renew

# Check Nginx configuration
sudo nginx -t
```

#### High Memory Usage

```bash
# Check memory usage
pm2 monit

# Restart application
pm2 restart vr-meeting

# Set memory limit in ecosystem.config.js
max_memory_restart: '500M'
```

### Diagnostic Commands

```bash
# Check server connectivity
curl -v http://localhost:8080

# Test WebSocket connection
wscat -c ws://localhost:8080

# Check DNS resolution
nslookup vr.yourcompany.com

# Check SSL certificate
openssl s_client -connect vr.yourcompany.com:443
```

---

## Quick Reference

### Essential Commands

| Task | Command |
|------|---------|
| Start server | `pm2 start vr-meeting` |
| Stop server | `pm2 stop vr-meeting` |
| Restart server | `pm2 restart vr-meeting` |
| View logs | `pm2 logs vr-meeting` |
| Check status | `pm2 status` |
| Update & restart | `git pull && npm install && pm2 reload vr-meeting` |

### Important File Locations

| File | Path |
|------|------|
| Application | `/opt/vr-meeting/Server/` |
| Environment config | `/opt/vr-meeting/Server/.env` |
| Nginx config | `/etc/nginx/sites-available/vr-meeting` |
| SSL certificates | `/etc/letsencrypt/live/vr.yourcompany.com/` |
| PM2 logs | `~/.pm2/logs/` |

### Port Reference

| Service | Port | Protocol |
|---------|------|----------|
| WebSocket (dev) | 8080 | WS |
| HTTP | 80 | TCP |
| HTTPS/WSS | 443 | TCP |

---

## Support

For issues or questions:

1. Check the [Troubleshooting](#troubleshooting) section
2. Review application logs: `pm2 logs vr-meeting`
3. Contact the development team

---

*Last updated: February 2025*
