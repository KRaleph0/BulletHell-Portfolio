# Relay Server

Unity 클라이언트 간 대기방 이벤트(WebSocket)와 실시간 위치·탄막(UDP)을 중계하는 Node.js 서버입니다.
AWS Lightsail에서 PM2로 운영했습니다. 프로토콜은 [ARCHITECTURE](../docs/ARCHITECTURE.md)를 참고하세요.

| 파일 | 역할 |
|---|---|
| `src/wsServer.js` | 방 입장(JWT 검증), 준비·시작·채팅·사망 처리, 승패를 API 서버에 저장 |
| `src/rooms.js` | 인메모리 방 상태, 방장 승계, 브로드캐스트 |
| `src/udpRelay.js` | 같은 방 상대에게 UDP 패킷 포워딩, 초당 패킷 제한, 세션 만료 |

## 실행

Node.js 18 이상

```bash
npm install
cp .env.example .env   # JWT_SECRET, API_URL 설정
npm start
```

PM2로 운영할 때:

```bash
pm2 start ecosystem.config.js
```

방화벽에서 TCP 3000(WebSocket), UDP 7777을 열어야 합니다.
