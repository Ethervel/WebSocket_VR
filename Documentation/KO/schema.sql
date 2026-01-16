-- ============================================
-- VR 회의 플랫폼 - 데이터베이스 스키마
-- MariaDB 10.6+
-- ============================================

CREATE DATABASE IF NOT EXISTS vr_meeting
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE vr_meeting;

-- ============================================
-- 테이블: users (인증)
-- ============================================
CREATE TABLE IF NOT EXISTS users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  username VARCHAR(50) NOT NULL,
  email VARCHAR(100) NOT NULL,
  password_hash VARCHAR(255) NOT NULL,
  display_name VARCHAR(100),
  avatar_color VARCHAR(20) DEFAULT '#3498db',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  last_login TIMESTAMP NULL,
  is_active BOOLEAN DEFAULT TRUE,

  UNIQUE INDEX idx_username (username),
  UNIQUE INDEX idx_email (email)
) ENGINE=InnoDB;

-- ============================================
-- 테이블: rooms (회의 세션)
-- ============================================
CREATE TABLE IF NOT EXISTS rooms (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_code VARCHAR(6) NOT NULL,
  room_name VARCHAR(100),
  host_id INT NOT NULL,
  room_type ENUM('Lobby', 'MeetingRoomA', 'MeetingRoomB') DEFAULT 'Lobby',
  max_players INT DEFAULT 10,
  is_active BOOLEAN DEFAULT TRUE,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  closed_at TIMESTAMP NULL,

  UNIQUE INDEX idx_room_code (room_code),
  INDEX idx_host (host_id),
  INDEX idx_active (is_active),

  FOREIGN KEY (host_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ============================================
-- 테이블: room_participants (참여 기록)
-- ============================================
CREATE TABLE IF NOT EXISTS room_participants (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  user_id INT NOT NULL,
  joined_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  left_at TIMESTAMP NULL,
  duration_seconds INT GENERATED ALWAYS AS (
    TIMESTAMPDIFF(SECOND, joined_at, IFNULL(left_at, NOW()))
  ) STORED,

  INDEX idx_room (room_id),
  INDEX idx_user (user_id),
  INDEX idx_room_user (room_id, user_id),

  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ============================================
-- 테이블: meetings (예약된 회의/기록)
-- ============================================
CREATE TABLE IF NOT EXISTS meetings (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT,
  title VARCHAR(200),
  description TEXT,
  organizer_id INT NOT NULL,
  scheduled_start TIMESTAMP NULL,
  scheduled_end TIMESTAMP NULL,
  actual_start TIMESTAMP NULL,
  actual_end TIMESTAMP NULL,
  recording_url VARCHAR(500) NULL,
  status ENUM('scheduled', 'in_progress', 'completed', 'cancelled') DEFAULT 'scheduled',
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

  INDEX idx_organizer (organizer_id),
  INDEX idx_status (status),
  INDEX idx_scheduled (scheduled_start),

  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE SET NULL,
  FOREIGN KEY (organizer_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ============================================
-- 테이블: meeting_invites (회의 초대)
-- ============================================
CREATE TABLE IF NOT EXISTS meeting_invites (
  id INT AUTO_INCREMENT PRIMARY KEY,
  meeting_id INT NOT NULL,
  user_id INT NOT NULL,
  status ENUM('pending', 'accepted', 'declined') DEFAULT 'pending',
  responded_at TIMESTAMP NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

  UNIQUE INDEX idx_meeting_user (meeting_id, user_id),
  INDEX idx_user_status (user_id, status),

  FOREIGN KEY (meeting_id) REFERENCES meetings(id) ON DELETE CASCADE,
  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ============================================
-- 테이블: shared_files (공유 파일)
-- ============================================
CREATE TABLE IF NOT EXISTS shared_files (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  uploader_id INT NOT NULL,
  file_name VARCHAR(255) NOT NULL,
  file_size BIGINT NOT NULL,
  mime_type VARCHAR(100),
  storage_path VARCHAR(500) NOT NULL,
  uploaded_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
  is_deleted BOOLEAN DEFAULT FALSE,

  INDEX idx_room (room_id),
  INDEX idx_uploader (uploader_id),

  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
  FOREIGN KEY (uploader_id) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ============================================
-- 테이블: whiteboard_snapshots (화이트보드 스냅샷)
-- ============================================
CREATE TABLE IF NOT EXISTS whiteboard_snapshots (
  id INT AUTO_INCREMENT PRIMARY KEY,
  room_id INT NOT NULL,
  whiteboard_id VARCHAR(50) NOT NULL,
  snapshot_data LONGBLOB NOT NULL,
  width INT NOT NULL,
  height INT NOT NULL,
  created_by INT NOT NULL,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

  INDEX idx_room_whiteboard (room_id, whiteboard_id),

  FOREIGN KEY (room_id) REFERENCES rooms(id) ON DELETE CASCADE,
  FOREIGN KEY (created_by) REFERENCES users(id) ON DELETE CASCADE
) ENGINE=InnoDB;

-- ============================================
-- 테이블: audit_logs (보안 로그)
-- ============================================
CREATE TABLE IF NOT EXISTS audit_logs (
  id BIGINT AUTO_INCREMENT PRIMARY KEY,
  user_id INT,
  action VARCHAR(50) NOT NULL,
  target_type VARCHAR(50),
  target_id INT,
  ip_address VARCHAR(45),
  user_agent VARCHAR(255),
  details JSON,
  created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,

  INDEX idx_user (user_id),
  INDEX idx_action (action),
  INDEX idx_created (created_at),

  FOREIGN KEY (user_id) REFERENCES users(id) ON DELETE SET NULL
) ENGINE=InnoDB;

-- ============================================
-- 유용한 뷰
-- ============================================

-- 뷰: 참여자 수가 포함된 활성 룸
CREATE OR REPLACE VIEW v_active_rooms AS
SELECT
  r.id,
  r.room_code,
  r.room_name,
  r.room_type,
  u.display_name AS host_name,
  r.max_players,
  COUNT(rp.id) AS current_players,
  r.created_at
FROM rooms r
JOIN users u ON r.host_id = u.id
LEFT JOIN room_participants rp ON r.id = rp.room_id AND rp.left_at IS NULL
WHERE r.is_active = TRUE
GROUP BY r.id;

-- 뷰: 사용자 통계
CREATE OR REPLACE VIEW v_user_stats AS
SELECT
  u.id,
  u.username,
  u.display_name,
  COUNT(DISTINCT rp.room_id) AS rooms_joined,
  SUM(rp.duration_seconds) AS total_time_seconds,
  MAX(rp.joined_at) AS last_activity
FROM users u
LEFT JOIN room_participants rp ON u.id = rp.user_id
GROUP BY u.id;

-- ============================================
-- 애플리케이션 사용자 (수동 생성 필요)
-- ============================================
-- CREATE USER 'vr_meeting_user'@'localhost' IDENTIFIED BY 'secure_password';
-- GRANT SELECT, INSERT, UPDATE, DELETE ON vr_meeting.* TO 'vr_meeting_user'@'localhost';
-- FLUSH PRIVILEGES;
