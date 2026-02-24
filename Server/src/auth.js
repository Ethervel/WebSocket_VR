const bcrypt = require('bcrypt');
const jwt = require('jsonwebtoken');
const db = require('./database');
require('dotenv').config();

const BCRYPT_ROUNDS = 12;
const JWT_SECRET = process.env.JWT_SECRET || 'default-secret-change-me';
const JWT_EXPIRES_IN = process.env.JWT_EXPIRES_IN || '24h';

// Rate limiting storage (in-memory, resets on server restart)
const loginAttempts = new Map();
const RATE_LIMIT_WINDOW = 60000; // 1 minute
const MAX_ATTEMPTS = 5;

function checkRateLimit(identifier) {
    const now = Date.now();
    const attempts = loginAttempts.get(identifier) || [];

    // Remove old attempts
    const recentAttempts = attempts.filter(time => now - time < RATE_LIMIT_WINDOW);
    loginAttempts.set(identifier, recentAttempts);

    if (recentAttempts.length >= MAX_ATTEMPTS) {
        return { allowed: false, retryAfter: Math.ceil((recentAttempts[0] + RATE_LIMIT_WINDOW - now) / 1000) };
    }

    return { allowed: true };
}

function recordAttempt(identifier) {
    const attempts = loginAttempts.get(identifier) || [];
    attempts.push(Date.now());
    loginAttempts.set(identifier, attempts);
}

function clearAttempts(identifier) {
    loginAttempts.delete(identifier);
}

// Register new user
async function register(email, password, displayName) {
    // Validate input
    if (!email || !password || !displayName) {
        return { success: false, error: 'Missing required fields' };
    }

    if (!isValidEmail(email)) {
        return { success: false, error: 'Invalid email format' };
    }

    if (password.length < 8) {
        return { success: false, error: 'Password must be at least 8 characters' };
    }

    if (displayName.length < 2 || displayName.length > 50) {
        return { success: false, error: 'Display name must be 2-50 characters' };
    }

    try {
        // Hash password
        const passwordHash = await bcrypt.hash(password, BCRYPT_ROUNDS);

        // Create user in database
        const result = await db.createUser(email.toLowerCase(), passwordHash, displayName);

        if (!result.success) {
            return result;
        }

        // Generate token
        const token = generateToken(result.userId, email.toLowerCase());

        console.log(`[Auth] User registered: ${email}`);

        return {
            success: true,
            userId: result.userId,
            displayName: displayName,
            token: token
        };
    } catch (err) {
        console.error('[Auth] Register error:', err.message);
        return { success: false, error: 'Registration failed' };
    }
}

// Login user
async function login(email, password, clientIp = 'unknown') {
    // Check rate limit
    const rateCheck = checkRateLimit(clientIp);
    if (!rateCheck.allowed) {
        return { success: false, error: `Too many attempts. Try again in ${rateCheck.retryAfter}s` };
    }

    // Validate input
    if (!email || !password) {
        return { success: false, error: 'Missing email or password' };
    }

    try {
        // Get user from database
        const user = await db.getUserByEmail(email.toLowerCase());

        if (!user) {
            recordAttempt(clientIp);
            return { success: false, error: 'Invalid email or password' };
        }

        // Verify password
        const validPassword = await bcrypt.compare(password, user.password_hash);

        if (!validPassword) {
            recordAttempt(clientIp);
            return { success: false, error: 'Invalid email or password' };
        }

        // Clear rate limit on success
        clearAttempts(clientIp);

        // Update last login
        await db.updateLastLogin(user.id);

        // Generate token
        const token = generateToken(user.id.toString(), user.email);

        console.log(`[Auth] User logged in: ${email}`);

        return {
            success: true,
            userId: user.id.toString(),
            displayName: user.display_name,
            avatarConfig: user.avatar_config,
            token: token
        };
    } catch (err) {
        console.error('[Auth] Login error:', err.message);
        return { success: false, error: 'Login failed' };
    }
}

// Verify JWT token
function verifyToken(token) {
    try {
        const decoded = jwt.verify(token, JWT_SECRET);
        return { valid: true, userId: decoded.userId, email: decoded.email };
    } catch (err) {
        return { valid: false, error: 'Invalid or expired token' };
    }
}

// Generate JWT token
function generateToken(userId, email) {
    return jwt.sign(
        { userId, email },
        JWT_SECRET,
        { expiresIn: JWT_EXPIRES_IN }
    );
}

// Validate email format
function isValidEmail(email) {
    const emailRegex = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;
    return emailRegex.test(email);
}

// Get user profile (for authenticated requests)
async function getUserProfile(userId) {
    const user = await db.getUserById(userId);
    if (!user) {
        return { success: false, error: 'User not found' };
    }
    return {
        success: true,
        userId: user.id.toString(),
        email: user.email,
        displayName: user.display_name,
        avatarConfig: user.avatar_config
    };
}

// Update user profile
async function updateProfile(userId, displayName, avatarConfig) {
    if (displayName && (displayName.length < 2 || displayName.length > 50)) {
        return { success: false, error: 'Display name must be 2-50 characters' };
    }

    return await db.updateUserProfile(userId, displayName, avatarConfig);
}

module.exports = {
    register,
    login,
    verifyToken,
    getUserProfile,
    updateProfile
};
