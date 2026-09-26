require('dotenv').config();
const wsServer  = require('./wsServer');
const udpRelay  = require('./udpRelay');

wsServer.start();
udpRelay.start();
