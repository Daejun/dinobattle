# 개발 환경 세팅

이 문서를 위에서부터 순서대로 따라가면 빌드가 됩니다.

## 0. 현재 상태

프로젝트 스캐폴드는 완성되어 있고, **툴체인은 아직 설치되지 않았습니다.**
2026-07-25 시점 이 머신에서 확인된 상태:

| 툴 | 상태 |
|---|---|
| git | ✅ `C:\Program Files\Git\cmd\git.exe` |
| node | ✅ `C:\Program Files\nodejs\node.exe` |
| dotnet | ✅ `C:\Program Files\dotnet\dotnet.exe` |
| git-lfs | ✅ 3.7.1 (이 리포지토리에 `--local` 초기화 완료) |
| WSL2 / Ubuntu 24.04 | ✅ 설치됨 — 툴체인은 WSL에서 돌립니다 ([`wsl.md`](wsl.md)) |
| **Unity Hub / Editor** | ❌ 미설치 |
| **JDK / Android SDK / adb** | ❌ 미설치 (Unity가 함께 설치해 줍니다) |
| WSL 내 ffmpeg / git-lfs | ❌ 미설치 — `bash Tools/fetch-assets.sh --deps` 로 확인 |
| `uv` (Unity MCP 연동용) | ❌ 미설치 — 8번 섹션 참고. 선택 사항 |

## 1. Unity Hub + Editor 설치

1. https://unity.com/download 에서 **Unity Hub** 설치
2. Hub → Installs → Install Editor → **Unity 6.5** 선택 (`6000.5.x`)
   - 2026-06-15 출시된 **Supported Update** 릴리스입니다 — LTS가 아닙니다
   - 지원 기간이 다음 Update 릴리스(6.6)까지이므로, 6.6이 나오면 업그레이드를 검토해야 합니다.
     그 시점에는 그때의 최신 LTS로 올리는 것이 낫습니다 ([`roadmap.md`](roadmap.md) M4 참고)
   - 6.5를 고른 이유: Android 쪽 개선(ThinLTO, IL2CPP Master, On-Tile Post Processing)이
     이 프로젝트에 직접 도움이 됩니다
3. 모듈 선택에서 **반드시** 체크:
   - ☑ **Android Build Support**
   - ☑ **OpenJDK** (Android Build Support 하위)
   - ☑ **Android SDK & NDK Tools** (Android Build Support 하위)

   → 이 세 개가 JDK / SDK / NDK / adb 를 전부 설치합니다. 별도로 Android Studio를 깔 필요 없습니다.

4. Unity 계정 로그인 후 **Personal** 라이선스 활성화 (매출 20만 USD 미만 무료)

## 2. 프로젝트 열기

Unity Hub → Projects → **Add** → `C:\Users\pdaej\dino_battle` 선택 → 열기.

`ProjectSettings/ProjectVersion.txt` 에 `6000.5.0f1` 이 적혀 있습니다.
설치한 패치 버전이 다르면(예: `6000.5.3f1`) Hub가 "다른 버전으로 열기" 확인창을 띄웁니다 —
**그대로 진행**하면 Unity가 파일을 실제 버전으로 덮어씁니다. 정상 동작입니다.

단, **6.5가 아닌 다른 마이너 버전(6.3, 6.6…)으로 열면** `bash Tools/check-project.sh` 가
경고합니다. 의도한 변경이라면 스크립트의 `EXPECTED_UNITY_MINOR` 와 이 문서를 함께 갱신하세요.

첫 실행 시 Unity가 `Library/`, `Packages/manifest.json`, 나머지 `ProjectSettings/*` 를 생성합니다.
수 분 걸립니다.

> `Packages/manifest.json` 은 의도적으로 만들지 않았습니다. 패키지 버전을 손으로 적으면
> 버전 불일치로 프로젝트가 아예 안 열릴 수 있어서, Unity가 기본값을 생성하게 두는 편이 안전합니다.

## 3. 필요한 패키지 (Window > Package Manager)

### ⚠️ uGUI는 6.5 기본 템플릿에 없습니다

`com.unity.ugui` 가 **`Packages/manifest.json` 에 이미 추가되어 있습니다** (버전 `2.5.0`).
없으면 `UnityEngine.UI` 를 찾지 못해 프로젝트가 컴파일되지 않습니다.

혼동하기 쉬운 구분입니다:

| 패키지 | 제공하는 것 | 기본 포함? |
|---|---|---|
| `com.unity.modules.ui` | `Canvas`, `CanvasRenderer`, `RectTransform` | ✅ |
| **`com.unity.ugui`** | **`UnityEngine.UI.*`** (Button, Text, Image…), `EventSystems`, TextMeshPro | ❌ 직접 추가 |

Safe Mode로 진입했다면 manifest에 이 줄이 있는지 먼저 확인하세요.

### 그 외 패키지

지금은 위 하나로 충분합니다. 필요해지면 추가하세요:

| 패키지 | 언제 | 비고 |
|---|---|---|
| **Universal RP** (`com.unity.render-pipelines.universal`) | 비주얼 품질 올릴 때 | 모바일 필수급. 도입 시 머티리얼 업그레이드 필요 |
| **TextMeshPro** | HUD 폰트 개선 | 이미 `com.unity.ugui` 에 포함됨. 현재는 legacy `Text` 사용 중 |
| **Input System** (`com.unity.inputsystem`) | 입력을 정식화할 때 | 현재는 legacy `Input` 사용 — 아래 참고 |
| **AI Navigation** (`com.unity.ai.navigation`) | NavMesh 길찾기 필요할 때 | 현재는 직접 스티어링. 장애물 아레나 만들면 고려 |
| **Test Framework** | 유닛 테스트 | Window > General > Test Runner 에서 자동 생성 |

### 입력 시스템에 대한 결정

런타임 코드는 **legacy Input Manager**(`Input.GetTouch`, `Input.mousePosition`)를 씁니다.
이유는 하나 — Input System 패키지 없이 즉시 컴파일되고 즉시 플레이됩니다.

`Project Settings > Player > Active Input Handling` 이 `Input Manager (Old)` 또는 `Both` 여야 합니다.
나중에 Input System으로 옮길 때 손대야 하는 파일은 딱 둘입니다:
- [`PlacementController.cs`](../Assets/Scripts/Placement/PlacementController.cs) → `TryGetPointer()`
- [`OrbitCameraController.cs`](../Assets/Scripts/CameraRig/OrbitCameraController.cs) → `HandleTouch()` / `HandleMouse()`

## 4. 플레이 가능한 씬 만들기

에디터 메뉴에서 **순서대로**:

1. **Dino Battle → 1. Generate Sample Content**
   → 공룡 6종의 `CreatureDefinition` + 플레이스홀더 프리팹 + 로스터 생성
2. **Dino Battle → 2. Build Battle Scene**
   → `Assets/Scenes/Arena.unity` 생성 (지형/조명/카메라/매니저/HUD 전부 배선됨) + Build Settings 등록

씬을 손으로 고치지 말고, 레이아웃을 바꾸려면
[`BattleSceneBuilder.cs`](../Assets/Editor/BattleSceneBuilder.cs) 를 수정하고 2번을 다시 실행하세요.
`.unity` 파일을 직접 편집하는 건 고통스럽고 머지도 안 됩니다.

## 5. 플레이

Play 버튼을 누르면:

1. 하단 로스터에서 공룡 선택
2. 바닥 클릭 → 배치 (초록 원 = 가능, 빨강 = 예산 초과 또는 자리 겹침)
3. **TEAM** 버튼으로 RED ↔ BLUE 전환 후 상대팀도 배치
4. **START BATTLE** → AI가 알아서 싸움
5. 관전: 좌드래그 = 궤도 회전, 휠 = 줌, 중클릭 드래그 = 팬, **x1** 버튼 = 배속

## 6. Android 빌드

### 실기 테스트 (권장)

1. 기기에서 개발자 옵션 → USB 디버깅 켜기
2. USB 연결
3. Unity: **File > Build Settings > Android > Switch Platform**
4. **Build And Run**

### CLI 빌드

WSL에서 (권장 — [`wsl.md`](wsl.md) 참고):

```bash
bash Tools/build-android.sh
```

Play Store용 AAB:

```bash
bash Tools/build-android.sh --aab
```

PowerShell에서:

```bash
pwsh ./Tools/build-android.ps1 -Aab
```

에디터 경로를 못 찾으면 `Tools/local.build.props` 를 만들어 주세요 (gitignore됨, bash/PowerShell 공용):

```
UnityPath=C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe
```

빌드 설정은 [`AndroidBuilder.cs`](../Assets/Editor/AndroidBuilder.cs) 에 코드로 박혀 있어서
Player Settings를 손으로 맞출 필요가 없습니다:

| 설정 | 값 | 비고 |
|---|---|---|
| Application ID | `com.dinobattle.game` | |
| **min SDK** | **26 (Android 8.0)** | **Unity 6.5가 API 26을 최소로 올렸습니다.** 더 낮추면 에디터가 빌드를 거부합니다 |
| Scripting backend | IL2CPP | ARM64 전용 빌드에 필수 |
| Architecture | ARM64 | Play Store가 armv7 단독 업로드를 거부합니다 |
| IL2CPP configuration | AAB=Master, APK=Release | Master는 컴파일이 훨씬 느린 대신 런타임이 가장 빠릅니다 |
| Code generation | AAB=OptimizeSpeed, APK=OptimizeSize | |
| Orientation | 가로 고정 | |

### 손으로 켜야 하는 것: Link Time Optimization (ThinLTO)

Unity 6.5의 신규 옵션으로, non-development 빌드에 링크 타임 최적화된 `libunity.so` 를 사용합니다.
시작 시간·프레임 성능 **평균 5% 개선**입니다.

**Project Settings → Player → Android → Publishing Settings → Link Time Optimization**

PlayerSettings API로 자동화하지 않은 이유는 6.5 신규 프로퍼티명을 확인하지 못했기 때문입니다 —
추측한 API명을 넣으면 컴파일이 깨지므로 수동 토글로 남겼습니다.
정확한 프로퍼티를 확인하면 `AndroidBuilder.ApplyPlayerSettings()` 에 추가하세요.

## 7. Git LFS

이 리포지토리에는 이미 `--local` 로 초기화되어 있습니다 (전역 git 설정은 건드리지 않았습니다).
`.gitattributes` 에 FBX/PNG/WAV 등 13종 확장자가 LFS 대상으로 등록되어 있습니다.

확인:

```bash
git lfs track
```

**다른 머신에서 클론했다면** 애셋을 커밋하기 전에 한 번:

```bash
git lfs install --local
```

WSL에서 커밋할 계획이면 WSL 쪽 git-lfs도 필요합니다:

```bash
sudo apt install -y git-lfs && git lfs install --local
```

LFS 없이 FBX를 커밋하면 리포지토리가 순식간에 수 GB로 부풀고 되돌리기 어렵습니다.

## 7-1. 정적 검증

이 워크스페이스에는 C# 컴파일러가 없습니다(Unity가 컴파일러입니다). 코드를 수정한 뒤에는:

```bash
bash Tools/check-project.sh
```

`SerializedObject.FindProperty` 가 없는 필드를 가리키는 문제, Unity 6에서 이름이 바뀐 API,
네임스페이스 누락, Animator 파라미터 드리프트, 리포지토리 위생을 검사합니다.
자세한 내용은 [`wsl.md`](wsl.md#check-projectsh--이게-왜-중요한가).

## 8. Unity MCP 연동 (선택, 권장)

Claude Code가 Unity 에디터에 직접 붙어서 **실제 컴파일 에러를 읽고**, 메뉴 아이템을 실행하고,
씬 하이어라키를 검사할 수 있게 됩니다. 이 워크스페이스의 최대 제약(컴파일러 부재)이 해소됩니다.

### 공식 MCP 대신 CoplayDev를 쓰는 이유

Unity 공식 MCP(`com.unity.ai.assistant`)는 **유료입니다.** Unity 직원이
[공식 스레드](https://discussions.unity.com/t/request-for-official-clarification-unity-mcp-access-subscription-requirements-and-future-policy/1720323)에서
"현재로서는 MCP 연결에 구독이 필요하다"고 확인했습니다. Personal 라이선스로는 별도의
Unity AI 구독이 필요하고, Pro/Enterprise도 동시 연결 수에 제한이 있습니다.

그 외 커뮤니티에서 보고된 문제: `Capacity Limit` 에러, 서드파티 AI 사용 시에도 기록되는 텔레메트리,
에이전트 작업 중 뜨는 모달 다이얼로그, **헤드리스 운용 어려움**. 마지막 두 개는 배치 빌드를
자동화하려는 이 프로젝트에 직접적인 문제입니다.

[CoplayDev/unity-mcp](https://github.com/CoplayDev/unity-mcp)은 MIT, 구독 불필요,
동시 연결 제한 없음. 2026-07 기준 12.8k stars로 커뮤니티 표준입니다.

### 사전 요구사항 — 완료됨

Unity 에디터와 Claude Code가 모두 Windows에 있으므로 **Python 툴체인도 Windows에** 설치합니다
(WSL이 아닙니다). `uv` 가 Python 3.10+ 를 알아서 관리합니다.

```bash
winget install --id astral-sh.uv --exact
```

설치 위치: `%LOCALAPPDATA%\Microsoft\WinGet\Links\uv.exe`.
**설치 직후에는 PATH가 갱신되지 않습니다** — 새 셸(또는 Unity)을 열어야 `uv` 가 인식됩니다.

### 설치 — 패키지는 추가 완료됨

`Packages/manifest.json` 에 아래가 이미 들어가 있습니다:

```json
"com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#v10.1.0"
```

**태그로 고정했습니다.** `#main` 으로 받으면 저장소가 움직일 때마다 프로젝트가 같이 흔들립니다.
버전을 올릴 때는 [릴리스 목록](https://github.com/CoplayDev/unity-mcp/releases)에서
실제 존재하는 태그를 확인하고 이 줄을 고치세요.

이 패키지는 `com.unity.nuget.newtonsoft-json` 과 `com.unity.test-framework` 를 끌어옵니다 —
Unity가 자동으로 해석하므로 `packages-lock.json` 이 함께 갱신됩니다.

### 남은 단계 (에디터에서)

1. Unity를 다시 열면 패키지를 clone하고 임포트합니다.
2. **Window → MCP for Unity → Configure All Detected Clients**
   → 감지된 MCP 클라이언트를 자동으로 설정합니다.
3. Claude Code를 **재시작**한 뒤 확인:

   ```bash
   claude mcp list
   ```

   자동 감지되지 않으면 위 Unity 창에 표시되는 서버 실행 명령을 복사해 수동 등록하세요.
   정확한 인자는 [프로젝트 위키](https://coplaydev.github.io/unity-mcp/)에 있습니다.

### 주의

MCP는 에이전트에게 **프로젝트 쓰기 권한**을 줍니다 — 스크립트와 씬을 직접 수정할 수 있습니다.
에이전트에게 작업을 맡기기 전에 커밋해 두세요. 되돌릴 수 있어야 합니다.

MCP를 붙이더라도 `bash Tools/check-project.sh` 는 계속 유용합니다 — 에디터를 켜지 않고
CI에서도 돌릴 수 있고, Animator 파라미터 드리프트처럼 컴파일러가 못 잡는 것을 검사합니다.

## 9. 문제 해결

| 증상 | 원인 / 해결 |
|---|---|
| 프로젝트 열 때 **"contains compilation errors"** + Safe Mode 창 | `UnityEngine.UI` 를 못 찾는 경우입니다. `Packages/manifest.json` 에 `"com.unity.ugui": "2.5.0"` 이 있는지 확인. 3번 섹션 참고. 수정 후 Unity를 다시 열면 됩니다 |
| `Roster_Default not found` 경고 | 1번(Generate Sample Content)을 먼저 실행 |
| 공룡이 배치되는데 START가 비활성 | 양 팀 모두 최소 1마리 필요 |
| 공룡이 안 움직임 | `Ground` 오브젝트가 `CreatureLocomotion.groundMask` 레이어에 있는지 확인 |
| 공룡이 서로 밀며 진동 | `CreatureBrain.approachRangeFactor` 를 낮추거나 `footprintRadius` 를 키울 것 |
| 큰 전투에서 프레임 드롭 | `MobilePerformance.physicsHz` 를 50 → 30으로 |
| HUD 텍스트가 안 보임 | 폰트 미로딩. HUD의 `Text` 컴포넌트에 폰트 수동 할당 |
