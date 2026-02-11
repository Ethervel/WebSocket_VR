# 공개 배포 가이드 - VR 회의실

> **버전:** Ubuntu 24.04 LTS + Node.js 22 LTS
> **최종 업데이트:** 2026년 2월
> **목표:** 모든 네트워크(인터넷)에서 서버에 접근 가능하게 만들기

---


### 서버

| 요소 | 사양 |
|------|------|
| 유형 | VPS, 클라우드 VM 또는 전용 서버 |
| OS | Ubuntu 24.04 LTS (권장) |
| RAM | 최소 4GB, 권장 8GB |
| CPU | 최소 2 vCPU |
| 스토리지 | 25GB SSD |
| 대역폭 | 최소 100Mbps |
| IP | 고정 공인 IPv4 |


### 도메인 및 DNS

- 도메인 이름 (예: `company.com`)
- A 레코드 생성을 위한 DNS 접근 권한
- 전용 서브도메인 (예: `meeting.company.com`)

---

## 파트 1: 서버 준비

### 1.1 초기 연결

```bash
# 로컬 PC에서
ssh root@서버_IP
```

### 1.2 시스템 업데이트

```bash
apt update && apt upgrade -y
```

### 1.3 비루트 사용자 생성

```bash
# 사용자 생성
adduser vr-admin
usermod -aG sudo vr-admin

# SSH 키 복사 (키 기반 인증 사용 시)
mkdir -p /home/vr-admin/.ssh
cp ~/.ssh/authorized_keys /home/vr-admin/.ssh/
chown -R vr-admin:vr-admin /home/vr-admin/.ssh
chmod 700 /home/vr-admin/.ssh
chmod 600 /home/vr-admin/.ssh/authorized_keys

# vr-admin으로 재연결
exit
```

```bash
ssh vr-admin@서버_IP
```

### 1.4 호스트명 설정

```bash
sudo hostnamectl set-hostname vr-meeting-server
```

---

## 파트 2: DNS 설정

Let's Encrypt에서 필요하므로 다른 것을 설치하기 전에 DNS를 먼저 설정하세요.

### 2.1 DNS 레코드 생성

도메인 등록기관/DNS 제어판에서 다음을 추가하세요:

| 유형 | 이름 | 값 | TTL |
|------|------|-----|-----|
| A | meeting | 서버_IP | 300 |
| A | turn | 서버_IP | 300 |

`company.com` 예시:
- `meeting.company.com` → `203.0.113.50`
- `turn.company.com` → `203.0.113.50`

### 2.2 DNS 전파 확인

```bash
# 몇 분 기다린 후 확인
dig meeting.company.com +short
dig turn.company.com +short
# 서버 IP가 표시되어야 함
```

또는 https://dnschecker.org 사용

---

## 파트 3: 구성 요소 설치

### 3.1 Node.js 22 LTS 설치

```bash
curl -fsSL https://deb.nodesource.com/setup_22.x | sudo bash -
sudo apt install -y nodejs
node --version    # v22.x.x
npm --version
```

### 3.2 필수 도구 설치

```bash
sudo apt install -y git nginx certbot python3-certbot-nginx poppler-utils ufw fail2ban
```

### 3.3 서버 프로젝트 복사

**옵션 A: Git으로**

```bash
cd ~
git clone https://your-repo.git vr-meeting
cd vr-meeting/Server
npm install
```

**옵션 B: 로컬 PC에서 SCP로**

```bash
# Windows PC에서 (PowerShell)
scp -r "D:\Test_project\WebSocket_VR\Server" vr-admin@서버_IP:~/vr-meeting/
```

그 다음 서버에서:

```bash
cd ~/vr-meeting/Server
npm install
```

### 3.4 시작 테스트

```bash
cd ~/vr-meeting/Server
npm start
```

서버가 정상적으로 시작되는지 확인한 후 `Ctrl+C`로 중지합니다.

---

## 파트 4: 방화벽 설정 (UFW)

### 4.1 규칙 설정

```bash
# SSH (중요: 자신을 잠그지 마세요!)
sudo ufw allow 22/tcp

# HTTP (Let's Encrypt용)
sudo ufw allow 80/tcp

# HTTPS (nginx + WebSocket)
sudo ufw allow 443/tcp

# STUN/TURN
sudo ufw allow 3478/tcp
sudo ufw allow 3478/udp
sudo ufw allow 5349/tcp

# TURN 릴레이 포트 (WebRTC 미디어)
sudo ufw allow 49152:65535/udp

# 방화벽 활성화
sudo ufw enable

# 확인
sudo ufw status verbose
```

### 4.2 예상 결과

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

## 파트 5: Let's Encrypt로 nginx 설정

### 5.1 초기 nginx 설정 생성 (HTTP)

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

붙여넣기 (`meeting.company.com`을 본인 도메인으로 변경):

```nginx
server {
    listen 80;
    server_name meeting.company.com;

    # Let's Encrypt용 임시 설정
    location / {
        return 200 'VR Meeting Server - HTTP OK';
        add_header Content-Type text/plain;
    }
}
```

사이트 활성화:

```bash
sudo ln -s /etc/nginx/sites-available/vr-meeting /etc/nginx/sites-enabled/
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl restart nginx
```

### 5.2 Let's Encrypt 인증서 발급

```bash
sudo certbot --nginx -d meeting.company.com
```

질문에 답변:
- 이메일: 본인 이메일 (만료 알림용)
- 이용약관: Yes
- 이메일 공유: No (또는 선호에 따라 Yes)
- HTTP→HTTPS 리다이렉트: 2 (Redirect)

Certbot이 자동으로 nginx 설정을 수정합니다.

### 5.3 WebSocket용 nginx 설정 업데이트

```bash
sudo nano /etc/nginx/sites-available/vr-meeting
```

모든 내용을 다음으로 교체:

```nginx
# HTTP → HTTPS 리다이렉트
server {
    listen 80;
    server_name meeting.company.com;
    return 301 https://$host$request_uri;
}

# 메인 HTTPS 서버
server {
    listen 443 ssl http2;
    server_name meeting.company.com;

    # Let's Encrypt 인증서 (certbot이 생성)
    ssl_certificate /etc/letsencrypt/live/meeting.company.com/fullchain.pem;
    ssl_certificate_key /etc/letsencrypt/live/meeting.company.com/privkey.pem;
    include /etc/letsencrypt/options-ssl-nginx.conf;
    ssl_dhparam /etc/letsencrypt/ssl-dhparams.pem;

    # 추가 보안
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header X-XSS-Protection "1; mode=block" always;

    # Node.js로 WebSocket 프록시
    location / {
        proxy_pass http://127.0.0.1:8080;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # 장기 WebSocket용 타임아웃
        proxy_read_timeout 86400s;
        proxy_send_timeout 86400s;

        # 버퍼
        proxy_buffering off;
        proxy_buffer_size 4k;
    }

    # 헬스 체크 엔드포인트 (선택사항)
    location /health {
        return 200 'OK';
        add_header Content-Type text/plain;
    }
}
```

적용:

```bash
sudo nginx -t
sudo systemctl restart nginx
```

### 5.4 자동 갱신 확인

```bash
# 갱신 테스트 (dry-run)
sudo certbot renew --dry-run
```

자동 갱신은 systemd 타이머 또는 cron을 통해 설정됩니다.

---

## 파트 6: coturn 설정 (TURN/STUN)

### 6.1 coturn 설치

```bash
sudo apt install -y coturn
```

### 6.2 coturn을 서비스로 활성화

```bash
sudo nano /etc/default/coturn
```

주석 해제 (#을 제거):

```
TURNSERVER_ENABLED=1
```

### 6.3 coturn용 인증서 생성

coturn은 TURNS(TLS)를 위해 자체 인증서가 필요합니다.

```bash
# coturn용 Let's Encrypt 인증서 복사
sudo mkdir -p /etc/coturn/certs

# 인증서 복사 스크립트 (Let's Encrypt가 갱신하므로 필요)
sudo nano /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

붙여넣기:

```bash
#!/bin/bash
DOMAIN="meeting.company.com"
cp /etc/letsencrypt/live/$DOMAIN/fullchain.pem /etc/coturn/certs/
cp /etc/letsencrypt/live/$DOMAIN/privkey.pem /etc/coturn/certs/
chown turnserver:turnserver /etc/coturn/certs/*.pem
chmod 600 /etc/coturn/certs/*.pem
systemctl restart coturn
```

실행 권한 부여 및 한 번 실행:

```bash
sudo chmod +x /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
sudo /etc/letsencrypt/renewal-hooks/deploy/coturn.sh
```

### 6.4 coturn 설정

```bash
sudo nano /etc/turnserver.conf
```

모든 내용을 다음으로 교체 (값 조정):

```ini
# ===========================================
# VR Meeting용 coturn 설정
# ===========================================

# 서버 이름
realm=meeting.company.com
server-name=meeting.company.com

# 리스닝 포트
listening-port=3478
tls-listening-port=5349

# 리스닝 IP
listening-ip=0.0.0.0
relay-ip=서버_공인_IP
external-ip=서버_공인_IP

# 미디어 릴레이용 UDP 포트 범위
min-port=49152
max-port=65535

# SSL 인증서
cert=/etc/coturn/certs/fullchain.pem
pkey=/etc/coturn/certs/privkey.pem

# 인증
lt-cred-mech
user=vrmeeting:보안TURN비밀번호_2024!

# 보안
fingerprint
no-cli
no-tlsv1
no-tlsv1_1

# 제한 (악용 방지)
total-quota=100
stale-nonce=600
max-bps=1000000

# 로그
log-file=/var/log/turnserver/turnserver.log
simple-log
no-stdout-log

# 기타
proc-user=turnserver
proc-group=turnserver
```

**중요: 다음을 교체하세요:**
- `meeting.company.com`을 본인 도메인으로
- `서버_공인_IP`를 실제 공인 IP로
- `보안TURN비밀번호_2024!`를 강력한 비밀번호로

### 6.5 로그 디렉토리 생성

```bash
sudo mkdir -p /var/log/turnserver
sudo chown turnserver:turnserver /var/log/turnserver
```

### 6.6 coturn 시작

```bash
sudo systemctl restart coturn
sudo systemctl enable coturn
sudo systemctl status coturn
```

### 6.7 coturn 리스닝 확인

```bash
ss -tlnp | grep turnserver
ss -ulnp | grep turnserver
```

예상 결과:
```
tcp   LISTEN  0  128  0.0.0.0:3478   turnserver
tcp   LISTEN  0  128  0.0.0.0:5349   turnserver
udp   UNCONN  0  0    0.0.0.0:3478   turnserver
```

---

## 파트 7: PM2를 이용한 프로세스 관리

### 7.1 PM2를 사용하는 이유?

PM2는 프로덕션 환경의 Node.js 프로세스 관리자입니다.

| 기능 | 설명 |
|------|------|
| **자동 재시작** | 크래시 시 자동으로 재시작 |
| **클러스터 모드** | 모든 CPU 코어 활용 |
| **무중단 리로드** | 사용자 연결 끊김 없이 업데이트 |
| **내장 모니터링** | 실시간 CPU, RAM, 로그 대시보드 |
| **로그 관리** | 로테이션을 포함한 중앙집중식 로그 |
| **자동 시작** | 서버 부팅 시 자동 시작 |

### 7.2 PM2 설치

```bash
# PM2 전역 설치
sudo npm install -g pm2

# 설치 확인
pm2 --version
```

### 7.3 PM2 설정 파일 생성

```bash
cd ~/vr-meeting/Server
nano ecosystem.config.js
```

붙여넣기:

```javascript
module.exports = {
  apps: [{
    // 애플리케이션 이름 (pm2 list에 표시)
    name: 'vr-meeting',

    // 진입점
    script: 'server.js',

    // 작업 디렉토리
    cwd: '/home/vr-admin/vr-meeting/Server',

    // 인스턴스 수 (1 = 단일, 'max' = 모든 CPU)
    instances: 1,

    // 메모리가 500MB 초과 시 재시작
    max_memory_restart: '500M',

    // 환경 변수
    env: {
      NODE_ENV: 'production',
      PORT: 8080
    },

    // 크래시 시 자동 재시작
    autorestart: true,

    // 파일 변경 감시 (프로덕션에서는 비활성화)
    watch: false,

    // 크래시 후 재시작까지 지연 (ms)
    restart_delay: 5000,

    // 정지 전 최대 재시작 횟수
    max_restarts: 10,

    // 로그 설정
    log_file: '/home/vr-admin/vr-meeting/logs/combined.log',
    error_file: '/home/vr-admin/vr-meeting/logs/error.log',
    out_file: '/home/vr-admin/vr-meeting/logs/out.log',
    log_date_format: 'YYYY-MM-DD HH:mm:ss Z',

    // 모든 인스턴스의 로그 병합
    merge_logs: true
  }]
};
```

### 7.4 필요한 디렉토리 생성

```bash
mkdir -p ~/vr-meeting/logs
mkdir -p ~/vr-meeting/Server/uploads
mkdir -p ~/vr-meeting/Server/temp
```

### 7.5 PM2로 애플리케이션 시작

```bash
cd ~/vr-meeting/Server

# 설정 파일로 시작
pm2 start ecosystem.config.js

# 상태 확인
pm2 status
```

예상 결과:

```
┌─────┬──────────────┬─────────┬─────────┬──────────┬────────┬──────────┐
│ id  │ name         │ mode    │ pid     │ uptime   │ status │ cpu │ mem│
├─────┼──────────────┼─────────┼─────────┼──────────┼────────┼──────────┤
│ 0   │ vr-meeting   │ fork    │ 12345   │ 0s       │ online │ 0%  │45MB│
└─────┴──────────────┴─────────┴─────────┴──────────┴────────┴──────────┘
```

### 7.6 부팅 시 자동 시작 설정

**중요한 단계** - 이것 없이는 재부팅 후 서버가 다시 시작되지 않습니다.

```bash
# 시작 스크립트 생성
pm2 startup
```

PM2가 다음과 같은 명령어를 표시합니다:

```
[PM2] To setup the Startup Script, copy/paste the following command:
sudo env PATH=$PATH:/usr/bin pm2 startup systemd -u vr-admin --hp /home/vr-admin
```

**이 정확한 명령어를 복사하여 실행하세요** (시스템마다 다릅니다).

그 다음 프로세스 목록 저장:

```bash
pm2 save
```

### 7.7 자동 시작 작동 확인

```bash
# 서버 재부팅
sudo reboot

# 재부팅 후 확인
pm2 status
```

vr-meeting 프로세스가 실행 중이어야 합니다.

### 7.8 유용한 PM2 명령어

```bash
# === 상태 & 모니터링 ===
pm2 status                    # 모든 프로세스 목록
pm2 monit                     # 실시간 모니터링 대시보드
pm2 info vr-meeting           # 상세 앱 정보

# === 로그 ===
pm2 logs                      # 모든 로그 보기 (실시간)
pm2 logs vr-meeting           # 특정 앱 로그
pm2 logs --lines 100          # 마지막 100줄
pm2 flush                     # 모든 로그 삭제

# === 제어 ===
pm2 stop vr-meeting           # 중지
pm2 start vr-meeting          # 시작
pm2 restart vr-meeting        # 재시작 (잠시 중단)
pm2 reload vr-meeting         # 중단 없이 리로드 (graceful)

# === 업데이트 ===
pm2 reload ecosystem.config.js    # 수정된 설정으로 리로드

# === 정리 ===
pm2 delete vr-meeting         # PM2 목록에서 제거
pm2 kill                      # PM2 완전 중지
```

### 7.9 PM2 로그 로테이션

자동 로그 로테이션 설치:

```bash
pm2 install pm2-logrotate

# 로테이션 설정
pm2 set pm2-logrotate:max_size 10M      # 파일이 10MB에 도달하면 로테이션
pm2 set pm2-logrotate:retain 7          # 7개 파일 유지
pm2 set pm2-logrotate:compress true     # 이전 로그 압축
```

---

## 파트 8: 추가 보안

### 8.1 fail2ban 설정

fail2ban은 SSH 무차별 대입 공격으로부터 보호합니다.

```bash
sudo nano /etc/fail2ban/jail.local
```

붙여넣기:

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

재시작:

```bash
sudo systemctl restart fail2ban
sudo systemctl enable fail2ban
```

### 8.2 루트 비밀번호 인증 비활성화

```bash
sudo nano /etc/ssh/sshd_config
```

확인/수정:

```
PermitRootLogin prohibit-password
PasswordAuthentication no
```

```bash
sudo systemctl restart sshd
```

### 8.3 자동 보안 업데이트

```bash
sudo apt install -y unattended-upgrades
sudo dpkg-reconfigure -plow unattended-upgrades
# "Yes" 선택
```

---

## 파트 9: Unity 설정

### 9.1 VRNetworkManager

`Bootstrap.unity` 씬에서 `VRNetworkManager`가 있는 GameObject를 선택:

| 필드 | 값 |
|------|-----|
| Server Url | `wss://meeting.company.com` |

### 9.2 VoiceChatManager

| 필드 | 값 |
|------|-----|
| Use Custom Turn Server | `true` |
| Custom Turn Url | `turn:meeting.company.com:3478` |
| Custom Turns Url | `turns:meeting.company.com:5349` |
| Custom Turn Username | `vrmeeting` |
| Custom Turn Credential | `보안TURN비밀번호_2024!` |
| Enable Turn Tcp | `true` |

> `/etc/turnserver.conf`와 동일한 자격 증명을 사용하세요

### 9.3 빌드 및 배포

1. **File > Build Settings**
2. 플랫폼: **Windows** 또는 **Android** (Quest)
3. **Build**
4. 사용자에게 빌드 배포

**Quest의 경우:**
- Meta Quest Developer Hub에 APK 업로드
- 또는 기업 배포를 위해 MDM(Mobile Device Management) 사용

---

## 파트 10: 확인 및 테스트

### 10.1 모든 서비스 확인

```bash
# PM2 (Node.js)
pm2 status

# nginx 및 coturn
sudo systemctl status nginx coturn

# 열린 포트
sudo ss -tlnp | grep -E '(nginx|node|turn)'
```

### 10.2 외부에서 테스트

클라이언트 PC에서 (다른 네트워크에서):

```powershell
# DNS
nslookup meeting.company.com

# HTTPS
Test-NetConnection -ComputerName meeting.company.com -Port 443

# TURN
Test-NetConnection -ComputerName meeting.company.com -Port 3478
```

### 10.3 SSL 인증서 테스트

```bash
# 서버에서
curl -I https://meeting.company.com
```

또는 https://www.ssllabs.com/ssltest/ 방문 후 도메인 입력.

### 10.4 Trickle ICE로 TURN 테스트

1. https://webrtc.github.io/samples/src/content/peerconnection/trickle-ice/ 접속
2. TURN 서버 추가:
   - URL: `turn:meeting.company.com:3478`
   - Username: `vrmeeting`
   - Credential: `보안TURN비밀번호_2024!`
3. "Gather candidates" 클릭
4. `relay` 후보가 나타나는지 확인

### 10.5 실시간 로그

SSH 터미널 3개 열기:

```bash
# 터미널 1: Node.js (PM2)
pm2 logs vr-meeting

# 터미널 2: nginx
sudo tail -f /var/log/nginx/access.log /var/log/nginx/error.log

# 터미널 3: coturn
sudo tail -f /var/log/turnserver/turnserver.log
```

또는 내장 PM2 대시보드 사용:

```bash
pm2 monit
```

---

## 파트 11: 모니터링 및 유지보수

### 11.1 일일 점검 스크립트

```bash
sudo nano /usr/local/bin/vr-meeting-check.sh
```

붙여넣기:

```bash
#!/bin/bash

echo "=== VR Meeting 서버 상태 ==="
echo "날짜: $(date)"
echo ""

echo "--- 서비스 ---"
systemctl is-active --quiet nginx && echo "nginx: OK" || echo "nginx: 실패"
systemctl is-active --quiet coturn && echo "coturn: OK" || echo "coturn: 실패"

# PM2 확인
PM2_STATUS=$(su - vr-admin -c "pm2 jlist" 2>/dev/null | grep -o '"status":"online"' | wc -l)
if [ "$PM2_STATUS" -gt 0 ]; then
    echo "vr-meeting (PM2): OK"
else
    echo "vr-meeting (PM2): 실패"
fi
echo ""

echo "--- SSL 인증서 ---"
CERT_EXPIRY=$(sudo openssl x509 -enddate -noout -in /etc/letsencrypt/live/meeting.company.com/fullchain.pem | cut -d= -f2)
echo "만료일: $CERT_EXPIRY"
echo ""

echo "--- 디스크 공간 ---"
df -h / | tail -1
echo ""

echo "--- 메모리 ---"
free -h | grep Mem
echo ""

echo "--- 활성 연결 ---"
ss -tn state established | grep -c ":443" | xargs echo "포트 443:"
ss -tn state established | grep -c ":8080" | xargs echo "포트 8080:"
```

```bash
sudo chmod +x /usr/local/bin/vr-meeting-check.sh
```

### 11.2 로그 로테이션

```bash
sudo nano /etc/logrotate.d/vr-meeting
```

붙여넣기:

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

### 11.3 설정 백업

```bash
# 백업 스크립트 생성
sudo nano /usr/local/bin/vr-meeting-backup.sh
```

```bash
#!/bin/bash
BACKUP_DIR="/home/vr-admin/backups"
DATE=$(date +%Y%m%d)

mkdir -p $BACKUP_DIR

# 설정 파일들
tar -czf $BACKUP_DIR/config-$DATE.tar.gz \
    /etc/nginx/sites-available/vr-meeting \
    /etc/turnserver.conf \
    /home/vr-admin/vr-meeting/Server/ecosystem.config.js \
    /home/vr-admin/vr-meeting/Server/

# 7일간의 백업 유지
find $BACKUP_DIR -name "*.tar.gz" -mtime +7 -delete

echo "백업 완료: $BACKUP_DIR/config-$DATE.tar.gz"
```

```bash
sudo chmod +x /usr/local/bin/vr-meeting-backup.sh

# cron에 추가 (매일 오전 2시 백업)
(crontab -l 2>/dev/null; echo "0 2 * * * /usr/local/bin/vr-meeting-backup.sh") | crontab -
```

---

## 파트 12: 빠른 참조 명령어

### PM2 관리 (Node.js)

```bash
# === 상태 ===
pm2 status                    # 프로세스 목록
pm2 info vr-meeting           # 상세 정보

# === 제어 ===
pm2 stop vr-meeting           # 중지
pm2 start vr-meeting          # 시작
pm2 restart vr-meeting        # 재시작
pm2 reload vr-meeting         # 중단 없이 리로드

# === 로그 ===
pm2 logs                      # 모든 로그 (실시간)
pm2 logs vr-meeting           # 특정 로그
pm2 logs --lines 100          # 마지막 100줄
pm2 flush                     # 로그 삭제

# === 모니터링 ===
pm2 monit                     # 실시간 대시보드

# === 코드 업데이트 후 ===
cd ~/vr-meeting/Server
git pull
npm install
pm2 reload vr-meeting
```

### nginx 및 coturn 관리

```bash
# 시작/중지/재시작
sudo systemctl start nginx coturn
sudo systemctl stop nginx coturn
sudo systemctl restart nginx coturn

# 상태
sudo systemctl status nginx coturn
```

### 로그

```bash
# Node.js (PM2)
pm2 logs vr-meeting

# nginx
sudo tail -f /var/log/nginx/error.log

# coturn
sudo tail -f /var/log/turnserver/turnserver.log
```

### SSL 인증서

```bash
# 만료일 확인
sudo certbot certificates

# 수동 갱신
sudo certbot renew

# 갱신 테스트
sudo certbot renew --dry-run
```

### 네트워크 디버그

```bash
# 열린 포트
sudo ss -tlnp

# 연결된 연결
sudo ss -tn state established

# 실시간 트래픽
sudo tcpdump -i any port 443 or port 3478
```


---

## 부록 A: 전체 파일 설정

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
relay-ip=공인_IP
external-ip=공인_IP
min-port=49152
max-port=65535
cert=/etc/coturn/certs/fullchain.pem
pkey=/etc/coturn/certs/privkey.pem
lt-cred-mech
user=vrmeeting:보안TURN비밀번호_2024!
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
