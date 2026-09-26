require('dotenv').config();
const dgram = require('dgram');
const rooms = require('./rooms');

const UDP_PORT   = parseInt(process.env.UDP_PORT   || '7777');
const RATE_LIMIT = parseInt(process.env.RATE_LIMIT || '200');
const TIMEOUT_MS = parseInt(process.env.TIMEOUT_MS || '10000');

// playerId → { address, port, lastSeen, count, windowStart }
const clients = new Map();

function start() {
    const sock = dgram.createSocket('udp4');

    sock.on('message', (buf, rinfo) => {
        let msg;
        try { msg = JSON.parse(buf); } catch { return; }

        const { playerId, roomId } = msg;
        if (!playerId || !roomId) return;

        const now = Date.now();

        // Register or update client UDP endpoint
        let client = clients.get(playerId);
        if (!client) {
            client = { address: rinfo.address, port: rinfo.port, lastSeen: now, count: 0, windowStart: now };
            clients.set(playerId, client);
        } else {
            client.address  = rinfo.address;
            client.port     = rinfo.port;
            client.lastSeen = now;
        }

        // Rate limit: max RATE_LIMIT packets per second per client
        if (now - client.windowStart >= 1000) {
            client.windowStart = now;
            client.count = 0;
        }
        client.count++;
        if (client.count > RATE_LIMIT) return;

        // Find opponent in same room via shared rooms state
        const room = rooms.get(roomId);
        if (!room) return;

        const opponent = room.players.find(p => p.id !== playerId);
        if (!opponent) return;

        const oppClient = clients.get(opponent.id);
        if (!oppClient) return;

        sock.send(buf, oppClient.port, oppClient.address, (err) => {
            if (err) console.error('[UDP] send error:', err.message);
        });
    });

    sock.on('error', (err) => console.error('[UDP] error:', err.message));

    sock.bind(UDP_PORT, () => console.log(`[UDP] Relay on :${UDP_PORT}`));

    // Evict stale sessions (no packet for TIMEOUT_MS ms)
    setInterval(() => {
        const cutoff = Date.now() - TIMEOUT_MS;
        for (const [id, c] of clients) {
            if (c.lastSeen < cutoff) clients.delete(id);
        }
    }, TIMEOUT_MS);
}

module.exports = { start };
