/**
 * Script de test pour la base de données MariaDB
 * Usage: node test-db.js
 */

const mysql = require('mysql2/promise');

// Configuration (mêmes valeurs que db.js)
const config = {
    host: process.env.DB_HOST || 'localhost',
    port: process.env.DB_PORT || 3306,
    user: process.env.DB_USER || 'root',
    password: process.env.DB_PASSWORD || 'JJkk2812'
};

const DB_NAME = process.env.DB_NAME || 'vr_meeting';

async function testDatabase() {
    console.log('='.repeat(50));
    console.log('TEST DE LA BASE DE DONNÉES MARIADB');
    console.log('='.repeat(50));
    console.log(`Host: ${config.host}:${config.port}`);
    console.log(`User: ${config.user}`);
    console.log(`Database: ${DB_NAME}`);
    console.log('='.repeat(50));

    let connection;

    try {
        // 1. Test connexion au serveur MySQL/MariaDB
        console.log('\n[1/5] Connexion au serveur MariaDB...');
        connection = await mysql.createConnection(config);
        console.log('✅ Connexion réussie au serveur MariaDB');

        // 2. Créer la base de données si elle n'existe pas
        console.log(`\n[2/5] Création de la base de données '${DB_NAME}'...`);
        await connection.query(`CREATE DATABASE IF NOT EXISTS ${DB_NAME}`);
        console.log(`✅ Base de données '${DB_NAME}' prête`);

        // 3. Utiliser la base de données
        await connection.query(`USE ${DB_NAME}`);

        // 4. Créer la table users si elle n'existe pas
        console.log('\n[3/5] Création de la table users...');
        await connection.query(`
            CREATE TABLE IF NOT EXISTS users (
                id INT AUTO_INCREMENT PRIMARY KEY,
                username VARCHAR(50) UNIQUE NOT NULL,
                email VARCHAR(100) UNIQUE NOT NULL,
                password_hash VARCHAR(255) NOT NULL,
                display_name VARCHAR(50),
                avatar_color VARCHAR(20) DEFAULT '#3498db',
                last_login DATETIME,
                created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                INDEX idx_username (username),
                INDEX idx_email (email)
            )
        `);
        console.log('✅ Table users créée/vérifiée');

        // 5. Vérifier la structure de la table
        console.log('\n[4/5] Vérification de la structure...');
        const [columns] = await connection.query(`DESCRIBE users`);
        console.log('Colonnes de la table users:');
        columns.forEach(col => {
            console.log(`   - ${col.Field} (${col.Type})${col.Key === 'PRI' ? ' [PK]' : ''}`);
        });

        // 6. Compter les utilisateurs existants
        console.log('\n[5/5] Statistiques...');
        const [rows] = await connection.query('SELECT COUNT(*) as count FROM users');
        console.log(`   Utilisateurs enregistrés: ${rows[0].count}`);

        // Afficher les utilisateurs (sans les mots de passe)
        if (rows[0].count > 0) {
            const [users] = await connection.query('SELECT id, username, email, display_name, avatar_color, last_login FROM users LIMIT 10');
            console.log('\n   Derniers utilisateurs:');
            users.forEach(u => {
                console.log(`   - ${u.username} (${u.email}) - ${u.display_name || 'N/A'}`);
            });
        }

        console.log('\n' + '='.repeat(50));
        console.log('✅ TOUS LES TESTS ONT RÉUSSI !');
        console.log('='.repeat(50));
        console.log('\nLa base de données est prête pour le serveur VR Meeting.');
        console.log('Lancez le serveur avec: npm start');

    } catch (error) {
        console.error('\n❌ ERREUR:', error.message);

        if (error.code === 'ECONNREFUSED') {
            console.log('\n📋 SOLUTION:');
            console.log('   MariaDB/MySQL n\'est pas démarré.');
            console.log('   1. Installez MariaDB: https://mariadb.org/download/');
            console.log('   2. Démarrez le service:');
            console.log('      - Windows: net start MariaDB');
            console.log('      - Linux: sudo systemctl start mariadb');
        } else if (error.code === 'ER_ACCESS_DENIED_ERROR') {
            console.log('\n📋 SOLUTION:');
            console.log('   Accès refusé. Vérifiez le mot de passe.');
            console.log('   Mot de passe actuel configuré: ' + config.password);
            console.log('   Modifiez dans db.js ou utilisez une variable d\'environnement:');
            console.log('   set DB_PASSWORD=votre_mot_de_passe && node test-db.js');
        } else if (error.code === 'ER_BAD_DB_ERROR') {
            console.log('\n📋 SOLUTION:');
            console.log('   La base de données n\'existe pas (ce script devrait la créer).');
        }

        process.exit(1);
    } finally {
        if (connection) {
            await connection.end();
        }
    }
}

// Lancer le test
testDatabase();
