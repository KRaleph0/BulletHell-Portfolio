require('dotenv').config();
const { WebSocketServer } = require('ws');
const jwt   = require('jsonwebtoken');
const axios = require('axios');
const { v4: uuidv4 } = require('uuid');
const rooms = require('./rooms');

const JWT_SECRET = process.env.JWT_SECRET;
const API_URL    = process.env.API_URL || 'http://localhost:8000';
const WS_PORT    = parseInt(process.env.WS_PORT || '3000');

if (!JWT_SECRET) throw new Error('[WS] JWT_SECRET is not set — server cannot start safely');

function verifyToken(token) {
    try {
        const payload = jwt.verify(token, JWT_SECRET);
        return { userId: payload.user_id, username: payload.sub };
    } catch (e) { return null; }
}

async function saveResult(winnerId, loserId, roomId) {
    try {
        await axios.post(`${API_URL}/game/result`, { winnerId, loserId, roomId }, { timeout: 5000 });
    } catch (e) {
        console.error('[WS] saveResult failed:', e.message);
    }
}

function start() {
    const wss = new WebSocketServer({ port: WS_PORT });
    console.log(`[WS] Server on :${WS_PORT}`);

    wss.on('connection', (ws, req) => {
        // strip query string before extracting roomId
        const pathname = (req.url || '').split('?')[0];
        const roomId   = pathname.split('/').pop();
        if (!roomId) { ws.close(); return; }

        let playerId = null;
        let username = null;
        let userId   = null;

        ws.send(JSON.stringify({ type: 'CONNECTED' }));

        ws.on('message', async (raw) => {
            try {
                let msg;
                try { msg = JSON.parse(raw); } catch (e) { return; }
                const { type } = msg;

                // ── JOIN ────────────────────────────────────────────────
                if (type === 'JOIN') {
                    if (playerId) return; // 중복 JOIN 방지

                    const auth = verifyToken(msg.token || '');
                    if (!auth) {
                        ws.send(JSON.stringify({ type: 'ERROR', code: 'AUTH_FAILED' }));
                        ws.close();
                        return;
                    }

                    const room = rooms.getOrCreate(roomId);
                    if (room.players.length >= 2) {
                        ws.send(JSON.stringify({ type: 'ERROR', code: 'ROOM_FULL' }));
                        ws.close();
                        return;
                    }
                    if (room.state === 'playing') {
                        ws.send(JSON.stringify({ type: 'ERROR', code: 'GAME_ALREADY_STARTED' }));
                        ws.close();
                        return;
                    }

                    playerId = uuidv4();
                    userId   = auth.userId;
                    username = auth.username;

                    const playerIndex = room.players.length + 1;
                    const isHost      = playerIndex === 1;
                    room.players.push({ id: playerId, ws, userId, username, index: playerIndex, isHost, isReady: false });

                    ws.send(JSON.stringify({ type: 'JOINED', playerId, playerIndex, isHost }));
                    rooms.broadcast(room, rooms.roomState(room));
                    return;
                }

                if (!playerId) return;

                // ── READY_TOGGLE ────────────────────────────────────────
                if (type === 'READY_TOGGLE') {
                    const room = rooms.get(roomId);
                    if (!room) return;
                    const p = room.players.find(x => x.id === playerId);
                    if (p) p.isReady = !p.isReady;
                    rooms.broadcast(room, rooms.roomState(room));
                    return;
                }

                // ── START_GAME ──────────────────────────────────────────
                if (type === 'START_GAME') {
                    const room = rooms.get(roomId);
                    if (!room) return;
                    const requester = room.players.find(p => p.id === playerId);
                    if (!requester || !requester.isHost) return;
                    if (room.players.length !== 2) return;
                    if (!room.players.every(p => p.isReady)) return;

                    room.state = 'playing';
                    rooms.broadcast(room, { type: 'GAME_STARTING' });
                    setTimeout(() => {
                        const r = rooms.get(roomId);
                        if (r && r.players.length) rooms.broadcast(r, { type: 'GAME_START' });
                    }, 3000);
                    return;
                }

                // ── CHAT ────────────────────────────────────────────────
                if (type === 'CHAT') {
                    const room = rooms.get(roomId);
                    if (room) rooms.broadcast(room, { type: 'CHAT', username, text: msg.text || '' });
                    return;
                }

                // ── PLAYER_DIED ─────────────────────────────────────────
                if (type === 'PLAYER_DIED') {
                    const room = rooms.get(roomId);
                    if (!room || room.state !== 'playing') return;
                    room.state = 'ended';

                    const loser  = room.players.find(p => p.id === playerId);
                    const winner = room.players.find(p => p.id !== playerId);
                    rooms.broadcast(room, {
                        type:     'GAME_OVER',
                        winnerId: winner ? winner.id : null,
                        loserId:  loser  ? loser.id  : null,
                    });
                    if (winner && loser) await saveResult(winner.userId, loser.userId, roomId);
                }
            } catch (err) {
                console.error('[WS] handler error:', err.message);
            }
        });

        ws.on('close', () => {
            if (!playerId) return;
            rooms.removePlayer(roomId, playerId);
            const room = rooms.get(roomId);
            if (room && room.players.length) rooms.broadcast(room, { type: 'OPPONENT_DISCONNECTED' });
        });

        ws.on('error', (err) => console.error('[WS] socket error:', err.message));
    });
}

module.exports = { start };
