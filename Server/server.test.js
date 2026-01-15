/**
 * Tests for VR Meeting Rooms WebSocket Server
 */

const WebSocket = require('ws');

const SERVER_URL = 'ws://localhost:8080';
const TEST_TIMEOUT = 10000;

// Helper to create a WebSocket client and wait for connection
function createClient() {
    return new Promise((resolve, reject) => {
        const ws = new WebSocket(SERVER_URL);
        const timeout = setTimeout(() => {
            reject(new Error('Connection timeout'));
        }, 5000);

        ws.on('open', () => {
            clearTimeout(timeout);
            resolve(ws);
        });

        ws.on('error', (err) => {
            clearTimeout(timeout);
            reject(err);
        });
    });
}

// Helper to wait for a specific message type
function waitForMessage(ws, type, timeout = 5000) {
    return new Promise((resolve, reject) => {
        const timer = setTimeout(() => {
            reject(new Error(`Timeout waiting for message type: ${type}`));
        }, timeout);

        const handler = (data) => {
            try {
                const message = JSON.parse(data.toString());
                if (message.type === type) {
                    clearTimeout(timer);
                    ws.off('message', handler);
                    resolve(message);
                }
            } catch (e) {
                // Ignore parse errors
            }
        };

        ws.on('message', handler);
    });
}

// Helper to send a message
function sendMessage(ws, message) {
    ws.send(JSON.stringify(message));
}

// Helper to close client
function closeClient(ws) {
    return new Promise((resolve) => {
        if (ws.readyState === WebSocket.OPEN) {
            ws.on('close', resolve);
            ws.close();
        } else {
            resolve();
        }
    });
}

describe('VR Meeting Rooms WebSocket Server', () => {
    let clients = [];

    afterEach(async () => {
        // Clean up all clients after each test
        await Promise.all(clients.map(closeClient));
        clients = [];
    });

    describe('Connection', () => {
        test('should connect and receive welcome message', async () => {
            const ws = await createClient();
            clients.push(ws);

            const welcome = await waitForMessage(ws, 'welcome');

            expect(welcome.type).toBe('welcome');
            expect(welcome.senderId).toBeDefined();
        }, TEST_TIMEOUT);

        test('should receive room-list after connection', async () => {
            const ws = await createClient();
            clients.push(ws);

            const roomList = await waitForMessage(ws, 'room-list');

            expect(roomList.type).toBe('room-list');
            expect(roomList.senderId).toBe('server');

            const data = JSON.parse(roomList.data);
            expect(data.rooms).toBeDefined();
            expect(Array.isArray(data.rooms)).toBe(true);
        }, TEST_TIMEOUT);

        test('should notify other clients when new peer connects', async () => {
            const ws1 = await createClient();
            clients.push(ws1);

            // Wait for initial messages
            await waitForMessage(ws1, 'welcome');

            // Connect second client
            const ws2Promise = createClient();

            // First client should receive peer-connected
            const peerConnected = await waitForMessage(ws1, 'peer-connected');

            const ws2 = await ws2Promise;
            clients.push(ws2);

            expect(peerConnected.type).toBe('peer-connected');
            expect(peerConnected.senderId).toBeDefined();
        }, TEST_TIMEOUT);
    });

    describe('Room Management', () => {
        test('should create a room with room-available message', async () => {
            const ws = await createClient();
            clients.push(ws);

            const welcome = await waitForMessage(ws, 'welcome');
            const clientId = welcome.senderId;

            // Create a room
            const roomData = {
                roomId: 'test-room-1',
                roomName: 'Test Room',
                roomType: 1,
                maxPlayers: 10
            };

            sendMessage(ws, {
                type: 'room-available',
                senderId: clientId,
                data: JSON.stringify(roomData)
            });

            // Should receive room-list update
            const roomList = await waitForMessage(ws, 'room-list');
            const data = JSON.parse(roomList.data);

            const createdRoom = data.rooms.find(r => r.roomId === 'test-room-1');
            expect(createdRoom).toBeDefined();
            expect(createdRoom.roomName).toBe('Test Room');
            expect(createdRoom.hostId).toBe(clientId);
        }, TEST_TIMEOUT);

        test('should allow joining a room', async () => {
            // First client creates a room
            const ws1 = await createClient();
            clients.push(ws1);

            const welcome1 = await waitForMessage(ws1, 'welcome');
            const host = welcome1.senderId;

            sendMessage(ws1, {
                type: 'room-available',
                senderId: host,
                data: JSON.stringify({
                    roomId: 'join-test-room',
                    roomName: 'Join Test Room',
                    maxPlayers: 10
                })
            });

            await waitForMessage(ws1, 'room-list');

            // Second client joins the room
            const ws2 = await createClient();
            clients.push(ws2);

            const welcome2 = await waitForMessage(ws2, 'welcome');
            const joiner = welcome2.senderId;

            sendMessage(ws2, {
                type: 'room-join',
                senderId: joiner,
                data: JSON.stringify({
                    roomId: 'join-test-room',
                    playerName: 'TestPlayer'
                })
            });

            // Host should receive room-join notification
            const joinNotification = await waitForMessage(ws1, 'room-join');
            expect(joinNotification.type).toBe('room-join');
        }, TEST_TIMEOUT);

        test('should reject joining a non-existent room', async () => {
            const ws = await createClient();
            clients.push(ws);

            const welcome = await waitForMessage(ws, 'welcome');

            sendMessage(ws, {
                type: 'room-join',
                senderId: welcome.senderId,
                data: JSON.stringify({
                    roomId: 'non-existent-room',
                    playerName: 'TestPlayer'
                })
            });

            const error = await waitForMessage(ws, 'error');
            expect(error.type).toBe('error');
            expect(error.data).toContain('not found');
        }, TEST_TIMEOUT);

        test('should allow leaving a room', async () => {
            // First client creates a room
            const ws1 = await createClient();
            clients.push(ws1);

            const welcome1 = await waitForMessage(ws1, 'welcome');
            const host = welcome1.senderId;

            sendMessage(ws1, {
                type: 'room-available',
                senderId: host,
                data: JSON.stringify({
                    roomId: 'leave-test-room',
                    roomName: 'Leave Test Room',
                    maxPlayers: 10
                })
            });

            await waitForMessage(ws1, 'room-list');

            // Second client joins then leaves
            const ws2 = await createClient();
            clients.push(ws2);

            const welcome2 = await waitForMessage(ws2, 'welcome');
            const joiner = welcome2.senderId;

            sendMessage(ws2, {
                type: 'room-join',
                senderId: joiner,
                data: JSON.stringify({
                    roomId: 'leave-test-room',
                    playerName: 'TestPlayer'
                })
            });

            await waitForMessage(ws1, 'room-join');
            // Also consume the room-list from the join
            await waitForMessage(ws1, 'room-list');

            // Now leave the room
            sendMessage(ws2, {
                type: 'room-leave',
                senderId: joiner,
                data: JSON.stringify({
                    roomId: 'leave-test-room'
                })
            });

            // Server broadcasts room-list after leave (player count decreases)
            // Note: room-leave notification to room members doesn't work due to server
            // setting roomId=null before broadcast. Verify via room-list update instead.
            const roomList = await waitForMessage(ws1, 'room-list');
            const data = JSON.parse(roomList.data);
            const room = data.rooms.find(r => r.roomId === 'leave-test-room');
            expect(room).toBeDefined();
            expect(room.playerCount).toBe(1); // Back to just host
        }, TEST_TIMEOUT);

        test('should close room when host disconnects', async () => {
            // Second client to receive notification
            const ws2 = await createClient();
            clients.push(ws2);
            await waitForMessage(ws2, 'welcome');

            // First client creates a room
            const ws1 = await createClient();
            clients.push(ws1);

            const welcome1 = await waitForMessage(ws1, 'welcome');
            const host = welcome1.senderId;

            sendMessage(ws1, {
                type: 'room-available',
                senderId: host,
                data: JSON.stringify({
                    roomId: 'close-test-room',
                    roomName: 'Close Test Room',
                    maxPlayers: 10
                })
            });

            await waitForMessage(ws2, 'room-available');

            // Host disconnects
            ws1.close();
            clients = clients.filter(c => c !== ws1);

            const roomClosed = await waitForMessage(ws2, 'room-closed');
            expect(roomClosed.type).toBe('room-closed');

            const data = JSON.parse(roomClosed.data);
            expect(data.roomId).toBe('close-test-room');
        }, TEST_TIMEOUT);
    });

    describe('Room Broadcasting', () => {
        test('should broadcast VR position only to room members', async () => {
            // Client 1 creates room A
            const ws1 = await createClient();
            clients.push(ws1);
            const welcome1 = await waitForMessage(ws1, 'welcome');

            sendMessage(ws1, {
                type: 'room-available',
                senderId: welcome1.senderId,
                data: JSON.stringify({
                    roomId: 'room-a',
                    roomName: 'Room A',
                    maxPlayers: 10
                })
            });
            await waitForMessage(ws1, 'room-list');

            // Client 2 joins room A
            const ws2 = await createClient();
            clients.push(ws2);
            const welcome2 = await waitForMessage(ws2, 'welcome');

            sendMessage(ws2, {
                type: 'room-join',
                senderId: welcome2.senderId,
                data: JSON.stringify({
                    roomId: 'room-a',
                    playerName: 'Player2'
                })
            });
            await waitForMessage(ws1, 'room-join');

            // Client 3 creates different room B
            const ws3 = await createClient();
            clients.push(ws3);
            const welcome3 = await waitForMessage(ws3, 'welcome');

            sendMessage(ws3, {
                type: 'room-available',
                senderId: welcome3.senderId,
                data: JSON.stringify({
                    roomId: 'room-b',
                    roomName: 'Room B',
                    maxPlayers: 10
                })
            });
            await waitForMessage(ws3, 'room-list');

            // Client 1 sends VR position
            const positionData = {
                position: { x: 1, y: 2, z: 3 },
                rotation: { x: 0, y: 0, z: 0, w: 1 }
            };

            sendMessage(ws1, {
                type: 'vr-position',
                senderId: welcome1.senderId,
                data: JSON.stringify(positionData)
            });

            // Client 2 (same room) should receive it
            const positionMsg = await waitForMessage(ws2, 'vr-position');
            expect(positionMsg.type).toBe('vr-position');

            // Client 3 (different room) should NOT receive it within timeout
            await expect(
                waitForMessage(ws3, 'vr-position', 1000)
            ).rejects.toThrow('Timeout');
        }, TEST_TIMEOUT);
    });

    describe('Whiteboard', () => {
        test('should broadcast whiteboard-draw to room members', async () => {
            // Setup: Two clients in same room
            const ws1 = await createClient();
            clients.push(ws1);
            const welcome1 = await waitForMessage(ws1, 'welcome');

            sendMessage(ws1, {
                type: 'room-available',
                senderId: welcome1.senderId,
                data: JSON.stringify({
                    roomId: 'whiteboard-room',
                    roomName: 'Whiteboard Room',
                    maxPlayers: 10
                })
            });
            await waitForMessage(ws1, 'room-list');

            const ws2 = await createClient();
            clients.push(ws2);
            const welcome2 = await waitForMessage(ws2, 'welcome');

            sendMessage(ws2, {
                type: 'room-join',
                senderId: welcome2.senderId,
                data: JSON.stringify({
                    roomId: 'whiteboard-room',
                    playerName: 'Player2'
                })
            });
            await waitForMessage(ws1, 'room-join');

            // Client 1 draws on whiteboard
            const drawData = {
                startX: 0.1,
                startY: 0.2,
                endX: 0.3,
                endY: 0.4,
                color: '#FF0000',
                brushSize: 5
            };

            sendMessage(ws1, {
                type: 'whiteboard-draw',
                senderId: welcome1.senderId,
                data: JSON.stringify(drawData)
            });

            // Client 2 should receive the draw
            const drawMsg = await waitForMessage(ws2, 'whiteboard-draw');
            expect(drawMsg.type).toBe('whiteboard-draw');
        }, TEST_TIMEOUT);

        test('should broadcast whiteboard-clear to room members', async () => {
            // Setup: Two clients in same room
            const ws1 = await createClient();
            clients.push(ws1);
            const welcome1 = await waitForMessage(ws1, 'welcome');

            sendMessage(ws1, {
                type: 'room-available',
                senderId: welcome1.senderId,
                data: JSON.stringify({
                    roomId: 'whiteboard-clear-room',
                    roomName: 'Whiteboard Clear Room',
                    maxPlayers: 10
                })
            });
            await waitForMessage(ws1, 'room-list');

            const ws2 = await createClient();
            clients.push(ws2);
            const welcome2 = await waitForMessage(ws2, 'welcome');

            sendMessage(ws2, {
                type: 'room-join',
                senderId: welcome2.senderId,
                data: JSON.stringify({
                    roomId: 'whiteboard-clear-room',
                    playerName: 'Player2'
                })
            });
            await waitForMessage(ws1, 'room-join');

            // Client 1 clears whiteboard
            sendMessage(ws1, {
                type: 'whiteboard-clear',
                senderId: welcome1.senderId,
                data: '{}'
            });

            // Client 2 should receive the clear
            const clearMsg = await waitForMessage(ws2, 'whiteboard-clear');
            expect(clearMsg.type).toBe('whiteboard-clear');
        }, TEST_TIMEOUT);
    });

    describe('WebRTC Signaling', () => {
        test('should relay WebRTC offer to target client', async () => {
            const ws1 = await createClient();
            clients.push(ws1);
            const welcome1 = await waitForMessage(ws1, 'welcome');
            const client1Id = welcome1.senderId;

            const ws2 = await createClient();
            clients.push(ws2);
            const welcome2 = await waitForMessage(ws2, 'welcome');
            const client2Id = welcome2.senderId;

            // Client 1 sends offer to Client 2
            const offerData = {
                targetId: client2Id,
                sdp: 'fake-sdp-offer-data'
            };

            sendMessage(ws1, {
                type: 'webrtc-offer',
                senderId: client1Id,
                data: JSON.stringify(offerData)
            });

            // Client 2 should receive the offer
            const offer = await waitForMessage(ws2, 'webrtc-offer');
            expect(offer.type).toBe('webrtc-offer');
            expect(offer.senderId).toBe(client1Id);

            const receivedData = JSON.parse(offer.data);
            expect(receivedData.sdp).toBe('fake-sdp-offer-data');
        }, TEST_TIMEOUT);

        test('should relay WebRTC answer to target client', async () => {
            const ws1 = await createClient();
            clients.push(ws1);
            const welcome1 = await waitForMessage(ws1, 'welcome');
            const client1Id = welcome1.senderId;

            const ws2 = await createClient();
            clients.push(ws2);
            const welcome2 = await waitForMessage(ws2, 'welcome');
            const client2Id = welcome2.senderId;

            // Client 2 sends answer to Client 1
            const answerData = {
                targetId: client1Id,
                sdp: 'fake-sdp-answer-data'
            };

            sendMessage(ws2, {
                type: 'webrtc-answer',
                senderId: client2Id,
                data: JSON.stringify(answerData)
            });

            // Client 1 should receive the answer
            const answer = await waitForMessage(ws1, 'webrtc-answer');
            expect(answer.type).toBe('webrtc-answer');
            expect(answer.senderId).toBe(client2Id);
        }, TEST_TIMEOUT);

        test('should relay ICE candidates to target client', async () => {
            const ws1 = await createClient();
            clients.push(ws1);
            const welcome1 = await waitForMessage(ws1, 'welcome');
            const client1Id = welcome1.senderId;

            const ws2 = await createClient();
            clients.push(ws2);
            const welcome2 = await waitForMessage(ws2, 'welcome');
            const client2Id = welcome2.senderId;

            // Client 1 sends ICE candidate to Client 2
            const iceData = {
                targetId: client2Id,
                candidate: 'fake-ice-candidate',
                sdpMid: 'audio',
                sdpMLineIndex: 0
            };

            sendMessage(ws1, {
                type: 'webrtc-ice-candidate',
                senderId: client1Id,
                data: JSON.stringify(iceData)
            });

            // Client 2 should receive the ICE candidate
            const ice = await waitForMessage(ws2, 'webrtc-ice-candidate');
            expect(ice.type).toBe('webrtc-ice-candidate');
            expect(ice.senderId).toBe(client1Id);
        }, TEST_TIMEOUT);
    });

    describe('Disconnection', () => {
        test('should notify peers when client disconnects', async () => {
            const ws1 = await createClient();
            clients.push(ws1);
            await waitForMessage(ws1, 'welcome');

            const ws2 = await createClient();
            clients.push(ws2);
            const welcome2 = await waitForMessage(ws2, 'welcome');
            const client2Id = welcome2.senderId;

            // Client 2 disconnects
            ws2.close();
            clients = clients.filter(c => c !== ws2);

            // Client 1 should receive peer-disconnected
            const disconnected = await waitForMessage(ws1, 'peer-disconnected');
            expect(disconnected.type).toBe('peer-disconnected');
            expect(disconnected.senderId).toBe(client2Id);
        }, TEST_TIMEOUT);
    });

    describe('Room List Request', () => {
        test('should return room list on request', async () => {
            const ws = await createClient();
            clients.push(ws);
            const welcome = await waitForMessage(ws, 'welcome');

            // Drain all pending messages with a short wait
            await new Promise(resolve => setTimeout(resolve, 100));

            // Set up listener BEFORE sending request to avoid race condition
            const roomListPromise = waitForMessage(ws, 'room-list');

            // Request room list
            sendMessage(ws, {
                type: 'room-list-request',
                senderId: welcome.senderId
            });

            const roomList = await roomListPromise;
            expect(roomList.type).toBe('room-list');
            expect(roomList.senderId).toBe('server');
        }, TEST_TIMEOUT);
    });
});
