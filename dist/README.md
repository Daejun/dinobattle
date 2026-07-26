# dist

빌드된 APK를 넣어두는 곳입니다. **`Build/` 와는 다릅니다.**

| | `Build/` | `dist/` |
|---|---|---|
| 무엇 | 에디터가 빌드할 때마다 새로 쓰는 작업 산출물 | 남겨두기로 한 릴리스 |
| git | 추적 안 함 (`.gitignore`) | 추적함, **Git LFS** 경유 |
| 갱신 | 메뉴 3번 누를 때마다 | 사람이 의도적으로 복사할 때만 |

`Build/` 를 그대로 추적하면 빌드할 때마다 26 MB짜리 바이너리 diff가 하나씩 쌓입니다.
남길 가치가 있는 버전만 여기로 복사하세요.

## 설치

```bash
adb install -r dist/dino-battle-1.0.apk
```

Android 8.0(API 26) 이상, ARM64. IL2CPP 빌드입니다.
Galaxy Z Fold7 / Android 16에서 설치·실행 확인했습니다.

## LFS 주의

이 파일은 LFS 포인터로 저장됩니다. 새로 클론했다면 받기 전에:

```bash
git lfs install --local
git lfs pull
```

이걸 안 하면 26 MB APK 대신 세 줄짜리 텍스트 파일이 나옵니다.

## 버전

파일명의 버전은 `ProjectSettings` 의 `bundleVersion` 을 따릅니다
(`AndroidBuilder` 가 `dino-battle-{version}.apk` 로 씁니다).
같은 버전으로 다시 빌드하면 같은 파일명을 덮어쓰므로, 이전 빌드를 남기려면
버전을 올리고 빌드하세요.
