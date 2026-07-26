# 건틀릿 모드 (계단 아레나) — 설계

길쭉한 판형 아레나가 계단으로 이어지고, 각 계단에 몬스터가 대기합니다. 플레이어가 출발시킨
공룡들이 계단을 하나씩 올라가며 싸우고, 마지막 계단에는 보스가 있습니다. 공룡이 전멸하면
버튼을 눌러 다음 무리를 출발시킵니다. 위로 갈수록 어려워집니다.

기존 대전 모드와는 **배치 화면 상단의 토글**로 전환합니다.

---

## 0. 요구사항 확인 — 하나 애매합니다

> "각 계단은 10개로 이루어져 있음"

**계단마다 몬스터 10마리**로 읽었습니다. 바로 다음 문장이 "해당 계단에는 몬스터들이 대기"라서
자연스러운 독해이고, "계단이 총 10개"였다면 "각"이 붙지 않았을 겁니다.

다만 **둘 다 설정값으로 둡니다** — 계단 수와 계단당 마릿수 모두 데이터로 뺍니다. 그러면 제가
잘못 읽었더라도 asset 한 줄이지 재설계가 아닙니다. 기본값은 계단 10개 × 마리수는 층별 곡선
(아래 4.1). 의도가 반대였다면 알려주세요.

---

## 1. 지금 구조에서 깨지는 것

이 섹션이 이 문서에서 제일 중요합니다. **설계보다 먼저 읽으세요** — 아래 7개 중 첫 번째를
해결하지 못하면 나머지 설계는 전부 무의미합니다.

### 1.1 공룡은 계단을 못 올라갑니다 ⚠️ 최대 위험

`CreatureLocomotion.ApplySteering()` (`:148`):

```csharp
Vector3 change = new(desiredVelocity.x - current.x, 0f, desiredVelocity.z - current.z);
body.AddForce(Vector3.ClampMagnitude(change, moveSpeed) * acceleration, ForceMode.Acceleration);
```

- **수직 성분이 없습니다.** y는 항상 0입니다. 추진력은 전부 수평이고, 높이는 중력과 콜라이더가
  지형을 타고 넘는 것에만 의존합니다.
- 바로 위에 `if (!IsGrounded) return;` 이 있습니다. 접지 판정이 끊기면 **조향이 통째로 멈춥니다.**
- `IsGrounded` 는 `transform.position + up*0.1` 에서 아래로 1.3유닛 레이캐스트입니다.
- **물리 콜라이더는 의도적으로 작습니다** — 보이는 몸의 약 1/4 (`CreatureImpact` 주석 참고).
  캡슐이 작을수록 넘을 수 있는 단차도 작아집니다.
- `NavMesh 미사용` 은 명시된 설계 결정입니다 (`CLAUDE.md`).

즉 **말 그대로의 계단(수직 면)은 물리적으로 못 올라갑니다.** 랩터는 0.3유닛 턱에도 걸릴 수
있습니다. 이건 튜닝 문제가 아니라 구조 문제입니다.

### 1.2 aggroRange 80 — 1층 공룡이 10층 보스를 노립니다

`CreatureDefinition.aggroRange` 기본값은 **80**이고 (`:45`), 현재 아레나 반지름은 **22**입니다.
지금까지는 "아레나 전체"라는 뜻이었으니 문제가 안 됐습니다. 길이 200유닛짜리 판형에서는
1층 공룡이 7층 몬스터를 최근접으로 집어 들고 그쪽으로 직진합니다.

`UnitRegistry.FindNearestEnemy` 는 팀 리스트를 전수 순회할 뿐 층 개념이 없습니다.

### 1.3 UnitRegistry에 층이 없습니다

`Team` 3개(Red/Blue/Neutral) 키의 static 딕셔너리입니다. "지금 교전 중인 층"을 표현할 자리가
없습니다. 그리고 **static이라 씬 로드보다 오래 삽니다** — 모드 전환 때 `Clear()` 를 빠뜨리면
이전 모드의 유령이 남습니다 (`CLAUDE.md` 에 이미 적힌 함정).

### 1.4 "한 팀 전멸 = 경기 종료" 가 하드코딩

`BattleManager.HandleUnitDied` (`:242`):

```csharp
if (UnitRegistry.AliveCount(Team.Red) > 0 && UnitRegistry.AliveCount(Team.Blue) > 0) return;
awaitingVerdict = true;
```

건틀릿에서 아군 전멸은 **끝이 아니라 다음 웨이브 대기**입니다. 몬스터 쪽도 한 층 비었다고
끝이 아닙니다.

### 1.5 BattlePhase 3개로는 부족합니다

`Placement / Fighting / Finished` (`GameEnums.cs:12`). "전멸했고 다음 무리를 기다리는 중"이
없습니다. **다만 enum에 값을 추가하는 건 위험합니다** — `BattleHUD`, `BattleMusic`,
`BattleCameraDirector`, `VictoryDance` 가 전부 이 값으로 분기하고, 새 값은 어디서도 처리되지
않은 채 조용히 지나갑니다. 설계(3.3)에서 다르게 풀었습니다.

### 1.6 카메라가 원형 아레나를 가정합니다

- `OrbitCameraController.panLimit = 80` — 피벗을 X/Z ±80 **정사각형**으로 클램프 (`:161`).
  긴 판형은 이 밖으로 나갑니다.
- `BattleCameraDirector.placementDistance = 34` 에서 `FocusOn(Vector3.zero, ...)` — 원점 중심.
- 프레이밍은 양 팀 가중 중심입니다. 건틀릿에서 보고 싶은 건 **선두**지 전체 중심이 아닙니다.

### 1.7 층별 난이도를 넣을 자리가 없습니다

밸런스는 `CreatureDefinition` 에만 있고 스폰 시점에 `CreatureUnit.Initialize()` 를 통해 컴포넌트로
흘러갑니다 (`CLAUDE.md` 의 핵심 규칙). 층별 배율을 넣으려면 **이 관문을 지나야 합니다.**
프리팹이나 씬에 수치를 박는 건 금지입니다.

주의: **방어력은 감산식**입니다 — `Health.TakeDamage` (`:42`) 가 `Mathf.Max(1f, raw - armor)`.
체력과 독립적인 손잡이가 아닙니다. 층 배율로 armor를 올리면 어느 지점에서 갑자기 모든 공격이
1 데미지가 되어 난이도가 선형이 아니라 절벽이 됩니다. **배율은 체력과 공격력에만.**

---

## 2. 계단을 어떻게 오를 것인가 — 세 가지 안

1.1이 이 모드의 성패입니다. 세 안을 비교합니다.

| 안 | 내용 | 비용 | 위험 |
|---|---|---|---|
| **A. 평면 층 + 완만한 경사로** | 각 층은 평평한 판. 층 사이는 12–15° 경사로. 계단의 "단차"는 옆면 장식으로만 표현 | 낮음 | 경사각 튜닝 필요 |
| B. 실제 단차 + 스텝 오프셋 로직 | `CreatureLocomotion` 에 계단 오르기 추가 (전방 레이 → 턱 감지 → 수직 보정) | 중 | 검증된 로코모션을 건드림. 평지 전투 회귀 위험 |
| C. NavMesh 도입 | `com.unity.ai.navigation`, 로코모션 교체 | 높음 | `Docs/roadmap.md` M4 항목. 전투 AI 전체 재검증 |

### 권고: A안

수평 추진만으로도 **경사로는 올라갑니다** — 경사면의 법선이 수평력을 위로 굴절시키기 때문입니다.
수직 성분이 없다는 1.1의 문제는 *턱*에서만 치명적이지 *비탈*에서는 아닙니다. 접지 레이캐스트도
비탈에서 정상 동작합니다 (아래로 쏘니까).

B안은 지금 잘 도는 로코모션에 손을 대는 것이고, 이 프로젝트는 스태거 하나 넣었다가 보스
승률이 5-7에서 11-1로 뒤집힌 전례가 있습니다. 로코모션은 밸런스에 직결됩니다.

C안은 모드 하나 때문에 치를 값이 아닙니다.

### 하지만 A안도 **가정**입니다 — 제일 먼저 검증하세요

경사각, 마찰, 작은 콜라이더, `ClampMagnitude(change, moveSpeed)` 가 오르막에서 실효 속도를
얼마나 깎는지 — 전부 측정해봐야 압니다. 그래서 구현 순서 1단계가 **"경사로 프로토타입"** 이고,
여기서 실패하면 A안을 버리고 B/C로 갑니다. 모드를 다 만들고 나서 "공룡이 못 올라가네"를
발견하는 것이 이 문서가 막으려는 유일한 최악의 결과입니다.

**검증 기준**: 가장 느리고 가장 작은 공룡(Velociraptor)과 가장 큰 보스가 1층에서 10층까지
**끼임 없이** 올라가고, 도달 시간이 평지 대비 30% 이내로 늘어날 것. 12마리 동시에도 동일.

---

## 3. 설계

### 3.1 씬 구조 — 두 아레나를 만들고 토글

```
Arena.unity
├── Ground / Boundary / Environment      ← 기존 원형 아레나 (VersusArena 아래로 이동)
├── GauntletArena                        ← 신규, 기본 비활성
│   ├── Tier_00 .. Tier_09               ← 평면 판 + 다음 층으로 가는 경사로
│   │   ├── Platform  (Collider)
│   │   ├── Ramp      (Collider)
│   │   ├── SpawnPoints (몬스터 배치용 Transform 배열)
│   │   └── Objective   (공룡이 이 층에서 향할 지점)
│   └── StartPlatform                    ← 플레이어 공룡 출발 지점
└── HUD
```

**두 아레나를 동시에 만들어 두고 루트만 켜고 끕니다.** 씬 재로드보다 단순하고, 비활성
렌더러는 비용이 0입니다. 정적 배칭에는 양쪽이 다 들어가지만 그건 빌드 시간 문제지 런타임
문제가 아닙니다.

`BattleSceneBuilder` 가 둘 다 생성합니다. **씬을 손으로 고치지 마세요** — 기존 규칙 그대로입니다.
경사로/플랫폼 콜라이더는 `Docs/performance.md` 와 최근 작업에 따라 **그림자를 던지지 않게**
설정하고, `BatchingStatic` 을 켭니다.

주의: 기존 `MarkSceneryStatic` / `StripSceneryShadowCasting` 은 `{ "Ground", "Boundary",
"Environment" }` 루트를 순회합니다. 새 루트를 이 목록에 추가해야 합니다.

### 3.2 데이터 — `GauntletLadder`

층 구성은 코드가 아니라 애셋입니다. `CreatureBlueprints` 와 같은 방식으로 에디터 스크립트가
생성합니다.

```csharp
[System.Serializable]
public class GauntletTierSpec
{
    public string label;                    // "1층", "보스" 등 HUD 표기
    public CreatureDefinition[] species;    // 이 층에 세울 종 (랜덤 배분)
    public int count;                       // 마리 수
    public float healthScale;               // 체력 배율
    public float damageScale;               // 공격력 배율
    public bool isBoss;                     // 마지막 층 연출/음악 전환용
}
```

**`armorScale` 은 없습니다.** 1.7의 이유 — 감산식이라 난이도가 절벽이 됩니다.

기본 사다리 (초안, 측정 후 조정):

| 층 | 마리 | 체력 ×  | 공격 × | 비고 |
|---|---|---|---|---|
| 1 | 10 | 1.0 | 1.0 | 약한 종만 |
| 2 | 10 | 1.15 | 1.05 | |
| 3 | 10 | 1.3 | 1.1 | 중형 섞기 시작 |
| … | | | | 층당 체력 +15%, 공격 +5% |
| 9 | 10 | 2.2 | 1.4 | |
| 10 | 1 | — | — | **보스** (`CreatureBlueprints` 의 기존 보스 재사용) |

체력을 공격력보다 빠르게 올리는 이유: 공격력 스케일링은 플레이어 공룡을 **즉사**시켜 체감이
"어려움"이 아니라 "불공정"이 됩니다. 체력 스케일링은 전투를 길게 만들 뿐입니다. 보스 밸런싱에서
이미 확인된 것 — TTK가 승률을 예측하는 지표였습니다.

### 3.3 상태 — `BattlePhase`는 건드리지 않습니다

1.5의 이유로 공용 enum에 값을 추가하지 않습니다. 대신:

```csharp
public enum GameMode { Versus, Gauntlet }

public enum GauntletState
{
    Ready,        // 배치 화면. 출발 대기
    Advancing,    // 공룡이 다음 층으로 이동 중 (교전 없음)
    Engaging,     // 현재 층 몬스터와 교전 중
    WaveWiped,    // 아군 전멸. "더 보내기" 버튼 대기
    Cleared,      // 보스 격파
    Defeated      // 예산 소진 + 전멸
}
```

- `BattleManager` 에 `GameMode Mode { get; }` 추가.
- 건틀릿 실행 중 `BattlePhase` 는 계속 `Fighting` 입니다. 기존 HUD/음악/카메라/춤은 **전부 그대로
  동작합니다** — 이게 enum을 안 건드리는 이유입니다.
- `BattleManager.HandleUnitDied` 에 한 줄: `if (Mode == GameMode.Gauntlet) { gauntlet.OnUnitDied(unit); return; }`
- `GauntletDirector` (신규 MonoBehaviour)가 `GauntletState` 를 소유하고, `StateChanged`,
  `TierChanged`, `RunEnded` 이벤트를 냅니다. **폴링 금지** — 기존 이벤트 규약 그대로.

### 3.4 타게팅 — 층을 안 지나간 몬스터는 등록조차 안 합니다

1.2를 코드 수정 없이 푸는 방법입니다.

`CreatureUnit` 은 `OnEnable` 에서 `UnitRegistry` 에 등록합니다. 따라서:

- 모든 층의 몬스터를 런 시작 시 미리 스폰하되 **`SetActive(false)`** 로 둡니다.
  → 레지스트리에 없으므로 `FindNearestEnemy` 가 절대 찾지 못합니다. **타게팅 코드 수정 0줄.**
- 공룡 선두가 N층 진입 트리거를 밟으면 N층 몬스터만 `SetActive(true)`.
- 미리 스폰하는 이유: 전투 중 인스턴스화는 프레임을 튀게 합니다. 비활성 오브젝트는 Update도
  렌더도 안 돕니다.

**대안 검토**: `FindNearestEnemy` 에 층 필터를 추가하는 방법도 있지만, 모든 호출자와
`SteeringBehaviors.Separation`, 카메라 프레이밍까지 층을 알아야 해서 번집니다. 비활성 스폰은
기존 생명주기를 그대로 쓰므로 새로운 규칙이 없습니다.

### 3.5 진격 — 적이 없을 때 공룡은 무엇을 하는가

지금 `CreatureBrain` 은 적이 없으면 `Idle` 로 서 있습니다. 건틀릿에서는 다음 층으로 걸어가야
합니다.

```csharp
// CreatureBrain
public Vector3? MarchTarget { get; set; }   // null이면 기존 동작 그대로
```

- `Idle` 상태에서 `MarchTarget` 이 있으면 그쪽으로 `Seek`.
- `GauntletDirector` 가 층이 바뀔 때마다 아군 전원의 `MarchTarget` 을 다음 층의 `Objective` 로
  설정.
- 적을 찾으면 기존 로직이 이깁니다 — 진격은 **대체가 아니라 폴백**입니다.
- 기존 **타깃 유지** 규칙(현재 타깃이 죽을 때까지 교체 안 함)은 그대로 둡니다. 이유는
  `CLAUDE.md` 에 있고 건틀릿에서도 유효합니다.

`MarchTarget` 이 nullable이라 대전 모드는 완전히 무영향입니다.

### 3.6 스탯 배율 — Initialize 관문을 통과시킵니다

1.7의 규칙을 지키는 유일한 방법:

```csharp
public void Initialize(CreatureDefinition definition, Team team, Color teamColor,
                       float healthScale = 1f, float damageScale = 1f)
```

- 기본 인자라 **기존 호출부 전부 무수정**.
- `Health.Configure(definition.maxHealth * healthScale, definition.armor)` — armor는 배율 없음.
- `MeleeAttack.Configure(definition, damageScale)`.
- 프리팹과 ScriptableObject는 손대지 않습니다. 배율은 스폰 시점에만 존재합니다.

`CreatureSpawner.Spawn` 에도 같은 형태로 선택 인자를 추가합니다.

### 3.7 카메라

- `OrbitCameraController.panLimit` 을 **단일 값에서 사각 경계로**: `panBoundsMin/Max` (Vector2).
  원형 아레나는 `(-80,-80)..(80,80)`, 건틀릿은 판형 실측치. `ClampToArena` 만 고치면 됩니다.
- `BattleCameraDirector` 에 건틀릿 프레이밍 추가: 중심은 **아군 선두 무리**(진행 방향 최전방
  70% 지점), 반경은 기존 `CoveringRadius` 재사용.
- 배치 화면: `FocusOn(Vector3.zero, placementDistance)` 대신 `StartPlatform` 을 봅니다.
- 보스 층 진입 시 한 번 넓게 빼서 보스를 보여주는 연출은 **v1 범위 밖**. 목록에만 둡니다.

### 3.8 UI

**배치 화면 상단에 모드 토글.** 현재 `PlacementPanel` 은 화면 하단 y 0–0.16입니다
(`BattleSceneBuilder:724`). 상단은 비어 있으므로 충돌 없습니다.

```
ModeBar  (y 0.90–1.00, Placement에서만 활성)
   [ 대전 ]  [ 건틀릿 ]      ← 선택된 쪽 강조
```

건틀릿 실행 중 HUD 추가:
- 현재 층 / 총 층 (`3 / 10`)
- 남은 예산 (3.9)
- **"더 보내기" 버튼** — `WaveWiped` 상태에서만 활성

`BattleHUD` 의 직렬화 참조는 전부 optional이라는 기존 규칙을 지키면 (`null` 체크 후 사용)
점진적으로 붙일 수 있습니다.

버튼 아이콘은 `ButtonIconBuilder` 에 추가합니다 (기존 6종과 같은 방식).

### 3.9 경제 — **결정 필요** (5번 참고)

"모든 공룡이 죽었으면 버튼을 눌러서 추가 공룡들을 출발시킬 수 있음" 에서 **횟수 제한이
명시되지 않았습니다.** 무제한이면 실패가 불가능해 긴장이 사라집니다.

**제안**: 런 전체에 총 예산(기본 5000). 출발시킬 무리를 배치 화면에서 고르고, 출발할 때마다
그 무리의 비용이 차감됩니다. 예산이 모자라 더 못 보내고 아군이 전멸하면 `Defeated`.
점수는 "몇 층까지, 예산 얼마를 쓰고" 로 남습니다.

`BattleLoadout` 의 팀당 예산 개념을 그대로 재사용할 수 있습니다.

---

## 4. 구현 순서

각 단계는 **검증 기준을 통과해야** 다음으로 갑니다. 특히 1단계.

### 1단계 — 경사로 프로토타입 (게이트) 🚧

모드도 UI도 없이, 경사로만 있는 테스트 씬에서 공룡을 걷게 합니다.

- `Dino Battle > Advanced > Ramp Probe` 에디터 메뉴: 층 10개짜리 경사로 생성 + 공룡 스폰 +
  최상층까지 걸리는 시간 측정
- 경사각 8/12/15/20/25° 를 각각 측정
- **통과 기준**: 2절의 기준. Velociraptor와 최대 보스가 12마리 동시에 끼임 없이 완주
- **실패 시**: A안 폐기, B안(스텝 오프셋) 프로토타입으로 전환. 이 문서 2절을 갱신할 것

> 측정할 때 `Application.runInBackground = true`, 시간은 `Time.time` 으로.
> `EditorApplication.update` 에서 델타를 누적하지 마세요 — `CLAUDE.md` 참고.

### 2단계 — 아레나 지오메트리

`BattleSceneBuilder` 에 `GauntletArena` 생성. 1단계에서 나온 경사각을 상수로.
`MarkSceneryStatic` / `StripSceneryShadowCasting` 루트 목록에 추가.
**검증**: `check-project.sh` 통과, 그림자 캐스터 수가 늘지 않을 것, 아레나 안에 떠도는 콜라이더
없을 것 (기존 `AssertArenaClear` 재사용).

### 3단계 — 데이터 + 스폰

`GauntletLadder` 애셋 생성기, `Initialize` 배율 인자, 비활성 선스폰.
**검증**: 10층 전부 스폰되고 레지스트리에는 0마리 (전부 비활성), 층 활성화 시 정확히 그 층만
등록될 것.

### 4단계 — 진행 로직

`GauntletDirector`, `MarchTarget`, 층 진입 트리거, `WaveWiped` / 더 보내기.
**검증**: 한 판을 끝까지 자동 진행 (스크립트로 아군을 무적으로 만들고) — 10층 전부 순서대로
활성화되고 보스에서 `Cleared`.

### 5단계 — 카메라 + UI

`panBounds`, 선두 프레이밍, 모드 토글, 층/예산 HUD.
**검증**: 모드 전환을 5회 왕복해도 `UnitRegistry` 에 유령이 없을 것 (1.3).

### 6단계 — 밸런싱

`BossBalanceProbe` 와 같은 방식의 `GauntletProbe`: 표준 편성으로 N회 자동 실행, 층별 도달률과
소모 예산 분포를 출력.
**검증 기준 제안**: 표준 편성으로 6–8층 도달이 중앙값. 보스 클리어율 20–30%.

> 보스 밸런싱에서 배운 것: **±25%p 안쪽 변화는 노이즈입니다.** 한 층을 만졌는데 다른 층이
> 같이 움직이면 그건 튜닝이 아니라 우연입니다. 시행 횟수를 먼저 늘리세요.

---

## 5. 결정이 필요한 것

1. **"각 계단은 10개"** — 계단마다 몬스터 10마리가 맞습니까, 아니면 계단이 총 10개입니까?
   (설정값이라 되돌리기 쉽지만 기본값을 정해야 합니다)
2. **추가 출발 횟수** — 3.9의 총예산 방식으로 갈까요, 무제한(연습 모드 성격)일까요?
3. **아군이 층을 넘어가는 조건** — 그 층 몬스터 전멸인가요, 아니면 살아남은 놈을 두고 지나갈
   수 있나요? (전멸을 기본으로 잡았습니다)
4. **죽은 아군의 처리** — 다음 웨이브에 부활 없음이 기본입니다. 진행 상황(도달 층)은 유지합니다.
5. **보스 층 도달 시 회복** — 없음이 기본. 있으면 난이도 곡선이 완전히 달라집니다.

---

## 6. 범위 밖 (v1에서 하지 않음)

- 보스 등장 컷신 / 전용 음악 전환
- 층별 지형 변화 (장애물 아레나는 NavMesh 항목과 얽힙니다 — `Docs/roadmap.md`)
- 진행 상황 저장 / 이어하기
- 층 보상, 업그레이드, 로그라이크 요소
- 건틀릿 전용 크리처

---

## 7. 관련 문서

- `CLAUDE.md` — 데이터 흐름 규칙, NavMesh 미사용 결정, 씬 코드 생성 규칙
- `Docs/performance.md` — 그림자/배칭 예산. 새 아레나도 같은 규칙을 지켜야 합니다
- `Docs/roadmap.md` — NavMesh, ECS 트리거
- `Docs/game-design.md` — 기존 대전 모드의 핵심 루프
