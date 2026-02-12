

const WebSocket = require('ws');
const { v4: uuidv4 } = require('uuid');

// CONFIGURATION


const PORT = process.env.PORT || 8080;
const HEARTBEAT_INTERVAL = 30000;  // 30 seconds
const PDF_CACHE_TTL = 30 * 60 * 1000;  // 30 minutes

// GLOBAL STATE

const clients = new Map();  // clientId -> { ws, roomId, playerName, lastHeartbeat }
const rooms = new Map();    // roomId -> RoomInfo
const pdfCache = new Map(); // fileId -> { pages, totalPages, timestamp }

// Presentation module (optional)
let filePresentation = null;
try {
    filePresentation = require('./filePresentation');
    console.log('[Server] filePresentation module loaded');
} catch (e) {
    console.log('[Server] filePresentation module not available');
}

// SERVER STARTUP

const wss = new WebSocket.Server({ port: PORT });

console.log('============================================');
console.log('  VR MEETING ROOMS - WebSocket Server');
console.log('============================================');
console.log(`  Port: ${PORT}`);
console.log(`  Heartbeat: ${HEARTBEAT_INTERVAL / 1000}s`);
console.log('============================================');

// CONNECTION HANDLING

wss.on('connection', (ws) => {
    const clientId = uuidv4();

    // Register the client
    clients.set(clientId, {
        ws: ws,
        roomId: null,
        playerName: 'Player',
        lastHeartbeat: Date.now()
    });

    console.log(`[Connect] Client ${clientId.substring(0, 8)}...`);

    // Send welcome message
    sendToClient(ws, {
        type: 'welcome',
        senderId: clientId
    });

    // Notify other clients
    broadcast({
        type: 'peer-connected',
        senderId: clientId
    }, clientId);

    // Send room list
    sendRoomList(ws);

    // Message handling
    ws.on('message', (data) => {
        try {
            const message = JSON.parse(data.toString());
            handleMessage(clientId, message);
        } catch (e) {
            console.error(`[Error] Parse: ${e.message}`);
        }
    });


    // Disconnection handling
    ws.on('close', () => {
        handleDisconnect(clientId);
    });

    // Error handling
    ws.on('error', (error) => {
        console.error(`[Error] Client ${clientId.substring(0, 8)}: ${error.message}`);
    });

    // Heartbeat
    ws.on('pong', () => {
        const client = clients.get(clientId);
        if (client) {
            client.lastHeartbeat = Date.now();
        }
    });
});

// MESSAGE ROUTING

function handleMessage(clientId, message) {
    const { type, data } = message;
    message.senderId = clientId;

    switch (type) {
        // --- Room Management ---
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

        // --- VR Position Sync ---
        case 'vr-position':
        case 'position':
            broadcastToRoom(clientId, message);
            break;

        // --- Interactive Objects ---
        case 'obj-sync':
        case 'obj-state':
            broadcastToRoom(clientId, message);
            break;

        // --- Whiteboard ---
        case 'whiteboard-draw':
        case 'whiteboard-batch':
        case 'whiteboard-clear':
        case 'whiteboard-request':
            broadcastToRoom(clientId, message);
            break;
        case 'whiteboard-state':
            handleWhiteboardState(clientId, data);
            break;

        // --- Room State ---
        case 'room-welcome':
        case 'room-teleport':
        case 'player-name-update':
            broadcastToRoom(clientId, message);
            break;

        // --- Kick Player (host only, forward to target) ---
        case 'kick-player':
            handleKickPlayer(clientId, data);
            break;

        // --- WebRTC Voice Chat ---
        case 'webrtc-offer':
            handleWebRTCOffer(clientId, data);
            break;
        case 'webrtc-answer':
            handleWebRTCAnswer(clientId, data);
            break;
        case 'webrtc-ice-candidate':
            handleWebRTCIceCandidate(clientId, data);
            break;

        // --- Screen Sharing ---
        case 'screen-share-start':
        case 'screen-share-stop':
        case 'screen-share-frame':
        case 'screen-share-request':
        case 'screen-share-state':
            broadcastToRoom(clientId, message);
            break;
        case 'screen-video-offer':
            handleScreenVideoOffer(clientId, data);
            break;
        case 'screen-video-answer':
            handleScreenVideoAnswer(clientId, data);
            break;
        case 'screen-video-ice':
            handleScreenVideoIce(clientId, data);
            break;

        // --- File Sharing ---
        case 'file-announce':
        case 'file-chunk':
        case 'file-complete':
        case 'file-request':
        case 'file-list-request':
            broadcastToRoom(clientId, message);
            break;
        case 'file-list-response':
            handleFileListResponse(clientId, data);
            break;

        // --- File Presentation ---
        case 'file-present-start':
        case 'file-present-page':
        case 'file-present-navigate':
        case 'file-present-stop':
        case 'file-present-request':
            broadcastToRoom(clientId, message);
            break;

        // --- Recording ---
        case 'recording-status':
            handleRecordingStatus(clientId, data);
            break;
        case 'recording-marker':
            broadcastToRoom(clientId, message);
            break;
        case 'file-present-state':
            handleFilePresentState(clientId, data);
            break;
        case 'pdf-convert-request':
            handlePdfConvertRequest(clientId, data);
            break;
        case 'pdf-page-request':
            handlePdfPageRequest(clientId, data);
            break;

        // --- Default: Broadcast to Room ---
        default:
            const client = clients.get(clientId);
            if (client && client.roomId) {
                broadcastToRoom(clientId, message);
            } else {
                broadcast(message, clientId);
            }
    }
}

// ROOM MANAGEMENT

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

        console.log(`[Room] Created: ${data.roomId}`);

        broadcastRoomList();
        broadcast({
            type: 'room-available',
            senderId: clientId,
            data: JSON.stringify(roomInfo)
        });

    } catch (e) {
        console.error(`[Error] handleRoomAvailable: ${e.message}`);
    }
}

function handleRoomClosed(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);

        if (room && room.hostId === clientId) {
            rooms.delete(data.roomId);
            console.log(`[Room] Closed: ${data.roomId}`);

            broadcast({
                type: 'room-closed',
                senderId: clientId,
                data: JSON.stringify(data)
            });

            broadcastRoomList();
        }

    } catch (e) {
        console.error(`[Error] handleRoomClosed: ${e.message}`);
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

        console.log(`[Room] Join: ${clientId.substring(0, 8)} -> ${data.roomId}`);

        broadcastToRoom(clientId, {
            type: 'room-join',
            senderId: clientId,
            data: JSON.stringify(data)
        });

        // Send recording state to late joiner if recording is in progress
        if (room.recordingState && room.recordingState.isRecording) {
            sendToClient(client.ws, {
                type: 'recording-status',
                senderId: room.recordingState.hostId,
                data: JSON.stringify(room.recordingState)
            });
            console.log(`[Room] Sent recording state to late joiner ${clientId.substring(0, 8)}`);
        }

        broadcastRoomList();

    } catch (e) {
        console.error(`[Error] handleRoomJoin: ${e.message}`);
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

        console.log(`[Room] Leave: ${clientId.substring(0, 8)} <- ${data.roomId}`);

        broadcastToRoom(clientId, {
            type: 'room-leave',
            senderId: clientId,
            data: JSON.stringify(data)
        });

        broadcastRoomList();

    } catch (e) {
        console.error(`[Error] handleRoomLeave: ${e.message}`);
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
        console.error(`[Error] handleRoomUpdate: ${e.message}`);
    }
}

function handleKickPlayer(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const room = rooms.get(data.roomId);

        // Only the host can kick
        if (!room || room.hostId !== clientId) {
            console.warn(`[Kick] Rejected: ${clientId.substring(0, 8)} is not host of ${data.roomId}`);
            return;
        }

        const targetClient = clients.get(data.playerId);
        if (!targetClient || !targetClient.ws) {
            console.warn(`[Kick] Target ${data.playerId?.substring(0, 8)} not found`);
            return;
        }

        // Send kick message to the target player only
        const kickMessage = JSON.stringify({
            type: 'kick-player',
            senderId: clientId,
            data: JSON.stringify(data)
        });
        targetClient.ws.send(kickMessage);

        // Update room state
        targetClient.roomId = null;
        room.playerCount = Math.max(0, room.playerCount - 1);

        // Notify room that this player left
        broadcastToRoom(clientId, {
            type: 'room-leave',
            senderId: data.playerId,
            data: JSON.stringify({ roomId: data.roomId, playerId: data.playerId })
        });

        broadcastRoomList();

        console.log(`[Kick] Host ${clientId.substring(0, 8)} kicked ${data.playerId.substring(0, 8)} from ${data.roomId}`);

    } catch (e) {
        console.error(`[Error] handleKickPlayer: ${e.message}`);
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

            // Stop recording if the recording host leaves
            if (room && room.recordingState && room.recordingState.hostId === clientId) {
                room.recordingState = null;
                broadcastToRoom(clientId, {
                    type: 'recording-status',
                    senderId: clientId,
                    data: JSON.stringify({ isRecording: false, hostId: null })
                });
                console.log(`[Recording] Stopped (host ${clientId.substring(0, 8)} left)`);
            }

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

    console.log(`[Disconnect] Client ${clientId.substring(0, 8)}...`);
}

// WHITEBOARD

function handleWhiteboardState(clientId, dataStr) {
    try {
        const stateData = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        if (stateData.targetId) {
            const targetClient = clients.get(stateData.targetId);
            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                sendToClient(targetClient.ws, {
                    type: 'whiteboard-state',
                    senderId: clientId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });
            }
        } else {
            broadcastToRoom(clientId, {
                type: 'whiteboard-state',
                senderId: clientId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }

    } catch (e) {
        console.error(`[Error] handleWhiteboardState: ${e.message}`);
    }
}

// RECORDING

function handleRecordingStatus(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const client = clients.get(clientId);
        if (!client || !client.roomId) return;

        const room = rooms.get(client.roomId);
        if (room) {
            // Store recording state in room for late joiners
            room.recordingState = {
                isRecording: data.isRecording,
                hostId: data.isRecording ? clientId : null,
                hostName: data.hostName || 'Unknown',
                startTimeUtc: data.startTimeUtc || null
            };

            if (data.isRecording) {
                console.log(`[Recording] Started in room ${client.roomId} by ${data.hostName}`);
            } else {
                console.log(`[Recording] Stopped in room ${client.roomId}`);
            }
        }

        // Broadcast to all clients in the room
        broadcastToRoom(clientId, {
            type: 'recording-status',
            senderId: clientId,
            data: typeof dataStr === 'string' ? dataStr : JSON.stringify(data)
        });

    } catch (e) {
        console.error(`[Error] handleRecordingStatus: ${e.message}`);
    }
}

// WEBRTC SIGNALING (Voice Chat)

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

    } catch (e) {
        console.error(`[Error] handleWebRTCOffer: ${e.message}`);
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

    } catch (e) {
        console.error(`[Error] handleWebRTCAnswer: ${e.message}`);
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
        console.error(`[Error] handleWebRTCIceCandidate: ${e.message}`);
    }
}

// SCREEN SHARING WEBRTC

function handleScreenVideoOffer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) return;

        sendToClient(targetClient.ws, {
            type: 'screen-video-offer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });

    } catch (e) {
        console.error(`[Error] handleScreenVideoOffer: ${e.message}`);
    }
}

function handleScreenVideoAnswer(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { targetId, sdp } = data;

        const targetClient = clients.get(targetId);
        if (!targetClient) return;

        sendToClient(targetClient.ws, {
            type: 'screen-video-answer',
            senderId: senderId,
            data: JSON.stringify({ sdp })
        });

    } catch (e) {
        console.error(`[Error] handleScreenVideoAnswer: ${e.message}`);
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
        console.error(`[Error] handleScreenVideoIce: ${e.message}`);
    }
}

// FILE SHARING

function handleFileListResponse(senderId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        if (data.targetId) {
            const targetClient = clients.get(data.targetId);
            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                sendToClient(targetClient.ws, {
                    type: 'file-list-response',
                    senderId: senderId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });
            }
        } else {
            broadcastToRoom(senderId, {
                type: 'file-list-response',
                senderId: senderId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }

    } catch (e) {
        console.error(`[Error] handleFileListResponse: ${e.message}`);
    }
}

// FILE PRESENTATION

function handleFilePresentState(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;

        if (data.targetId) {
            const targetClient = clients.get(data.targetId);
            if (targetClient && targetClient.ws.readyState === WebSocket.OPEN) {
                sendToClient(targetClient.ws, {
                    type: 'file-present-state',
                    senderId: clientId,
                    data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
                });
            }
        } else {
            broadcastToRoom(clientId, {
                type: 'file-present-state',
                senderId: clientId,
                data: typeof dataStr === 'string' ? dataStr : JSON.stringify(dataStr)
            });
        }

    } catch (e) {
        console.error(`[Error] handleFilePresentState: ${e.message}`);
    }
}

// PDF CONVERSION

async function handlePdfConvertRequest(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { roomId, fileId, requesterId } = data;

        // Use filePresentation module if available
        if (filePresentation && filePresentation.pdfAvailable) {
            await filePresentation.handlePdfConvertRequest(clientId, data, clients, sendToClient);
            return;
        }

        // Check cache
        if (pdfCache.has(fileId)) {
            const cached = pdfCache.get(fileId);
            sendPdfConvertResponse(requesterId, fileId, roomId, {
                success: true,
                totalPages: cached.totalPages
            });
            return;
        }

        // Module not available
        sendPdfConvertResponse(requesterId, fileId, roomId, {
            success: false,
            error: 'PDF conversion not available'
        });

    } catch (e) {
        console.error(`[Error] handlePdfConvertRequest: ${e.message}`);
    }
}

function handlePdfPageRequest(clientId, dataStr) {
    try {
        const data = typeof dataStr === 'string' ? JSON.parse(dataStr) : dataStr;
        const { roomId, fileId, pageNumber, requesterId } = data;

        if (filePresentation && filePresentation.pdfAvailable) {
            filePresentation.handlePdfPageRequest(clientId, data, clients, sendToClient);
            return;
        }

        const cached = pdfCache.get(fileId);
        if (!cached || pageNumber >= cached.pages.length) return;

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

    } catch (e) {
        console.error(`[Error] handlePdfPageRequest: ${e.message}`);
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

// COMMUNICATION UTILITIES

/**
 * Sends a message to a specific client
 */
function sendToClient(ws, message) {
    if (ws && ws.readyState === WebSocket.OPEN) {
        ws.send(JSON.stringify(message));
    }
}

/**
 * Broadcasts a message to all clients (except one)
 */
function broadcast(message, exceptClientId = null) {
    const messageStr = JSON.stringify(message);

    clients.forEach((client, clientId) => {
        if (clientId !== exceptClientId && client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}

/**
 * Broadcasts a message only to clients in the same room
 */
function broadcastToRoom(senderId, message) {
    const sender = clients.get(senderId);
    if (!sender || !sender.roomId) return;

    const roomId = sender.roomId;
    const messageStr = JSON.stringify(message);

    clients.forEach((client, clientId) => {
        if (clientId !== senderId &&
            client.roomId === roomId &&
            client.ws.readyState === WebSocket.OPEN) {
            client.ws.send(messageStr);
        }
    });
}

/**
 * Sends the room list to a client
 */
function sendRoomList(ws) {
    if (!ws || ws.readyState !== WebSocket.OPEN) return;

    const roomList = Array.from(rooms.values());

    sendToClient(ws, {
        type: 'room-list',
        senderId: 'server',
        data: JSON.stringify({ rooms: roomList })
    });
}

/**
 * Broadcasts the room list to all clients
 */
function broadcastRoomList() {
    const roomList = Array.from(rooms.values());

    broadcast({
        type: 'room-list',
        senderId: 'server',
        data: JSON.stringify({ rooms: roomList })
    });
}

/**
 * Sends an error message to a client
 */
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

// SERVER MAINTENANCE

// Heartbeat to detect disconnected clients
const heartbeatInterval = setInterval(() => {
    const now = Date.now();

    wss.clients.forEach((ws) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.ping();
        }
    });

    clients.forEach((client, clientId) => {
        if (now - client.lastHeartbeat > HEARTBEAT_INTERVAL * 2) {
            console.log(`[Timeout] Client ${clientId.substring(0, 8)}...`);
            client.ws.terminate();
            handleDisconnect(clientId);
        }
    });

}, HEARTBEAT_INTERVAL);

// PDF cache cleanup
setInterval(() => {
    const now = Date.now();
    for (const [fileId, entry] of pdfCache) {
        if (now - entry.timestamp > PDF_CACHE_TTL) {
            pdfCache.delete(fileId);
        }
    }
}, 5 * 60 * 1000);

// Periodic status log
setInterval(() => {
    const roomCount = rooms.size;
    const clientCount = clients.size;
    console.log(`[Status] ${clientCount} clients | ${roomCount} rooms`);
}, 60000);

// Graceful shutdown
process.on('SIGINT', () => {
    console.log('\n[Server] Shutting down...');
    clearInterval(heartbeatInterval);

    wss.clients.forEach((ws) => {
        ws.close();
    });

    wss.close(() => {
        console.log('[Server] Goodbye!');
        process.exit(0);
    });
});
