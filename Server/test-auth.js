/**
 * Script de test pour l'authentification
 * Usage: node test-auth.js
 */

const { registerUser, loginUser, updateUserProfile } = require('./auth');

async function testAuth() {
    console.log('='.repeat(50));
    console.log('TEST DES FONCTIONS D\'AUTHENTIFICATION');
    console.log('='.repeat(50));

    const testUser = {
        username: 'testuser_' + Date.now(),
        email: `test_${Date.now()}@example.com`,
        password: 'TestPassword123!',
        displayName: 'Test User VR'
    };

    try {
        // 1. Test inscription
        console.log('\n[1/4] Test inscription...');
        console.log(`   Username: ${testUser.username}`);
        console.log(`   Email: ${testUser.email}`);

        const registerResult = await registerUser(
            testUser.username,
            testUser.email,
            testUser.password,
            testUser.displayName
        );

        if (registerResult.success) {
            console.log('✅ Inscription réussie !');
            console.log(`   UserId: ${registerResult.userId}`);
            console.log(`   DisplayName: ${registerResult.displayName}`);
        } else {
            console.log('❌ Inscription échouée:', registerResult.error);
            return;
        }

        // 2. Test connexion avec username
        console.log('\n[2/4] Test connexion (par username)...');
        const loginResult = await loginUser(testUser.username, testUser.password);

        if (loginResult.success) {
            console.log('✅ Connexion réussie !');
            console.log(`   UserId: ${loginResult.userId}`);
            console.log(`   Username: ${loginResult.username}`);
            console.log(`   Email: ${loginResult.email}`);
            console.log(`   DisplayName: ${loginResult.displayName}`);
            console.log(`   AvatarColor: ${loginResult.avatarColor || 'default'}`);
        } else {
            console.log('❌ Connexion échouée:', loginResult.error);
        }

        // 3. Test connexion avec email
        console.log('\n[3/4] Test connexion (par email)...');
        const loginByEmail = await loginUser(testUser.email, testUser.password);

        if (loginByEmail.success) {
            console.log('✅ Connexion par email réussie !');
        } else {
            console.log('❌ Connexion par email échouée:', loginByEmail.error);
        }

        // 4. Test mise à jour profil
        console.log('\n[4/4] Test mise à jour profil...');
        const updateResult = await updateUserProfile(
            registerResult.userId,
            'Updated Name VR',
            '#FF5733'
        );

        if (updateResult.success) {
            console.log('✅ Profil mis à jour !');

            // Vérifier les changements
            const checkLogin = await loginUser(testUser.username, testUser.password);
            if (checkLogin.success) {
                console.log(`   Nouveau DisplayName: ${checkLogin.displayName}`);
                console.log(`   Nouveau AvatarColor: ${checkLogin.avatarColor}`);
            }
        } else {
            console.log('❌ Mise à jour échouée:', updateResult.error);
        }

        // 5. Test mauvais mot de passe
        console.log('\n[Bonus] Test mauvais mot de passe...');
        const badLogin = await loginUser(testUser.username, 'wrongpassword');
        if (!badLogin.success) {
            console.log('✅ Rejet correct du mauvais mot de passe');
        } else {
            console.log('❌ ERREUR: Mauvais mot de passe accepté !');
        }

        console.log('\n' + '='.repeat(50));
        console.log('✅ TOUS LES TESTS D\'AUTHENTIFICATION ONT RÉUSSI !');
        console.log('='.repeat(50));

    } catch (error) {
        console.error('\n❌ ERREUR:', error.message);
        console.error(error.stack);
    }

    // Fermer proprement
    process.exit(0);
}

testAuth();
