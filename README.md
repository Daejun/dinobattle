# Dino Battle

공룡끼리 싸우는 안드로이드 게임. **관전형 전투 시뮬레이터** —
양 팀에 공룡을 배치하고 전투를 시작하면 AI가 알아서 싸우고, 플레이어는 카메라를 돌리며 지켜봅니다.

레퍼런스: [Animal Revolt Battle Simulator 플레이 영상](https://youtu.be/XEvputtBXZQ)

| | |
|---|---|
| 엔진 | Unity 6.5 / `6000.5.x` (3D, Built-in RP) |
| 타겟 | Android (min SDK 26, ARM64, IL2CPP, 가로 고정) |
| 언어 | C# |

---

## 빠르게 시작

> ⚠️ 이 머신에는 **Unity가 아직 설치되지 않았습니다.** 먼저 [`Docs/setup.md`](Docs/setup.md) 를 보세요.

Unity 6.5(Android Build Support 모듈 포함)를 설치한 뒤:

1. Unity Hub → Add → 이 폴더 열기
2. 에디터 메뉴 **Dino Battle → 1. Generate Sample Content**
3. 에디터 메뉴 **Dino Battle → 2. Build Battle Scene**
4. ▶ Play

바닥을 클릭해 공룡을 배치하고, **TEAM** 버튼으로 상대팀도 배치한 뒤 **START BATTLE**.

Android 빌드 (WSL):

```bash
bash Tools/build-android.sh
```

코드를 수정했으면 정적 검증 — Unity 없이 문제를 잡습니다:

```bash
bash Tools/check-project.sh
```

---

## 프로젝트 구조

```
Assets/
  Scripts/
    Core/          BattleManager, CreatureSpawner, MobilePerformance, GameEnums
    Data/          CreatureDefinition, CreatureRoster, BattleLoadout  (ScriptableObject 밸런스 데이터)
    Units/         CreatureUnit, CreatureBrain, CreatureLocomotion, MeleeAttack, Health, UnitRegistry
    Placement/     PlacementController  (배치 단계 입력)
    CameraRig/     OrbitCameraController (관전 카메라)
    UI/            BattleHUD, HealthBarBillboard
  Editor/          SampleContentBuilder, BattleSceneBuilder, AndroidBuilder  (씬/데이터 코드 생성)
  Prefabs/         Creatures, UI, VFX
  GameData/        Creatures/*.asset, Rosters/*.asset
  Art/             Models, Materials, Textures, Animations
  Audio/           SFX, Music
  Scenes/          Arena.unity  (코드 생성 — 손으로 편집하지 말 것)
Docs/              setup, wsl, game-design, roadmap, assets, legal
Tools/             check-project.sh, build-android.{sh,ps1}, fetch-assets.sh, convert-audio.sh
```

## 개발 환경

**WSL 하이브리드** — Unity Editor는 Windows, 툴체인은 WSL(Ubuntu 24.04).
프로젝트는 `/mnt/c/Users/pdaej/dino_battle` 에 있고 양쪽에서 같은 경로로 접근합니다.
Unity Editor를 WSL로 옮기지 않은 이유는 [`Docs/wsl.md`](Docs/wsl.md) 에 정리했습니다.

| 스크립트 | 용도 |
|---|---|
| `Tools/check-project.sh` | 정적 검증 — 컴파일러 없이 잡을 수 있는 문제 5종 |
| `Tools/build-android.sh` | APK/AAB 헤드리스 빌드 (WSL에서 Windows `Unity.exe` 호출) |
| `Tools/fetch-assets.sh` | CC0 애셋 다운로드 / 라이선스 정보 / 의존성 확인 |
| `Tools/convert-audio.sh` | 사운드 정규화 + OGG 변환 |

## 문서

| 문서 | 내용 |
|---|---|
| [Docs/setup.md](Docs/setup.md) | 툴체인 설치, 프로젝트 열기, 빌드, **Unity MCP 연동**, 트러블슈팅 |
| [Docs/wsl.md](Docs/wsl.md) | **WSL 하이브리드 워크플로** — 스크립트, 애셋 파이프라인, adb |
| [Docs/game-design.md](Docs/game-design.md) | 게임 루프, AI 상태 머신, 스탯 모델, 초기 밸런스 |
| [Docs/assets.md](Docs/assets.md) | **CC0 공룡 모델·애니메이션·사운드 소스 조사 결과** |
| [Docs/roadmap.md](Docs/roadmap.md) | M0 스캐폴드 ~ M4 출시 마일스톤 |
| [Docs/legal.md](Docs/legal.md) | 저작권 주의사항 (마블 캐릭터 사용 불가 등) |
| [CLAUDE.md](CLAUDE.md) | Claude Code용 프로젝트 컨텍스트 |
| [ATTRIBUTIONS.md](ATTRIBUTIONS.md) | 서드파티 애셋 출처 기록 |

## 설계 원칙

**밸런스 수치는 프리팹이 아니라 ScriptableObject에.**
`CreatureUnit.Initialize()` 가 스폰 시점에 `CreatureDefinition` → 컴포넌트로 스탯을 흘려줍니다.
공룡을 재조정할 때 프리팹을 열 필요가 없습니다.

**씬은 코드로 생성.**
`.unity` YAML을 손으로 편집하거나 머지하는 건 고통입니다.
[`BattleSceneBuilder`](Assets/Editor/BattleSceneBuilder.cs) 를 수정하고 메뉴를 다시 실행하세요.

**의존성은 최소로.**
legacy Input Manager와 uGUI만 사용합니다. uGUI(`com.unity.ugui` 2.5.0)는 Unity 6.5 3D 템플릿에
포함되지 않아 `Packages/manifest.json` 에 명시했습니다 — 그 외 추가 패키지는 없습니다.
Input System / TMP / URP 이관은 M4 항목입니다.

## 라이선스

코드: 미정. 애셋: [ATTRIBUTIONS.md](ATTRIBUTIONS.md) 참고.
