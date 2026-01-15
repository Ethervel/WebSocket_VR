/**
 * Authentication handlers
 */

const bcrypt = require('bcrypt');
const db = require('./db');

const SALT_ROUNDS = 10;

async function registerUser(username, email, password, displayName) {
    try {
        // Check if user exists
        const [existing] = await db.query(
            'SELECT id FROM users WHERE username = ? OR email = ?',
            [username, email]
        );

        if (existing.length > 0) {
            return { success: false, error: 'Username or email already exists' };
        }

        // Hash password
        const passwordHash = await bcrypt.hash(password, SALT_ROUNDS);

        // Insert user
        const [result] = await db.query(
            'INSERT INTO users (username, email, password_hash, display_name) VALUES (?, ?, ?, ?)',
            [username, email, passwordHash, displayName || username]
        );

        console.log('[Auth] User registered: ' + username + ' (id: ' + result.insertId + ')');

        return {
            success: true,
            userId: result.insertId,
            username: username,
            displayName: displayName || username
        };

    } catch (err) {
        console.error('[Auth] Register error:', err.message);
        return { success: false, error: 'Registration failed' };
    }
}

async function loginUser(username, password) {
    try {
        const [users] = await db.query(
            'SELECT id, username, email, password_hash, display_name, avatar_color FROM users WHERE username = ? OR email = ?',
            [username, username]
        );

        if (users.length === 0) {
            return { success: false, error: 'User not found' };
        }

        const user = users[0];
        const passwordMatch = await bcrypt.compare(password, user.password_hash);

        if (!passwordMatch) {
            return { success: false, error: 'Invalid password' };
        }

        // Update last login
        await db.query('UPDATE users SET last_login = NOW() WHERE id = ?', [user.id]);

        console.log('[Auth] User logged in: ' + user.username);

        return {
            success: true,
            userId: user.id,
            username: user.username,
            email: user.email,
            displayName: user.display_name,
            avatarColor: user.avatar_color
        };

    } catch (err) {
        console.error('[Auth] Login error:', err.message);
        return { success: false, error: 'Login failed' };
    }
}

async function updateUserProfile(userId, displayName, avatarColor) {
    try {
        await db.query(
            'UPDATE users SET display_name = ?, avatar_color = ? WHERE id = ?',
            [displayName, avatarColor, userId]
        );
        return { success: true };
    } catch (err) {
        console.error('[Auth] Update profile error:', err.message);
        return { success: false, error: 'Update failed' };
    }
}

module.exports = { registerUser, loginUser, updateUserProfile };
