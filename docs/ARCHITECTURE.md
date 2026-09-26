# 네트워크 아키텍처

## 구성

```
Unity 클라이언트
   │
   ├─ HTTPS ─────────────► API 서버 (FastAPI, 자택 서버 / Docker)
   │                        회원가입·로그인(JWT) · 방 목록/생성/입장 · 승패 저장 · 랭킹
   │
   ├─ WebSocket :3000 ───► 릴레이 서버 (Node.js, AWS Lightsail)
   │                        대기방 · 준비 · 게임 시작 · 사망 · 게임 종료
   │
   └─ UDP :7777 ─────────► 릴레이 서버 ──► 상대 클라이언트
                            위치(20Hz) · 탄막 발사 · 피격 · HP
```

| 트래픽 | 프로토콜 | 이유 |
|---|---|---|
| 인증, 방 관리 | HTTPS (REST) | 요청-응답 구조, DB 접근 필요 |
| 대기방, 게임 이벤트 | WebSocket | 순서와 도착이 보장되어야 함 |
| 위치, 탄막, HP | UDP | 빈도가 높고, 늦게 온 값은 버려도 됨 |

API 서버를 자택 서버에 두고 지연에 민감한 릴레이만 클라우드에 올렸습니다. 릴레이는 API 서버와 같은 JWT 시크릿으로 토큰을 직접 검증하므로, 입장할 때마다 API 서버를 거치지 않습니다.

## 게임 진행 흐름

1. **로그인** — `POST /auth/login` → JWT 발급, `PlayerPrefs`에 저장
2. **방 입장** — `POST /rooms/{roomId}/join` → 릴레이 주소·포트 수신
3. **대기방** — `ws://<relay>:3000/ws/{roomId}` 접속 후 `JOIN`(토큰) → `JOINED`(세션 ID, 순번, 방장 여부)
4. **게임 시작** — 방장이 `START_GAME` → 두 명 모두 준비 상태면 `GAME_STARTING` → 3초 후 `GAME_START`
5. **대전** — UDP로 위치·탄막·피격 송수신
6. **종료** — 죽은 쪽이 `PLAYER_DIED` → 릴레이가 `GAME_OVER` 브로드캐스트 후 API 서버에 승패 저장

## WebSocket 메시지

| 방향 | 타입 | 내용 |
|---|---|---|
| C→S | `JOIN` | `token`, `roomId` |
| C→S | `READY_TOGGLE` | 준비 상태 토글 |
| C→S | `START_GAME` | 게임 시작 요청 (방장만, 2명 모두 준비 시) |
| C→S | `CHAT` | `text` |
| C→S | `PLAYER_DIED` | 로컬 플레이어 사망 |
| S→C | `JOINED` | `playerId`(세션 UUID), `playerIndex`, `isHost` |
| S→C | `PLAYER_STATE` | 방 인원 전체의 이름·준비·방장 여부 |
| S→C | `CHAT` | `username`, `text` |
| S→C | `GAME_STARTING` / `GAME_START` | 카운트다운 시작 / 게임 시작 |
| S→C | `GAME_OVER` | `winnerId`, `loserId` |
| S→C | `OPPONENT_DISCONNECTED` | 상대 연결 끊김 |
| S→C | `ERROR` | `AUTH_FAILED`, `ROOM_FULL`, `GAME_ALREADY_STARTED` |

방 상태는 `lobby → playing → ended`로만 진행하며, 방장이 나가면 남은 플레이어가 방장을 이어받습니다.

## UDP 패킷

모든 패킷은 JSON이고 `type`, `seq`, `playerId`, `roomId`를 공통으로 가집니다. 릴레이는 `playerId`와 `roomId`만 읽고 나머지는 그대로 전달합니다.

| 타입 | 주요 필드 | 설명 |
|---|---|---|
| `POS` | `x`, `z`, `rot` | 50ms마다 전송, 수신 측은 보간 이동 |
| `BULLET` | 위치, 방향, `pattern`, `timestamp`, `speed`, `mode`, 가속·곡선·부채꼴 파라미터 | 발사 이벤트 1회 = 패킷 1개 |
| `HIT` | `damage`, `hp`, `maxHp` | 피격자가 전송, `hp`로 상대 화면의 HP 보정 |
| `HP` | `hp`, `maxHp` | 회복 등 피격 외 HP 변화 |

수신 측은 타입별 마지막 `seq`보다 작거나 같은 패킷을 버립니다.

## 데드 레커닝

```
latency = 수신 시각 - 패킷의 timestamp      (0 ~ 0.5초로 제한)

일반 탄:   거리 = speed × latency
크러셔:    거리 = min(latency, accelDelay) × 초기속도
               + max(0, latency - accelDelay) × 가속 후 속도
```

탄을 이 거리만큼 앞에서 생성하고 남은 수명도 그만큼 줄입니다. 크러셔는 가속 시점이 이미 지났으면 가속 상태로 시작합니다.

## 릴레이 서버

- `wsServer.js` — 방 입장·준비·시작·채팅·사망 처리, JWT 로컬 검증, 결과를 API 서버에 저장
- `rooms.js` — 인메모리 방 상태, 방장 승계, 브로드캐스트
- `udpRelay.js` — 발신 주소 등록, 같은 방 상대에게 포워딩, 초당 패킷 수 제한, 10초 무응답 세션 정리

## 한계와 개선 방향

- **시계 동기화** — 지연 시간을 두 클라이언트의 시스템 시각 차이로 계산하므로 시계가 어긋나면 보정값도 어긋납니다. 0.5초 상한으로 피해를 줄였지만, 서버 기준 시각을 맞추는 핑 교환이 더 정확합니다.
- **클라이언트 판정 신뢰** — 피격과 사망을 클라이언트가 보고하므로 조작된 클라이언트를 막지 못합니다.
- **인메모리 방 상태** — 릴레이가 재시작되면 진행 중인 방이 사라지고 API 서버의 방 상태와 어긋날 수 있습니다. 그래서 대기방 로직을 API 서버로 옮기고, 릴레이는 nginx 리버스 프록시(Tailscale 경유)와 UDP 포워딩만 맡는 구조로 바꾸는 작업을 설계했습니다. 경량화된 UDP 릴레이에는 첫 발신 IP 고정과 1400바이트 초과 패킷 차단도 넣을 계획이었습니다.
