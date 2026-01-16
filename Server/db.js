/**
 * Database configuration for MariaDB
 */

const mysql = require('mysql2/promise');

const pool = mysql.createPool({
    host: process.env.DB_HOST || 'localhost',
    port: process.env.DB_PORT || 3306,
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD || 'JJkk2812',
    database: process.env.DB_NAME || 'vr_meeting',
    waitForConnections: true,
    connectionLimit: 10,
    queueLimit: 0
});

// Test connection
pool.getConnection()
    .then(conn => {
        console.log('[DB] Connected to MariaDB');
        conn.release();
    })
    .catch(err => {
        console.error('[DB] Connection failed:', err.message);
    });

module.exports = pool;
