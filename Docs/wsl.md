# WSL 하이브리드 워크플로

**Unity Editor는 Windows에서, 나머지 툴체인은 WSL에서.**

## 왜 하이브리드인가

Unity Editor 자체를 WSL로 옮기지 않은 이유:

| 문제 | 내용 |
|---|---|
| **미지원 환경** | Unity는 Linux 에디터를 지원하지만 WSL은 지원 대상이 아닙니다. WSLg의 GPU는 D3D12 매핑 기반이라 Unity가 요구하는 OpenGL/Vulkan에서 크래시가 잦습니다 |
| **파일 I/O** | 프로젝트가 `/mnt/c/`에 있으면 `Library/`의 수십만 개 파일이 9p 브리지를 타면서 임포트·컴파일이 몇 배로 느려집니다 |
| **ext4로 옮기면?** | 빨라지지만 Windows 쪽 경로가 `\\wsl$\...` UNC가 되어 Windows Unity로 빌드가 불안정해집니다 |

반면 WSL이 확실히 유리한 것:

- `git` / `git-lfs`
- 애셋 파이프라인 — `ffmpeg`, `sox`, `blender --background`
- bash 스크립트, Linux CI 러너와 동일한 환경
- `grep`/`find` 기반 정적 검증 (Unity 없이 코드 문제를 잡는 유일한 수단)

**결론: 프로젝트는 `/mnt/c/Users/pdaej/dino_battle`에 그대로 두고, WSL에서 같은 경로로 접근합니다.**
WSL은 Windows 실행 파일을 직접 호출할 수 있으므로 빌드 스크립트도 bash에서 정상 동작합니다.

## 최초 세팅 (한 번만)

```bash
# WSL 진입
wsl -d Ubuntu-24.04

cd /mnt/c/Users/pdaej/dino_battle

# 의존성 확인
./Tools/fetch-assets.sh --deps

# 부족한 것 설치
sudo apt update && sudo apt install -y git git-lfs ffmpeg
```

Blender는 애니메이션 리타깃/제작이 필요해질 때만:

```bash
sudo snap install blender --classic
```

## 스크립트 실행 권한

프로젝트가 `/mnt/c`에 있으면 기본적으로 실행 비트가 유지되지 않습니다.
`bash <script>` 로 호출하면 항상 동작합니다:

```bash
bash Tools/check-project.sh
```

실행 비트를 쓰고 싶으면 `/etc/wsl.conf` 에 metadata를 켜세요:

```ini
[automount]
options = "metadata,umask=22,fmask=11"
```

이후 `wsl --shutdown` 하고 다시 진입하면 `chmod +x Tools/*.sh` 가 유지됩니다.

## 스크립트 목록

| 스크립트 | 용도 |
|---|---|
| `Tools/check-project.sh` | **정적 검증.** Unity 없이 코드 문제를 잡습니다 (아래 참고) |
| `Tools/build-android.sh` | Android APK/AAB 헤드리스 빌드. WSL에서 Windows `Unity.exe` 호출 |
| `Tools/fetch-assets.sh` | CC0 애셋 다운로드 / 라이선스 정보 / 의존성 확인 |
| `Tools/convert-audio.sh` | 사운드 정규화 + OGG 변환 (모노, 44.1kHz, 라우드니스 정렬) |
| `Tools/build-android.ps1` | 같은 빌드의 PowerShell 버전 (Windows 전용으로 쓸 때) |

`Tools/local.build.props` 는 **PowerShell과 bash가 공유**합니다. Windows 경로로 한 줄만 적으면
bash 쪽은 `wslpath` 로 자동 변환합니다:

```
UnityPath=C:\Program Files\Unity\Hub\Editor\6000.5.0f1\Editor\Unity.exe
```

## check-project.sh — 이게 왜 중요한가

이 워크스페이스에는 **C# 컴파일러가 없습니다** (Unity가 컴파일러입니다).
그래서 에디터를 열기 전까지 드러나지 않는 종류의 버그를 grep으로 잡습니다:

1. **`SerializedObject.FindProperty("x")` 가 존재하지 않는 필드를 가리키는 경우**
   — `null` 을 조용히 반환한 뒤 메뉴 명령 실행 시점에 `NullReferenceException`.
   에디터 생성 스크립트에서 필드명을 바꿨을 때 가장 흔히 터지는 문제입니다.
2. **Unity 6에서 이름이 바뀐 API** — `Rigidbody.velocity` → `linearVelocity`,
   `FindObjectOfType` → `FindAnyObjectByType`
3. **`DinoBattle.*` 네임스페이스 누락**
4. **Animator 파라미터 이름 드리프트** — 코드의 `"Speed"` / `"Attack"` 문자열과
   `Docs/assets.md` 의 아티스트 인계 문서가 어긋나는 것
5. **리포지토리 위생** — `Library/` gitignore 여부, git-lfs 초기화 여부

코드를 수정한 뒤에는 항상 실행하세요:

```bash
bash Tools/check-project.sh
```

## 빌드

```bash
bash Tools/build-android.sh          # APK
bash Tools/build-android.sh --aab    # Play Store 번들
```

스크립트가 하는 일:
1. `local.build.props` → `UNITY_PATH` 환경변수 → WSL 내 네이티브 Linux Unity →
   `/mnt/c/Program Files/Unity/Hub/Editor` 순서로 에디터를 찾습니다
2. Windows `Unity.exe` 를 찾으면 `wslpath -w` 로 프로젝트/로그 경로를 Windows 형식으로 변환
3. 프로젝트가 ext4에 있어 UNC 경로(`\\wsl$\...`)가 나오면 **에러로 중단**합니다 —
   조용히 실패하는 빌드보다 낫습니다
4. 실패 시 로그 마지막 60줄을 출력

## 애셋 파이프라인

```bash
# 무엇을 받을 수 있는지 확인 (다운로드 없음)
bash Tools/fetch-assets.sh --list

# CC0 사운드 라이브러리 클론 (확인 프롬프트 있음)
bash Tools/fetch-assets.sh --sounds

# 골라낸 파일을 Unity용으로 변환
bash Tools/convert-audio.sh .assets-cache/CC0-Public-Domain-Sounds/beast_or_animal/roar.wav Assets/Audio/SFX/

# 폴더 단위 일괄 변환
bash Tools/convert-audio.sh --batch .assets-cache/picked/ Assets/Audio/SFX/
```

다운로드는 `.assets-cache/` 에 쌓이고 이 폴더는 gitignore됩니다.
**실제로 쓰는 파일만** `Assets/` 로 복사하세요 — 클론 전체를 넣으면 Unity가 전부 임포트하면서
리포지토리가 폭발합니다.

## adb (실기 테스트)

WSL에는 USB 장치가 직접 노출되지 않습니다. 두 가지 방법:

**A. Windows adb를 그대로 호출** (간단, 권장)

```bash
alias adb='/mnt/c/Users/pdaej/AppData/Local/Android/Sdk/platform-tools/adb.exe'
adb devices
adb install Build/Android/dino-battle-*.apk
```

Unity가 Android Build Support 모듈과 함께 설치한 SDK 경로를 쓰세요.
Unity Hub 설치 시 경로는 보통
`C:\Program Files\Unity\Hub\Editor\<version>\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe` 입니다.

**B. usbipd-win으로 USB 패스스루** (WSL 안에서 네이티브 adb를 쓰고 싶을 때)

```powershell
winget install usbipd
usbipd list
usbipd attach --wsl --busid <busid>
```

목적이 APK 설치뿐이라면 A가 훨씬 간단합니다.

## 주의사항

- **줄바꿈**: `.gitattributes` 가 `*.sh eol=lf`, `*.ps1 eol=crlf` 로 강제합니다.
  `.sh` 파일이 CRLF가 되면 `bad interpreter` 에러가 납니다
- **`git lfs install`**: 이 리포지토리는 `--local` 로 초기화되어 있습니다 (전역 설정 미변경).
  다른 머신에서 클론하면 다시 실행해야 합니다
- **WSL의 git-lfs**: 현재 WSL에는 git-lfs가 없습니다. WSL에서 애셋을 커밋할 계획이면
  `sudo apt install -y git-lfs && git lfs install --local` 을 실행하세요.
  Windows 쪽 git에는 3.7.1이 설치되어 있습니다
