/**
 * File Presentation Module - PDF Conversion
 *
 * Ce module gere la conversion des PDFs en images pour la presentation.
 * Necessite l'installation de pdf-poppler:
 *   npm install pdf-poppler
 *
 * Sur Windows, vous devez aussi installer Poppler:
 *   1. Telecharger depuis: https://github.com/oschwartz10612/poppler-windows/releases
 *   2. Extraire dans C:\Program Files\poppler
 *   3. Ajouter C:\Program Files\poppler\Library\bin au PATH
 */

const fs = require('fs');
const path = require('path');
const os = require('os');

// Cache pour les PDFs convertis: fileId -> { pages: [base64...], totalPages, timestamp }
const pdfCache = new Map();
const PDF_CACHE_TTL = 30 * 60 * 1000; // 30 minutes

// Verifier si pdf-poppler est disponible
let pdfPoppler = null;
try {
    pdfPoppler = require('pdf-poppler');
    console.log('[PDFModule] pdf-poppler loaded successfully');
} catch (e) {
    console.log('[PDFModule] pdf-poppler not installed. PDF conversion will not work.');
    console.log('[PDFModule] To enable PDF conversion, run: npm install pdf-poppler');
}

/**
 * Convertit un PDF en images JPEG
 * @param {string} fileId - ID unique du fichier
 * @param {Buffer} pdfBuffer - Contenu du PDF
 * @returns {Promise<{success: boolean, totalPages: number, error?: string}>}
 */
async function convertPdfToImages(fileId, pdfBuffer) {
    if (!pdfPoppler) {
        return {
            success: false,
            totalPages: 0,
            error: 'pdf-poppler not installed. Run: npm install pdf-poppler'
        };
    }

    const tempDir = fs.mkdtempSync(path.join(os.tmpdir(), 'pdf-'));
    const pdfPath = path.join(tempDir, `${fileId}.pdf`);
    const outputDir = path.join(tempDir, 'pages');

    try {
        // Sauvegarder le PDF temporairement
        fs.writeFileSync(pdfPath, pdfBuffer);
        fs.mkdirSync(outputDir);

        // Options de conversion
        const opts = {
            format: 'jpeg',
            out_dir: outputDir,
            out_prefix: 'page',
            page: null,  // Toutes les pages
            scale: 1920  // Largeur max
        };

        // Convertir
        await pdfPoppler.convert(pdfPath, opts);

        // Lire les pages converties
        const pageFiles = fs.readdirSync(outputDir)
            .filter(f => f.endsWith('.jpg') || f.endsWith('.jpeg'))
            .sort((a, b) => {
                // Trier par numero de page
                const numA = parseInt(a.match(/\d+/)?.[0] || '0');
                const numB = parseInt(b.match(/\d+/)?.[0] || '0');
                return numA - numB;
            });

        const pages = [];
        for (const pageFile of pageFiles) {
            const pageBuffer = fs.readFileSync(path.join(outputDir, pageFile));
            pages.push(pageBuffer.toString('base64'));
        }

        // Mettre en cache
        pdfCache.set(fileId, {
            pages,
            totalPages: pages.length,
            timestamp: Date.now()
        });

        console.log(`[PDFModule] Converted ${fileId}: ${pages.length} pages`);

        return {
            success: true,
            totalPages: pages.length
        };

    } catch (e) {
        console.error(`[PDFModule] Conversion error: ${e.message}`);
        return {
            success: false,
            totalPages: 0,
            error: e.message
        };
    } finally {
        // Nettoyer les fichiers temporaires
        try {
            fs.rmSync(tempDir, { recursive: true, force: true });
        } catch (e) {
            console.warn(`[PDFModule] Cleanup warning: ${e.message}`);
        }
    }
}

/**
 * Recupere une page convertie depuis le cache
 * @param {string} fileId - ID du fichier
 * @param {number} pageNumber - Numero de page (0-indexed)
 * @returns {{success: boolean, imageBase64?: string, width?: number, height?: number}}
 */
function getPageFromCache(fileId, pageNumber) {
    const cached = pdfCache.get(fileId);
    if (!cached || pageNumber >= cached.pages.length) {
        return { success: false };
    }

    return {
        success: true,
        imageBase64: cached.pages[pageNumber],
        width: 1920,
        height: 1080
    };
}

/**
 * Verifie si un PDF est dans le cache
 * @param {string} fileId
 * @returns {boolean}
 */
function isInCache(fileId) {
    return pdfCache.has(fileId);
}

/**
 * Recupere les infos du cache pour un PDF
 * @param {string} fileId
 * @returns {{totalPages: number} | null}
 */
function getCacheInfo(fileId) {
    const cached = pdfCache.get(fileId);
    if (!cached) return null;
    return { totalPages: cached.totalPages };
}

/**
 * Nettoie les entrees expirees du cache
 */
function cleanupCache() {
    const now = Date.now();
    for (const [fileId, entry] of pdfCache) {
        if (now - entry.timestamp > PDF_CACHE_TTL) {
            pdfCache.delete(fileId);
            console.log(`[PDFModule] Cache expired: ${fileId}`);
        }
    }
}

/**
 * Handler pour les requetes de conversion PDF
 * A utiliser dans server.js
 */
async function handlePdfConvertRequest(clientId, data, clients, sendToClient) {
    const { roomId, fileId, fileDataBase64, requesterId } = data;

    console.log(`[PDFModule] Convert request from ${requesterId} for ${fileId}`);

    // Verifier le cache
    if (isInCache(fileId)) {
        const info = getCacheInfo(fileId);
        sendPdfConvertResponse(requesterId, fileId, roomId, {
            success: true,
            totalPages: info.totalPages
        }, clients, sendToClient);
        return;
    }

    // Convertir
    const pdfBuffer = Buffer.from(fileDataBase64, 'base64');
    const result = await convertPdfToImages(fileId, pdfBuffer);

    sendPdfConvertResponse(requesterId, fileId, roomId, result, clients, sendToClient);
}

/**
 * Handler pour les requetes de page PDF
 * A utiliser dans server.js
 */
function handlePdfPageRequest(clientId, data, clients, sendToClient) {
    const { roomId, fileId, pageNumber, requesterId } = data;

    const pageResult = getPageFromCache(fileId, pageNumber);
    if (!pageResult.success) {
        console.log(`[PDFModule] Page ${pageNumber} not found for ${fileId}`);
        return;
    }

    const targetClient = clients.get(requesterId);
    if (!targetClient) return;

    sendToClient(targetClient.ws, {
        type: 'pdf-page-response',
        senderId: 'server',
        data: JSON.stringify({
            roomId,
            fileId,
            targetId: requesterId,
            pageNumber,
            imageDataBase64: pageResult.imageBase64,
            width: pageResult.width,
            height: pageResult.height
        })
    });

    console.log(`[PDFModule] Sent page ${pageNumber} of ${fileId} to ${requesterId}`);
}

function sendPdfConvertResponse(targetId, fileId, roomId, result, clients, sendToClient) {
    const targetClient = clients.get(targetId);
    if (!targetClient) return;

    sendToClient(targetClient.ws, {
        type: 'pdf-convert-response',
        senderId: 'server',
        data: JSON.stringify({
            roomId,
            fileId,
            targetId,
            totalPages: result.totalPages || 0,
            success: result.success,
            error: result.error || null
        })
    });
}

// Nettoyage periodique du cache
setInterval(cleanupCache, 5 * 60 * 1000);

module.exports = {
    convertPdfToImages,
    getPageFromCache,
    isInCache,
    getCacheInfo,
    cleanupCache,
    handlePdfConvertRequest,
    handlePdfPageRequest,
    pdfPoppler: !!pdfPoppler  // Indique si pdf-poppler est disponible
};
