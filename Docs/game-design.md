# 게임 디자인

## 레퍼런스

[마블 스파이더 베놈 캡틴아메리카 공룡 티렉스 Vs. 바이오 티렉스 공룡 대결](https://youtu.be/XEvputtBXZQ)
— **Animal Revolt Battle Simulator** 플레이 영상.

영상에서 가져올 것:
- 팀을 구성해 크리처를 배치하고, 전투 시작 후에는 **관전**하는 구조
- 물리 기반 몸싸움 — 큰 공룡이 작은 공룡을 밀어내는 무게감
- "T-Rex vs Bio T-Rex" 처럼 **대결 카드**가 콘텐츠의 핵심

가져오지 않을 것:
- 마블 캐릭터 (저작권 침해 — [`legal.md`](legal.md) 참고)

## 핵심 루프

```
배치 (Placement)  →  전투 (Fighting)  →  결과 (Finished)
     ↑                                        │
     └────────────── REMATCH ─────────────────┘
```

1. **배치** — 예산(팀당 기본 1000) 안에서 RED / BLUE 양 팀에 공룡을 드롭.
   시간은 멈춰 있고, 자리 겹침과 예산 초과는 배치 단계에서 막힙니다.
2. **전투** — 모든 공룡의 `CreatureBrain.CombatEnabled = true`. 플레이어는 카메라와 배속만 조작.
3. **결과** — 한쪽이 전멸하면 종료. 양쪽 동시 전멸은 DRAW.

**플레이어는 전투 중 아무것도 조작하지 않습니다.** 이게 이 장르의 정체성입니다 —
재미는 "이 조합이 이길까?"를 예측하고 검증하는 데서 나옵니다.

## AI 상태 머신

[`CreatureBrain`](../Assets/Scripts/Units/CreatureBrain.cs):

```
Idle ──(적 발견)──> Seek ──(사거리 진입)──> Attack
 ↑                    ↑                        │
 │                    └───(타깃 사망)───────────┘
 └──(적 없음)─────────┘
                     Dead (사망 시 어디서든)
```

의도적인 설계 결정:

- **타깃 유지** — 현재 타깃이 죽을 때까지 바꾸지 않습니다. 매 프레임 최근접 적으로
  갈아타면 공룡들이 우왕좌왕해서 전투가 우스워집니다.
- **리타깃 스태거** — `retargetInterval`(0.4초)을 크리처마다 랜덤 오프셋으로 시작해
  100마리가 같은 프레임에 스캔하는 것을 방지합니다.
- **NavMesh 미사용** — 직접 스티어링. 아레나 지오메트리를 바꿔도 베이크가 필요 없습니다.
  장애물이 있는 맵을 만들 때 재검토 대상입니다.
- **어택 윈드업** — 사거리 진입 즉시 데미지가 들어가지 않고 `attackWindup` 만큼 지연됩니다.
  애니메이션과 타격이 맞아떨어져야 물기가 "연결된" 것처럼 보입니다.

## 스탯 모델

모든 밸런스 수치는 [`CreatureDefinition`](../Assets/Scripts/Data/CreatureDefinition.cs)
ScriptableObject에 있습니다. **프리팹에는 밸런스 수치를 두지 않습니다** —
`CreatureUnit.Initialize()` 가 스폰 시점에 definition → 컴포넌트로 흘려줍니다.

| 스탯 | 역할 |
|---|---|
| `maxHealth` / `armor` | 생존력. armor는 피격당 고정 감산, 최소 1은 항상 관통 |
| `attackDamage` / `attackInterval` | DPS. `DamagePerSecond` 프로퍼티로 계산됨 |
| `attackRange` / `attackWindup` | 리치와 타이밍. 큰 공룡은 리치가 길고 느립니다 |
| `moveSpeed` / `turnSpeedDegrees` | 기동력. 무거운 공룡은 회전이 느려 랩터에게 측면을 내줍니다 |
| `mass` | 물리 몸싸움. 무게 차이가 넉백/밀림으로 나타납니다 |
| `aggroRange` | 탐지 거리 |
| `cost` | 배치 예산 소모량 |

### 이동 — Reynolds steering behaviors

순수 Seek 하나로는 무리가 한 줄로 접근한 뒤 제자리에서 때리기만 했습니다.
현재는 [Craig Reynolds의 steering behaviors](https://www.gamedeveloper.com/design/introduction-to-steering-behaviours)(1999)를
가중 합성합니다 — [`SteeringBehaviors.cs`](../Assets/Scripts/Units/SteeringBehaviors.cs).

| 동작 | 역할 |
|---|---|
| **Arrive** | 목표 근처에서 감속. 순수 Seek는 오버슈트 후 되돌아오며 진동합니다 |
| **Pursue** | 타깃의 미래 위치를 겨냥. 현재 위치를 쫓으면 영원히 뒤를 따릅니다 |
| **Separation** | 이웃을 밀어냄. 없으면 무리가 한 점으로 무너집니다 |

**Separation은 접근용과 교전용 가중치가 다릅니다.** 접근 중에는 무리를 흩어 여러 방향에서 오게 하지만,
접촉 후에도 유지하면 서로 밀어내 "정중한 거리"가 생깁니다. 레퍼런스 영상에서는 공격자들이
몸을 깊게 겹친 채 쌓여 싸웁니다.

### 사거리는 몸통 간 거리입니다

`attackRange` 는 **루트 대 루트** 거리이며, 여기에 **상대의 `footprintRadius` 가 더해집니다**
([`MeleeAttack.EffectiveRange`](../Assets/Scripts/Units/MeleeAttack.cs)).

두 가지를 방지합니다:
- aim점끼리 재면 선회 중 서로 등을 돌릴 때 거리가 오히려 멀어져 영영 사거리에 못 듭니다
- 중심 간 거리만 쓰면 랩터(사거리 1.8)가 몸길이 5인 T-Rex를 때리려면 그 몸 안으로 들어가야 합니다

### 체급 상호작용

작은 개체는 큰 개체를 **기어올라 물어뜯습니다** ([`PounceCling`](../Assets/Scripts/Units/PounceCling.cs)).
질량비 4배 이상일 때 발동하고, 호스트의 `Shoulders / Torso / Back / Neck / Hips` 본에
최대 5마리가 서로 다른 자리에 매달립니다 ([`ClingAnchors`](../Assets/Scripts/Units/ClingAnchors.cs)).
일정 시간 후 호스트가 털어냅니다.

### 초기 밸런스 (플레이스홀더)

[`SampleContentBuilder`](../Assets/Editor/SampleContentBuilder.cs) 의 `Blueprints` 배열:

| 공룡 | 코스트 | HP | 방어 | 데미지 | 간격 | 리치 | 속도 | 질량 |
|---|---|---|---|---|---|---|---|---|
| T-Rex | 420 | 4200 | 12 | 480 | 1.6 | 5.0 | 6.5 | 8000 |
| Bio T-Rex | 520 | 4800 | 20 | 560 | 1.5 | 5.2 | 7.0 | 8600 |
| Spinosaurus | 380 | 3600 | 8 | 420 | 1.5 | 4.6 | 6.8 | 6800 |
| Triceratops | 300 | 4400 | 22 | 300 | 1.9 | 3.8 | 5.8 | 7400 |
| Velociraptor | 90 | 600 | 2 | 110 | 0.7 | 2.4 | 11.0 | 900 |
| Ankylosaurus | 320 | 5200 | 30 | 260 | 2.2 | 3.4 | 4.6 | 8200 |

의도한 가위바위보:

- **랩터 무리 vs 단일 대형** — 랩터 4마리(360)가 T-Rex(420)를 둘러싸면 이깁니다.
  대형 공룡의 느린 회전이 약점.
- **Ankylosaurus vs 랩터** — 방어 30이 랩터 데미지 110을 크게 깎아 무리 전술을 무력화.
- **Bio T-Rex** — 정직하게 강한 대신 코스트가 비쌉니다. 영상의 메인 이벤트 카드.

밸런스는 실제로 붙여보고 조정할 대상입니다. 배속 x4로 같은 매치를 여러 번 돌리세요.

## 팀과 진영

- `Team.Red` / `Team.Blue`, `Team.Neutral`(소품용)
- 팀 색상은 [`CreatureSpawner`](../Assets/Scripts/Core/CreatureSpawner.cs) 에서 지정하고
  스폰 시 머티리얼 `_BaseColor` / `_Color` 로 틴트합니다
- 배치 시 공룡은 아레나 중앙을 향하도록 자동 회전 — 시작하자마자 서로를 보고 있게

## 카메라

관전이 게임플레이의 절반이므로 카메라가 1급 시스템입니다.
[`OrbitCameraController`](../Assets/Scripts/CameraRig/OrbitCameraController.cs):

| 입력 | 동작 |
|---|---|
| 한 손가락 드래그 / 좌클릭 드래그 | 궤도 회전 |
| 두 손가락 핀치 / 휠 | 줌 |
| 두 손가락 드래그 / 중클릭 드래그 | 팬 |

`Time.unscaledDeltaTime` 으로 스무딩하기 때문에 배속 0.25x나 일시정지 중에도
카메라 조작감이 일정합니다.

## 앞으로의 방향

[`roadmap.md`](roadmap.md) 참고.
