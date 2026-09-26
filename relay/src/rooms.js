// roomId → { id, players: [], state: 'lobby'|'playing'|'ended' }
const rooms = new Map();

function getOrCreate(roomId) {
    if (!rooms.has(roomId)) rooms.set(roomId, { id: roomId, players: [], state: 'lobby' });
    return rooms.get(roomId);
}

function get(roomId) { return rooms.get(roomId) || null; }

function removePlayer(roomId, playerId) {
    const room = rooms.get(roomId);
    if (!room) return null;
    const idx = room.players.findIndex(p => p.id === playerId);
    if (idx === -1) return null;
    const [left] = room.players.splice(idx, 1);
    if (room.players.length === 0) {
        rooms.delete(roomId);
    } else if (left.isHost) {
        room.players[0].isHost = true;
    }
    return left;
}

function roomState(room) {
    return {
        type: 'PLAYER_STATE',
        players: room.players.map(p => ({
            playerId: p.id,
            username: p.username,
            isReady:  p.isReady,
            isHost:   p.isHost,
            index:    p.index,
        })),
    };
}

function broadcast(room, data) {
    const msg = JSON.stringify(data);
    for (const p of room.players) {
        if (p.ws.readyState === 1 /* OPEN */) p.ws.send(msg);
    }
}

module.exports = { getOrCreate, get, removePlayer, roomState, broadcast };
