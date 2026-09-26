module.exports = {
  apps: [{
    name:   'bullethell-relay',
    script: 'src/index.js',
    env: {
      NODE_ENV: 'production',
      WS_PORT:  3000,
      UDP_PORT: 7777,
    }
  }]
};
