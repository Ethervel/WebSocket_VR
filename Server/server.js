/**
 * WebSocket Server for VR Meeting Rooms Application
 * Version avec FILTRAGE PAR ROOM pour sync objets et whiteboard
 * 
 * Installation:
 *   npm init -y
 *   npm install ws uuid
 * 
 * Execution:
 *   node server.js
 */

const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');
const { registerUser, loginUser, updateUserProfile } = require('./auth');

// File Presentation module (optional - for PDF conversion)
let filePresentation = null;
try {
    filePresentation = require('./filePresentation');
    console.log(`[SERVER] File presentation module loaded (pdf-poppler: ${filePresentation.pdfPoppler ? 'available' : 'not installed'})`);
} catch (e) {
    console.log('[SERVER] File presentation module not loaded');
}

const PORT = process.env.PORT || 8080;
const HEARTBEAT_INTERVAL = 30000;

const clients = new Map(); // clientId -> { ws, roomId, playerName }
const rooms = new Map();   // roomId -> RoomInfo

const wss = new WebSocket.Server({ port: PORT });

console.log(`[SERVER] WebSocket server started on port ${PORT}`);

// ========================================
// CONNECTION
// ========================================

wss.on('connection', (ws) => {
    const clientId = uuidv4();
    
    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });
    
    console.log(`[SERVER] Client connected: ${clientId}`);
    
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });
    
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);
    
    sendRoomList(ws);
    
    ws.on('message', (data) => {
        try {
            const message = JSON.parse(data.toString());
            handleMessage(clientId, message);
        } catch (e) {
            console.error(`[SERVER] Parse error: ${e.message}`);
        }
    });
    
    ws.on('close', () => {
        handleDisconnect(clientId);
    });
    
    ws.on('error', (error) => {
        console.error(`[SERVER] Client error (${clientId}): ${error.message}`);
    });
    
    ws.on('pong', () => {
        const client = clients.get(clientId);
        if (client) {
            client.lastHeartbeat = Date.now();
        }
    });
});

// ========================================
// MESSAGE ROUTING
// ========================================

function handleMessage(clientId, message) {
    const { type, senderId, data } = message;
    message.senderId = clientId;
    
    console.log(`[SERVER] Message from ${clientId}: ${type}`);
    
    switch (type) {
        // === ROOM LIFECYCLE ===
        case 'room-available':
            handleRoomAvailable(clientId, data);
            break;
            
        case 'room-closed':
            handleRoomClosed(clientId, data);
            break;
            
        case 'room-join':
            handleRoomJoin(clientId, data);
            break;
            
        case 'room-leave':
            handleRoomLeave(clientId, data);
            break;
            
        case 'room-list-request':
            sendRoomList(clients.get(clientId)?.ws);
            break;
            
        case 'room-update':
            handleRoomUpdate(clientId, data);
            break;
            
        // === VR POSITION (PAR ROOM) ===
        case 'vr-position':
        case 'position':
            broadcastToRoom(clientId, message);
            break;
            
        // === OBJETS INTERACTIFS (PAR ROOM) ===
        case 'obj-sync':
        case 'obj-state':
            broadcastToRoom(clientId, message);
            break;
            
        // === WHITEBOARD (PAR ROOM) ===
        case 'whiteboard-draw':
        case 'whiteboard-batch':
        case 'whiteboard-clear':
            broadcastToRoom(clientId, message);
            break;
            
        // === WHITEBOARD STATE (POINT-TO-POINT) ===
        case 'whiteboard-request':
            // Relayer à toute la room pour que quelqu'un réponde
            broadcastToRoom(clientId, message);
            break;
            
        case 'whiteboard-state':
            // Envoyer seulement à celui qui a demandé
            // (géré dans handleWhiteboardState)
            handleWhiteboardState(clientId, data);
            break;
            
        // === ROOM STATE (GLOBAL) ===
        case 'room-welcome':
        case 'room-teleport':
        case 'player-name-update':
            broadcastToRoom(clientId, message);
            break;

        // === WEBRTC SIGNALING (POINT-TO-POINT) ===
        case 'webrtc-offer':
            handleWebRTCOffer(clientId, data);
            break;

        case 'webrtc-answer':
            handleWebRTCAnswer(clientId, data);
            break;

        case 'webrtc-ice-candidate':
            handleWebRTCIceCandidate(clientId, data);
            break;

        // === SCREEN SHARING (PAR ROOM) ===
        case 'screen-share-start':
        case 'screen-share-stop':
        case 'screen-share-frame':
        case 'screen-share-request':
        case 'screen-share-state':
            console.log(`[ScreenShare] ${type} from ${clientId}`);
            broadcastToRoom(clientId, message);
            break;

        // === SCREEN SHARING WEBRTC (POINT-TO-POINT) ===
        case 'screen-video-offer':
            handleScreenVideoOffer(clientId, data);
            break;

        case 'screen-video-answer':
            handleScreenVideoAnswer(clientId, data);
            break;

        case 'screen-video-ice':
            handleScreenVideoIce(clientId, data);
            break;

        // === FILE SHARING (PAR ROOM) ===
        case 'file-announce':
        case 'file-chunk':
        case 'file-complete':
        case 'file-request':
        case 'file-list-request':
            console.log(`[FileShare] ${type} from ${clientId}`);
            broadcastToRoom(clientId, message);
            break;

        case 'file-list-response':
            handleFileListResponse(clientId, data);
            break;

        // === FILE PRESENTATION (PAR ROOM) ===
        case 'file-present-start':
        case 'file-present-page':
        case 'file-present-navigate':
        case 'file-present-stop':
        case 'file-present-request':
            console.log(`[FilePresent] ${type} from ${clientId}`);
            broadcastToRoom(clientId, message);
            break;

        case 'file-present-state':
            handleFilePresentState(clientId, data);
            break;

        // === PDF CONVERSION (POINT-TO-POINT avec serveur) ===
        case 'pdf-convert-request':
            handlePdfConvertRequest(clientId, data);
            break;

        case 'pdf-page-request':
            handlePdfPageRequest(clientId, data);
            break;

        // === AUTHENTICATION ===
        case 'auth-register':
            handleAuthRegister(clientId, data);
            break;

        case 'auth-login':
            handleAuthLogin(clientId, data);
            break;

        case 'auth-update-profile':
            handleAuthUpdateProfile(clientId, data);
            break;

        // === VOICE TEST (DEBUG) ===
        case 'voice-test-tone':
            broadcastToRoom(clientId, message);
            break;

        default:
            // Par défaut: broadcast à la room si le client est dans une room
            const client = clients.get(clientId);
            if (client && client.roomId) {
                broadcastToRoom(clientId, message);
            } else {
                broadcast(message, clientId);
            }
    }
}

// ========================================
// ROOM MANAGEMENT
// ========================================

function handleRoomAvailable(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        
        const roomInfo = {
            roomId: data.roomId,
            hostId: clientId,
            roomName: data.roomName || `Room ${data.roomId}`,
            roomType: data.roomType || 0,
            playerCount: 1,
            maxPlayers: data.maxPlayers || 10,
            createdAt: Date.now()
        };
        
        rooms.set(data.roomId, roomInfo);
        
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
        }
        
        console.log(`[SERVER] Room created: ${data.roomId} by ${clientId}`);
        
        broadcastRoomList();
        broadcast({
            type: 'room-available',
            senderId: clientId,
            data: JSON.stringify(roomInfo)
        });
        
    } catch (e) {
        console.error(`[SERVER] handleRoomAvailable error: ${e.message}`);
    }
}

function handleRoomClosed(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);
        
        if (room && room.hostId === clientId) {
            rooms.delete(data.roomId);
            console.log(`[SERVER] Room closed: ${data.roomId}`);
            
            broadcast({
                type: 'room-closed',
                senderId: clientId,
                data: JSON.stringify(data)
            });
            
            broadcastRoomList();
        }
        
    } catch (e) {
        console.error(`[SERVER] handleRoomClosed error: ${e.message}`);
    }
}

function handleRoomJoin(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);
        
        if (!room) {
            sendError(clientId, `Room ${data.roomId} not found`);
            return;
        }
        
        if (room.playerCount >= room.maxPlayers) {
            sendError(clientId, 'Room is full');
            return;
        }
        
        const client = clients.get(clientId);
        if (client) {
            client.roomId = data.roomId;
            client.playerName = data.playerName || 'Player';
        }
        
        room.playerCount++;
        
        console.log(`[SERVER] Player ${clientId} joined room ${data.roomId}`);
        
        // Broadcast SEULEMENT à cette room
        broadcastToRoom(clientId, {
            type: 'room-join',
            senderId: clientId,
            data: JSON.stringify(data)
        });
        
        broadcastRoomList();
        
    } catch (e) {
        console.error(`[SERVER] handleRoomJoin error: ${e.message}`);
    }
}

function handleRoomLeave(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);
        
        if (room) {
            room.playerCount = Math.max(0, room.playerCount - 1);
        }
        
        const client = clients.get(clientId);
        if (client) {
            client.roomId = null;
        }
        
        console.log(`[SERVER]  Player ${clientId} left room ${data.roomId}`);
        
        broadcastToRoom(clientId, {
            type: 'room-leave',
            senderId: clientId,
            data: JSON.stringify(data)
        });
        
        broadcastRoomList();
        
    } catch (e) {
        console.error(`[SERVER] handleRoomLeave error: ${e.message}`);
    }
}

function handleRoomUpdate(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);
        
        if (room && room.hostId === clientId) {
            room.playerCount = data.playerCount || room.playerCount;
            room.roomName = data.roomName || room.roomName;
            broadcastRoomList();
        }
        
    } catch (e) {
        console.error(`[SERVER] handleRoomUpdate error: ${e.message}`);
    }
}

function handleDisconnect(clientId) {
    const client = clients.get(clientId);
    
    if (client) {
        if (client.roomId) {
            const room = rooms.get(client.roomId);
            
            if (room) {
                if (room.hostId === clientId) {
                    rooms.delete(client.roomId);
                    broadcast({
                        type: 'room-closed',
                        senderId: clientId,
                        data: JSON.stringify({ roomId: client.roomId })
                    });
                } else {
                    room.playerCount = Math.max(0, room.playerCount - 1);
                }
            }
            
            // Notifier SEULEMENT la room du départ
            broadcastToRoom(clientId, {
                type: 'room-leave',
                senderId: clientId,
                data: JSON.stringify({
                    roomId: client.roomId,
                    playerId: clientId
                })
            });
        }
    }
    
    clients.delete(clientId);
    
    broadcast({
        type: 'peer-disconnected',
        senderId: clientId
    });
    
    broadcastRoomList();
    
    console.log(`[SERVER]  Client disconnected: ${clientId}`);
}

// ========================================
// WHITEBOARD HANDLERS
// ========================================

function handleWhiteboardState(clientId, dataStr) {
    try {
        const stateData = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        
        // Si targetId est spécifié, envoyer seulement à ce client
        if (stateData.targetId) {
            const targetClient = clients.get(stateData.targetId);
            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                sendToClient(targetClient.ws, {
                    type: 'whiteboard-state',
                    senderId: clientId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });
                
                const sizeKB = stateData.textureData ? 
                    (stateData.textureData.length * 0.75 / 1024).toFixed(2) : '0';
                
                console.log(`[Whiteboard] State sent ${clientId} → ${stateData.targetId} (${sizeKB} KB)`);
            }
        } else {
            // Sinon, broadcast à toute la room
            broadcastToRoom(clientId, {
                type: 'whiteboard-state',
                senderId: clientId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }
        
    } catch (e) {
        console.error(`[Whiteboard] handleWhiteboardState error: ${e.message}`);
    }
}

// ========================================
// WEBRTC SIGNALING
// ========================================

function handleWebRTCOffer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;
        
        const targetClient = clients.get(targetId);
        if (!targetClient) return;
        
        sendToClient(targetClient.ws, {
            type: 'webrtc-offer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });
        
        console.log(`[WebRTC] Offer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[WebRTC] handleWebRTCOffer error: ${e.message}`);
    }
}

function handleWebRTCAnswer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;
        
        const targetClient = clients.get(targetId);
        if (!targetClient) return;
        
        sendToClient(targetClient.ws, {
            type: 'webrtc-answer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });
        
        console.log(`[WebRTC] Answer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[WebRTC] handleWebRTCAnswer error: ${e.message}`);
    }
}

function handleWebRTCIceCandidate(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, candidate, sdpMid, sdpMLineIndex } = data;
        
        const targetClient = clients.get(targetId);
        if (!targetClient) return;
        
        sendToClient(targetClient.ws, {
            type: 'webrtc-ice-candidate',
            senderId: senderId,
            data: JSON.stringify({ candidate, sdpMid, sdpMLineIndex })
        });
        
    } catch (e) {
        console.error(`[WebRTC] handleWebRTCIceCandidate error: ${e.message}`);
    }
}

// ========================================
// SCREEN SHARING WEBRTC SIGNALING
// ========================================

function handleScreenVideoOffer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) {
            console.log(`[ScreenVideo] Target ${targetId} not found for offer`);
            return;
        }

        sendToClient(targetClient.ws, {
            type: 'screen-video-offer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });

        console.log(`[ScreenVideo] Offer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[ScreenVideo] handleScreenVideoOffer error: ${e.message}`);
    }
}

function handleScreenVideoAnswer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) {
            console.log(`[ScreenVideo] Target ${targetId} not found for answer`);
            return;
        }

        sendToClient(targetClient.ws, {
            type: 'screen-video-answer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });

        console.log(`[ScreenVideo] Answer: ${senderId} → ${targetId}`);
    } catch (e) {
        console.error(`[ScreenVideo] handleScreenVideoAnswer error: ${e.message}`);
    }
}

function handleScreenVideoIce(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, candidate, sdpMid, sdpMLineIndex } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) return;

        sendToClient(targetClient.ws, {
            type: 'screen-video-ice',
            senderId: senderId,
            data: JSON.stringify({ candidate, sdpMid, sdpMLineIndex })
        });

    } catch (e) {
        console.error(`[ScreenVideo] handleScreenVideoIce error: ${e.message}`);
    }
}

// ========================================
// FILE SHARING HANDLERS
// ========================================

function handleFileListResponse(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // Si targetId est spécifié, envoyer seulement à ce client
        if (data.targetId) {
            const targetClient = clients.get(data.targetId);
            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                sendToClient(targetClient.ws, {
                    type: 'file-list-response',
                    senderId: senderId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });

                console.log(`[FileShare] List response: ${senderId} → ${data.targetId}`);
            }
        } else {
            // Sinon, broadcast à toute la room
            broadcastToRoom(senderId, {
                type: 'file-list-response',
                senderId: senderId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }

    } catch (e) {
        console.error(`[FileShare] handleFileListResponse error: ${e.message}`);
    }
}

// ========================================
// FILE PRESENTATION HANDLERS
// ========================================

function handleFilePresentState(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        // Si targetId est spécifié, envoyer seulement à ce client
        if (data.targetId) {
            const targetClient = clients.get(data.targetId);
            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                sendToClient(targetClient.ws, {
                    type: 'file-present-state',
                    senderId: clientId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });

                console.log(`[FilePresent] State sent ${clientId} → ${data.targetId}`);
            }
        } else {
            // Sinon, broadcast à toute la room
            broadcastToRoom(clientId, {
                type: 'file-present-state',
                senderId: clientId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }

    } catch (e) {
        console.error(`[FilePresent] handleFilePresentState error: ${e.message}`);
    }
}

// ========================================
// PDF CONVERSION HANDLERS
// ========================================

// Cache pour les PDFs convertis: fileId -> { pages: [base64...], totalPages, timestamp }
const pdfCache = new Map();
const PDF_CACHE_TTL = 30 * 60 * 1000; // 30 minutes

async function handlePdfConvertRequest(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { roomId, fileId, fileDataBase64, requesterId } = data;

        console.log(`[PDFConvert] Request from ${requesterId} for file ${fileId}`);

        // Utiliser le module filePresentation si disponible
        if (filePresentation && filePresentation.pdfPoppler) {
            await filePresentation.handlePdfConvertRequest(clientId, data, clients, sendToClient);
            return;
        }

        // Vérifier le cache local (fallback)
        if (pdfCache.has(fileId)) {
            const cached = pdfCache.get(fileId);
            sendPdfConvertResponse(requesterId, fileId, roomId, {
                success: true,
                totalPages: cached.totalPages
            });
            console.log(`[PDFConvert] Cache hit for ${fileId}`);
            return;
        }

        // pdf-poppler non disponible
        sendPdfConvertResponse(requesterId, fileId, roomId, {
            success: false,
            error: 'PDF conversion not available. Install pdf-poppler: npm install pdf-poppler'
        });

    } catch (e) {
        console.error(`[PDFConvert] Error: ${e.message}`);
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        sendPdfConvertResponse(data.requesterId, data.fileId, data.roomId, {
            success: false,
            error: e.message
        });
    }
}

function handlePdfPageRequest(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { roomId, fileId, pageNumber, requesterId } = data;

        // Utiliser le module filePresentation si disponible
        if (filePresentation && filePresentation.pdfPoppler) {
            filePresentation.handlePdfPageRequest(clientId, data, clients, sendToClient);
            return;
        }

        // Fallback vers le cache local
        const cached = pdfCache.get(fileId);
        if (!cached || pageNumber >= cached.pages.length) {
            console.log(`[PDFPage] Page ${pageNumber} not found for ${fileId}`);
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
                imageDataBase64: cached.pages[pageNumber],
                width: 1920,
                height: 1080
            })
        });

        console.log(`[PDFPage] Sent page ${pageNumber} of ${fileId} to ${requesterId}`);

    } catch (e) {
        console.error(`[PDFPage] Error: ${e.message}`);
    }
}

function sendPdfConvertResponse(targetId, fileId, roomId, result) {
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

// Nettoyage du cache PDF
setInterval(() => {
    const now = Date.now();
    for (const [fileId, entry] of pdfCache) {
        if (now - entry.timestamp > PDF_CACHE_TTL) {
            pdfCache.delete(fileId);
            console.log(`[PDFCache] Expired: ${fileId}`);
        }
    }
}, 5 * 60 * 1000); // Vérifier toutes les 5 minutes

// ========================================
// AUTHENTICATION HANDLERS
// ========================================

async function handleAuthRegister(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { username, email, password, displayName } = data;

        if (!username || !email || !password) {
            sendAuthResponse(clientId, 'auth-register-response', {
                success: false,
                error: 'Missing required fields'
            });
            return;
        }

        const result = await registerUser(username, email, password, displayName);

        sendAuthResponse(clientId, 'auth-register-response', result);

        if (result.success) {
            const client = clients.get(clientId);
            if (client) {
                client.userId = result.userId;
                client.playerName = result.displayName;
            }
        }

    } catch (e) {
        console.error('[Auth] handleAuthRegister error:', e.message);
        sendAuthResponse(clientId, 'auth-register-response', {
            success: false,
            error: 'Server error'
        });
    }
}

async function handleAuthLogin(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { username, password } = data;

        if (!username || !password) {
            sendAuthResponse(clientId, 'auth-login-response', {
                success: false,
                error: 'Missing credentials'
            });
            return;
        }

        const result = await loginUser(username, password);

        sendAuthResponse(clientId, 'auth-login-response', result);

        if (result.success) {
            const client = clients.get(clientId);
            if (client) {
                client.userId = result.userId;
                client.playerName = result.displayName;
            }
        }

    } catch (e) {
        console.error('[Auth] handleAuthLogin error:', e.message);
        sendAuthResponse(clientId, 'auth-login-response', {
            success: false,
            error: 'Server error'
        });
    }
}

async function handleAuthUpdateProfile(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { displayName, avatarColor } = data;

        const client = clients.get(clientId);
        if (!client || !client.userId) {
            sendAuthResponse(clientId, 'auth-update-response', {
                success: false,
                error: 'Not authenticated'
            });
            return;
        }

        const result = await updateUserProfile(client.userId, displayName, avatarColor);

        if (result.success && displayName) {
            client.playerName = displayName;
        }

        sendAuthResponse(clientId, 'auth-update-response', result);

    } catch (e) {
        console.error('[Auth] handleAuthUpdateProfile error:', e.message);
        sendAuthResponse(clientId, 'auth-update-response', {
            success: false,
            error: 'Server error'
        });
    }
}

function sendAuthResponse(clientId, type, data) {
    const client = clients.get(clientId);
    if (client) {
        sendToClient(client.ws, {
            type: type,
            senderId: 'server',
            data: JSON.stringify(data)
        });
    }
}

// ========================================
// BROADCAST UTILITIES
// ========================================

function sendToClient(ws, message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
    }
}

function broadcast(message, exceptClientId = null) {
    const messageStr = JSON.stringify(message);
    
    clients.forEach((client, clientId) => {
        if (clientId !== exceptClientId && client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}

/**
 *  FONCTION CRITIQUE: Broadcast SEULEMENT aux clients de la même room
 */
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    if (!sender || !sender.roomId) {
        return;
    }
    
    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);
    
    let recipientCount = 0;
    
    clients.forEach((client, clientId) => {
        // Envoyer SEULEMENT si:
        // 1. Ce n'est pas l'expéditeur
        // 2. Le client est dans la MÊME room
        // 3. La connexion est ouverte
        if (clientId !== senderId && 
            client.roomId === roomId && 
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
            recipientCount++;
        }
    });
    
    // Log détaillé pour debug
    if (message.type && (message.type.includes('whiteboard') || message.type.includes('obj-'))) {
        console.log(`[Room:${roomId}] ${message.type} from ${senderId} → ${recipientCount} clients`);
    }
}

function sendRoomList(ws) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;
    
    const roomList = Array.from(rooms.values());
    
    sendToClient(ws, {
        type: 'room-list',
        senderId: 'server',
        data: JSON.stringify({ rooms: roomList })
    });
}

function broadcastRoomList() {
    const roomList = Array.from(rooms.values());
    
    broadcast({
        type: 'room-list',
        senderId: 'server',
        data: JSON.stringify({ rooms: roomList })
    });
}

function sendError(clientId, errorMessage) {
    const client = clients.get(clientId);
    if (client) {
        sendToClient(client.ws, {
            type: 'error',
            senderId: 'server',
            data: errorMessage
        });
    }
}

// ========================================
// SERVER MAINTENANCE
// ========================================

const heartbeatInterval = setInterval(() => {
    const now = Date.now();
    
    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });
    
    clients.forEach((client, clientId) => {
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            console.log(`[SERVER] Client timeout: ${clientId}`);
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });
    
}, HEARTBEAT_INTERVAL);

process.on('SIGINT', () => {
    console.log('\n[SERVER] Shutting down...');
    clearInterval(heartbeatInterval);
    
    wss.clients.forEach((ws) => {
        ws.close();
    });
    
    wss.close(() => {
        console.log('[SERVER] Server closed');
        process.exit(0);
    });
});

setInterval(() => {
    const roomDetails = Array.from(rooms.values())
        .map(r => `${r.roomId}(${r.playerCount})`)
        .join(', ');
    console.log(`[SERVER]  ${clients.size} clients | Rooms: ${roomDetails || 'none'}`);
}, 60000);