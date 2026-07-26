# CLAUDE.md

Unity 6.5 (`6000.5.x`)로 만드는 **관전형 공룡 전투 시뮬레이터** (Android).
플레이어가 양 팀에 공룡을 배치하고 전투를 시작하면 AI가 싸우고, 플레이어는 카메라만 조작합니다.
레퍼런스는 Animal Revolt Battle Simulator.

## 개발 환경 상태 (중요)

**Unity 6000.5.0f1 + Android Build Support(SDK/NDK/OpenJDK 동봉)가 설치되어 있습니다.**
이 문서는 원래 아무것도 없던 시절에 쓰였고, 그 제약은 더 이상 사실이 아닙니다.

확인된 상태 (2026-07-26):

- 에디터: `C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe`
- Android 툴체인은 에디터에 동봉된 것을 그대로 씁니다 —
  `.../PlaybackEngines/AndroidPlayer/{SDK,NDK,OpenJDK}`. 별도 설치 불필요
- 활성 빌드 타깃이 이미 Android, IL2CPP / ARM64 / minSdk 26
- `Dino Battle > 3. Build Android APK` 로 25 MB APK가 나옵니다 (`Build/Android/`).
  증분 빌드는 1분 이내

**즉 컴파일 검증도 빌드 검증도 직접 할 수 있습니다.** "빌드해서 확인했다"고 말하려면 실제로
빌드하세요. 다만 **폰이 연결되어 있지 않으면 설치는 못 합니다** — `adb devices` 가 비어 있으면
사용자에게 USB 연결과 디버깅 허용을 요청하세요.

헤드리스 빌드(`Tools/build-android.sh`)는 **에디터가 열려 있으면 실패합니다** — 같은 프로젝트를
두 번 열 수 없기 때문입니다. MCP가 붙어 있는 상황에서는 메뉴 3번을 쓰세요.

설치 절차는 `Docs/setup.md` 에 있습니다.

### Unity MCP가 연결되면 이 제약이 바뀝니다

`Docs/setup.md` 8번 섹션대로 [CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)를
붙이면 `mcp__*` 툴로 에디터에 직접 접근할 수 있습니다. 그때는:

- **실제 컴파일 에러를 읽으세요.** grep 추측 대신 에디터가 뱉은 에러가 정답입니다
- `Dino Battle > 1/2/3` 메뉴를 직접 실행해 씬 생성까지 확인하세요
- 씬 하이어라키와 컴포넌트 배선을 실제로 검사하세요

MCP가 연결되어 있는지는 세션의 사용 가능한 툴 목록으로 판단하세요.

**메뉴 2번은 모달 확인창을 띄웁니다.** `BattleSceneBuilder.Build()` 가 씬을 덮어쓰기 전에
`EditorUtility.DisplayDialog` 를 호출하는데, MCP로 실행하면 창이 뜬 채로 **에디터 전체가 멈추고
MCP 연결도 끊깁니다**. 응답이 없으면 죽은 게 아니라 확인창을 기다리는 중입니다 —
사용자에게 "Build" 를 눌러달라고 하세요. `Get-Process Unity` 는 이때도 Responding=True 로 나오니
판단 근거가 되지 못합니다.

**측정 전에 `Application.runInBackground = true` 를 켜세요.** 꺼져 있으면 에디터가 포커스를 잃는
순간 월드가 멈추고, `Time.frameCount` 가 그대로인 정지된 세계를 측정하게 됩니다.

**`EditorApplication.update` 는 게임 프레임당 한 번이 아닙니다.** 여기서 시간을 재려면
`Time.time` 을 쓰세요. 델타를 누적하면 실제보다 몇 배 빠른 시계가 됩니다 —
`BossBalanceProbe` 가 정상 전투를 12/12 무승부로 보고한 원인이었습니다.

**MCP가 있어도 `Tools/check-project.sh` 는 계속 돌리세요.** Animator 파라미터 드리프트,
문서-코드 불일치처럼 컴파일러가 잡지 못하는 것을 검사하고, 에디터 없이 CI에서도 동작합니다.

**MCP는 프로젝트 쓰기 권한입니다.** 씬이나 스크립트를 MCP로 수정하기 전에 커밋 상태를 확인하세요.

## 명령어

개발은 **WSL 하이브리드**입니다 — Unity Editor는 Windows, 툴체인은 WSL(Ubuntu 24.04).
프로젝트는 `/mnt/c/Users/pdaej/dino_battle` 에 있고 양쪽에서 같은 경로로 접근합니다.
자세한 이유와 트레이드오프는 `Docs/wsl.md`.

```bash
# 정적 검증 — 코드를 수정했으면 반드시 실행. 아래 "검증" 섹션 참고
bash Tools/check-project.sh

# Android 빌드 (Unity 설치 후)
bash Tools/build-android.sh          # APK
bash Tools/build-android.sh --aab    # Play Store 번들

# 애셋 파이프라인
bash Tools/fetch-assets.sh --list    # 받을 수 있는 CC0 애셋 목록 (다운로드 없음)
bash Tools/fetch-assets.sh --sounds  # CC0 사운드 라이브러리 클론
bash Tools/convert-audio.sh <in> Assets/Audio/SFX/   # 모노/44.1k/OGG 정규화
```

PowerShell 버전도 있습니다 (`Tools/build-android.ps1`). 두 스크립트는
`Tools/local.build.props` 를 공유합니다 — Windows 경로 한 줄만 적으면 bash가 `wslpath` 로 변환합니다.

**주의**: 이 세션의 `Bash` 툴은 Git Bash이고 WSL이 아닙니다. WSL에서 돌려야 하면
`wsl -d Ubuntu-24.04 -- bash -c "cd /mnt/c/Users/pdaej/dino_battle && ..."` 형태로 호출하세요.

## 검증 — 컴파일러가 없으므로 이것이 유일한 안전망

`bash Tools/check-project.sh` 가 잡는 것:

1. **`SerializedObject.FindProperty("x")` 가 없는 필드를 가리키는 경우** — 가장 중요합니다.
   에디터 생성 스크립트(`BattleSceneBuilder`, `SampleContentBuilder`)가 런타임 컴포넌트의
   private 필드를 이름 문자열로 채우기 때문에, 필드명을 바꾸면 컴파일은 되고 런타임에 터집니다.
   **런타임 스크립트의 `[SerializeField]` 필드명을 바꿨다면 반드시 실행하세요.**
2. Unity 6에서 이름이 바뀐 API (`Rigidbody.velocity`, `FindObjectOfType`)
3. `DinoBattle.*` 네임스페이스 누락
4. Animator 파라미터(`Speed`, `Attack`) 이름이 코드와 `Docs/assets.md` 사이에서 어긋나는 것
5. **음악 임포트 설정** — `Assets/Audio/Music/*.meta` 가 `loadType: 2` (Streaming)인지.
   Unity 기본값 DecompressOnLoad로 두면 재생 시 PCM으로 풀려서 트랙 두 개가 43 MB를 먹습니다
   (APK 전체가 25 MB인데). `Assets/Editor/AudioImportSettings.cs` 가 임포트 시점에 설정하지만
   AssetPostprocessor는 **재임포트 때만** 돌기 때문에, 실제로 출하되는 커밋된 `.meta` 를 검사합니다.
   미참조 음악 파일도 같이 잡습니다 — Unity가 빌드에서 빼버리므로 리포지토리 무게만 됩니다
6. 리포지토리 위생 (`Library/` gitignore, git-lfs 초기화)

에디터 메뉴 (Unity 안에서, **순서대로** 실행):

1. `Dino Battle > 1. Generate Sample Content` — CreatureDefinition + 플레이스홀더 프리팹 + 로스터 생성
2. `Dino Battle > 2. Build Battle Scene` — `Assets/Scenes/Arena.unity` 생성 및 Build Settings 등록
3. `Dino Battle > 3. Build Android APK / AAB`

## 아키텍처

### 데이터 흐름 — 이게 핵심 규칙입니다

```
CreatureDefinition (ScriptableObject)
        │  스폰 시점
        ▼
CreatureUnit.Initialize()  ──►  Health.Configure()
                           ──►  CreatureLocomotion.Configure()
                           ──►  MeleeAttack.Configure()
```

**밸런스 수치는 절대 프리팹에 넣지 마세요.** 모든 스탯은 `Assets/GameData/Creatures/*.asset`
(`CreatureDefinition`)에 있고, 스폰 시점에 컴포넌트로 흘러들어갑니다.
프리팹은 비주얼과 컴포넌트 배선만 담당하는 "멍청한" 껍데기입니다.

프리팹의 직렬화된 값(`MeleeAttack.damage` 등)은 에디터에서 단독 테스트할 때만 쓰이는 기본값이고,
런타임에는 definition 값으로 덮어써집니다.

### 매치 생명주기

`BattleManager` (싱글턴, `Instance`)가 `BattlePhase` 를 소유합니다:

```
Placement ──StartBattle()──► Fighting ──(한 팀 전멸)──► Finished
     ▲                                                     │
     └──────────────── EnterPlacement() ───────────────────┘
```

다른 시스템은 폴링하지 말고 이벤트를 구독하세요:
- `PhaseChanged(BattlePhase)`
- `BattleEnded(Team)`
- `UnitCountChanged()`

### 전투 AI

`CreatureBrain`: `Idle → Seek → Attack → Dead`.
`CombatEnabled` 가 false면 아무것도 하지 않습니다 (배치 단계).

의도적인 결정 — **바꾸기 전에 이유를 이해하세요**:

- **타깃 유지**: 현재 타깃이 죽을 때까지 교체하지 않습니다. 매 프레임 최근접으로 갈아타면
  공룡들이 우왕좌왕해서 전투가 우스워집니다.
- **리타깃 스태거**: `Awake()` 에서 타이머를 `Random.Range(0, retargetInterval)` 로 초기화해
  100마리가 같은 프레임에 스캔하지 않게 합니다.
- **`UnitRegistry` 는 static**: 팀별 생존 유닛 리스트. 물리 오버랩 쿼리 대신 리스트 순회.
  **static이므로 씬 로드보다 오래 살아남습니다** — 새 매치 스폰 전에 반드시 `Clear()`.
  `BattleManager.EnterPlacement()` / `StartBattle()` 이 처리합니다.
- **NavMesh 미사용**: `CreatureLocomotion` 이 Rigidbody 직접 스티어링. 베이크 불필요.
  장애물 아레나를 만들 때만 재검토.
- **어택 윈드업**: 사거리 진입 즉시 데미지가 아니라 `attackWindup` 지연 후 적용.
  윈드업 중 타깃이 도망가면 `windupRangeSlack` 만큼 여유를 주고 그 밖은 헛방.

### 씬은 코드로 생성합니다

`Assets/Scenes/Arena.unity` 는 `Assets/Editor/BattleSceneBuilder.cs` 의 산출물입니다.

**씬을 에디터에서 손으로 고치지 마세요.** 레이아웃/HUD를 바꾸려면 `BattleSceneBuilder` 를
수정하고 메뉴 2번을 다시 실행하세요. 이유:
- `.unity` YAML은 머지가 불가능합니다 (`.gitattributes` 에서 automerge를 껐습니다)
- 씬 구성이 코드로 리뷰 가능해집니다
- 새 클론에서 재현이 보장됩니다

같은 이유로 `SampleContentBuilder` 가 프리팹과 ScriptableObject를 생성합니다.
private 직렬화 필드를 채울 때는 `SerializedObject` + `FindProperty` 를 씁니다 —
필드를 public으로 바꾸지 마세요.

## 코드 규칙

- 네임스페이스: `DinoBattle.Core` / `.Data` / `.Units` / `.Placement` / `.CameraRig` / `.UI` / `.EditorTools`
- private 필드는 `[SerializeField] private` + camelCase. public 프로퍼티로 노출.
- **Assembly Definition(asmdef)을 추가하지 않았습니다.** 전부 `Assembly-CSharp` 로 컴파일됩니다.
  asmdef를 넣으면 `UnityEngine.UI` 참조를 명시해야 하는 등 함정이 늘어납니다. 지금은 불필요.
- **Unity 6.5 API 주의사항** — 6.5에서 **경고가 아니라 컴파일 에러**가 되는 것들:
  - `Rigidbody.linearVelocity` (구버전 `velocity` 아님)
  - `FindAnyObjectByType` / `FindObjectsByType` (`FindObjectOfType` / `FindObjectsOfType` 아님)
  - `GetComponent<T>()` — `gameObject.rigidbody` / `.camera` / `.light` / `.collider` /
    `.renderer` / `.audio` 프로퍼티 접근자는 **제거되었습니다**
  - `AddComponent<T>()` — `AddComponent("문자열")` 오버로드는 제거되었습니다
  - `EntityId` 는 8바이트입니다. InstanceID를 `int` 에 저장하면 데이터가 깨집니다
  - 2D 물리는 `Unity.U2D.Physics.PhysicsCore2D` 로 이동 (`LowLevelPhysics2D` 아님)
  - `Docs/setup.md` 의 min SDK는 **26** — 6.5가 API 26을 최소로 올렸습니다. 낮추면 빌드 거부됩니다

  이 항목들은 `Tools/check-project.sh` 가 검사합니다.
- HUD의 직렬화 참조는 전부 optional입니다 — null 체크 후 사용. 덕분에 HUD를 점진적으로 조립할 수 있습니다.

## 입력 — legacy Input Manager를 씁니다

`Input.GetTouch()`, `Input.mousePosition`, `Input.mouseScrollDelta` 사용.
**이건 의도된 선택입니다**: Input System 패키지 없이 즉시 컴파일되고 즉시 플레이됩니다.

`Project Settings > Player > Active Input Handling` 이 `Input Manager (Old)` 또는 `Both` 여야 합니다.

Input System으로 이관할 때 손댈 파일은 둘뿐:
- `PlacementController.TryGetPointer()`
- `OrbitCameraController.HandleTouch()` / `HandleMouse()`

## 패키지에 대해

`Packages/manifest.json` 은 이제 존재합니다 (Unity가 첫 실행 시 생성).

**`com.unity.ugui` 는 Unity 6.5 3D 템플릿의 기본 패키지가 아닙니다.**
첫 오픈에서 컴파일 에러로 걸렸고, `2.5.0` 을 manifest에 명시적으로 추가했습니다.
헷갈리기 쉬운 구분:

| 패키지 | 제공하는 것 |
|---|---|
| `com.unity.modules.ui` (기본 포함) | `UnityEngine.Canvas`, `CanvasRenderer`, `RectTransform` |
| **`com.unity.ugui`** (직접 추가함) | **`UnityEngine.UI.*`** (Button, Text, Image, CanvasScaler, GraphicRaycaster), `UnityEngine.EventSystems`, TextMeshPro |

`BattleHUD.cs` 와 `BattleSceneBuilder.cs` 가 `UnityEngine.UI` 에 의존합니다.

**패키지 버전을 추측해서 적지 마세요.** 에디터에 동봉된 버전은 여기서 확인합니다:

```
C:\Program Files\Unity\Hub\Editor\<버전>\Editor\Data\Resources\PackageManager\BuiltInPackages\<패키지>\package.json
```

`Tools/check-project.sh` 의 2b 검사가 코드의 `using` 과 manifest를 대조합니다 —
새 네임스페이스를 쓰기 시작하면 `NS_REQUIRES` 배열에 매핑을 추가하세요.

URP / Input System / AI Navigation은 `Docs/roadmap.md` 의 M4 항목입니다.

## 저작권 — 반드시 지킬 것

레퍼런스 영상에 마블 캐릭터(스파이더맨, 베놈, 캡틴아메리카)가 나오지만 **사용 불가**입니다.
Play Store에서 즉시 내려가고 계정 정지 사유입니다.

- 실제 공룡 종 이름은 자연물이라 자유롭게 사용 가능
- "Bio T-Rex" 같은 창작 변종은 OK
- "Indominus Rex", "Indoraptor", "Blue" 등 쥬라기 월드 상표는 **금지**
- 영화/게임에서 리핑한 사운드 **금지** (Content ID 탐지됨)

애셋 라이선스 우선순위: **CC0 > CC-BY**. CC-BY-SA / NC / ND는 거부.
애셋 추가 시 `ATTRIBUTIONS.md` 에 한 줄 기록.

추천 소스는 `Docs/assets.md` 에 정리되어 있습니다 (Quaternius CC0 공룡 팩,
lavenderdotpet/CC0-Public-Domain-Sounds 등).

## Git

- Unity 표준 `.gitignore` — `Library/`, `Temp/`, `Build/`, `Logs/`, `UserSettings/`, `.assets-cache/` 제외
- `.gitattributes` 에서 Unity YAML의 automerge를 끄고, 애셋 확장자 13종을 LFS로 지정
- **git-lfs는 이 리포지토리에 `--local` 로 이미 초기화되어 있습니다** (전역 git 설정은 건드리지 않음).
  다른 머신에서 클론하면 `git lfs install --local` 을 다시 실행해야 합니다.
  LFS 없이 FBX를 커밋하면 리포지토리가 수 GB로 부풀고 되돌리기 어렵습니다.
- WSL에는 git-lfs가 아직 없습니다 (Windows git에는 3.7.1). WSL에서 애셋을 커밋하려면
  `sudo apt install -y git-lfs` 가 먼저 필요합니다.
- 다운로드한 서드파티 애셋은 `.assets-cache/` 에 두고, **실제 쓰는 파일만** `Assets/` 로 복사.
  클론 전체를 `Assets/` 에 넣으면 Unity가 전부 임포트합니다.
