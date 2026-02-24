const mariadb = require('mariadb');
require('dotenv').config();

const pool = mariadb.createPool({
    host: process.env.DB_HOST || 'localhost',
    port: parseInt(process.env.DB_PORT) || 3306,
    user: process.env.DB_USER,
    password: process.env.DB_PASSWORD,
    database: process.env.DB_NAME,
    connectionLimit: 10,
    acquireTimeout: 30000,
    connectTimeout: 10000
});

// Test connection on startup
async function testConnection() {
    let conn;
    try {
        conn = await pool.getConnection();
        console.log('[Database] Connected to MariaDB');
        return true;
    } catch (err) {
        console.error('[Database] Connection failed:', err.message);
        return false;
    } finally {
        if (conn) conn.release();
    }
}

// User queries
async function createUser(email, passwordHash, displayName) {
    let conn;
    try {
        conn = await pool.getConnection();
        const result = await conn.query(
            'INSERT INTO users (email, password_hash, display_name) VALUES (?, ?, ?)',
            [email, passwordHash, displayName]
        );
        return { success: true, userId: result.insertId.toString() };
    } catch (err) {
        if (err.code === 'ER_DUP_ENTRY') {
            return { success: false, error: 'Email already exists' };
        }
        console.error('[Database] createUser error:', err.message);
        return { success: false, error: 'Database error' };
    } finally {
        if (conn) conn.release();
    }
}

async function getUserByEmail(email) {
    let conn;
    try {
        conn = await pool.getConnection();
        const rows = await conn.query(
            'SELECT id, email, password_hash, display_name, avatar_config FROM users WHERE email = ? AND is_active = TRUE',
            [email]
        );
        return rows.length > 0 ? rows[0] : null;
    } catch (err) {
        console.error('[Database] getUserByEmail error:', err.message);
        return null;
    } finally {
        if (conn) conn.release();
    }
}

async function getUserById(userId) {
    let conn;
    try {
        conn = await pool.getConnection();
        const rows = await conn.query(
            'SELECT id, email, display_name, avatar_config FROM users WHERE id = ? AND is_active = TRUE',
            [userId]
        );
        return rows.length > 0 ? rows[0] : null;
    } catch (err) {
        console.error('[Database] getUserById error:', err.message);
        return null;
    } finally {
        if (conn) conn.release();
    }
}

async function updateLastLogin(userId) {
    let conn;
    try {
        conn = await pool.getConnection();
        await conn.query(
            'UPDATE users SET last_login = CURRENT_TIMESTAMP WHERE id = ?',
            [userId]
        );
    } catch (err) {
        console.error('[Database] updateLastLogin error:', err.message);
    } finally {
        if (conn) conn.release();
    }
}

async function updateUserProfile(userId, displayName, avatarConfig) {
    let conn;
    try {
        conn = await pool.getConnection();
        await conn.query(
            'UPDATE users SET display_name = ?, avatar_config = ? WHERE id = ?',
            [displayName, avatarConfig, userId]
        );
        return { success: true };
    } catch (err) {
        console.error('[Database] updateUserProfile error:', err.message);
        return { success: false, error: 'Database error' };
    } finally {
        if (conn) conn.release();
    }
}

// Meeting logs
async function logMeetingAction(roomCode, userId, action) {
    let conn;
    try {
        conn = await pool.getConnection();
        await conn.query(
            'INSERT INTO meeting_logs (room_code, user_id, action) VALUES (?, ?, ?)',
            [roomCode, userId, action]
        );
    } catch (err) {
        console.error('[Database] logMeetingAction error:', err.message);
    } finally {
        if (conn) conn.release();
    }
}

module.exports = {
    pool,
    testConnection,
    createUser,
    getUserByEmail,
    getUserById,
    updateLastLogin,
    updateUserProfile,
    logMeetingAction
};
