# 애셋 소스 — 모델 / 애니메이션 / 사운드

공룡 전투 시뮬레이터에 실제로 쓸 수 있는 **오픈소스 / 자유 라이선스** 애셋 목록입니다.
라이선스 우선순위: **CC0 > CC-BY > CC-BY-SA**. CC-BY-SA는 파생물까지 전염되므로 게임 애셋으로는 피하세요.

> ⚠️ 참고한 영상에는 마블 캐릭터(스파이더맨, 베놈, 캡틴아메리카)가 등장하지만
> **이는 저작권 침해입니다.** Play Store 등록 시 삭제 사유가 됩니다.
> 오리지널 공룡 + 창작 변종(Bio T-Rex 같은)으로 가세요. [`legal.md`](legal.md) 참고.

---

## 1. 애니메이션이 포함된 공룡 3D 모델 (최우선 추천)

### ⭐ Quaternius — Animated Dinosaur Pack (CC0)

이 프로젝트에 가장 잘 맞습니다. **CC0**라서 출처 표기조차 필요 없고, 상업적 사용 자유입니다.

| 항목 | 내용 |
|---|---|
| 페이지 | https://quaternius.com/packs/animateddinosaurs.html |
| itch.io | https://quaternius.itch.io/animated-lowpoly-dinosaurs |
| 내용 | 공룡 6종, 각각 리깅 + 애니메이션 |
| 애니메이션 | `Idle`, `Walk`, `Run`, `Attack`, `Death`, `Jump` |
| 포맷 | FBX, OBJ, Blend |
| 라이선스 | **CC0** (개인/상업 모두 자유) |

**이 프로젝트에 왜 딱 맞는가:** `Idle`/`Walk`/`Run`/`Attack`/`Death` 5종이
[`CreatureBrain`](../Assets/Scripts/Units/CreatureBrain.cs)의 상태 머신과 1:1로 대응합니다.
`Speed` float 파라미터 하나로 Idle↔Walk↔Run 블렌드, `Attack` 트리거,
사망 시 `Death` — 추가 애니메이션 작업 없이 바로 붙습니다.

### ⭐ Quaternius — Ultimate Animated Animals (CC0)

| 항목 | 내용 |
|---|---|
| 페이지 | https://quaternius.com/packs/ultimateanimatedanimals.html |
| 내용 | 동물 12종, **각각 애니메이션 12종 이상** |
| 애니메이션 | Attack, Death, Kick, Gallop, Walk, Jump 등 |
| 포맷 | FBX, OBJ, glTF, Blend |
| 라이선스 | **CC0** |

공룡 외 대전 상대(곰, 사자 등)를 넣거나, **애니메이션만 리타깃**해서
공룡 리그에 재사용하기 좋습니다. 사족보행 골격이 유사해 Humanoid가 아닌
Generic 리그로도 리타깃 성공률이 높습니다.

### Quaternius 전체 라이브러리
https://quaternius.com/ — 전 애셋 CC0. 환경(암석, 나무, 지형), VFX, 캐릭터 팩까지 있어
아레나 배경까지 한 소스로 통일할 수 있습니다.

### 보조 소스

| 소스 | 링크 | 라이선스 | 비고 |
|---|---|---|---|
| OpenGameArt — CC0 3D Animals/Creatures | https://opengameart.org/content/cc0-3d-animals-creatures | CC0 | Dromaeosaur, Brontosaurus 등 |
| awesome-cc0 (GitHub) | https://github.com/madjin/awesome-cc0 | 큐레이션 | CC0 애셋 종합 인덱스 |
| 3d-resources (GitHub) | https://github.com/devanshutak25/3d-resources | 큐레이션 | 3,400+ 툴/애셋, 12개 섹션 |
| Sketchfab CC0 필터 | https://sketchfab.com/search?features=downloadable&licenses=cc0 | CC0 | **모델별 라이선스 개별 확인 필수** |

---

## 2. 전투 애니메이션 (모델과 분리해서 구하는 경우)

사족보행(quadruped) 공룡은 **Mixamo를 쓸 수 없습니다** — Mixamo는 Humanoid 전용입니다.
선택지는 셋입니다.

1. **모델에 딸린 애니메이션 사용** — Quaternius 팩이 이걸 해결해줍니다. **권장.**
2. **Blender로 직접 제작** — Rigify로 사족보행 리그를 만들고 4~5개 클립만 키프레임.
   전투 시뮬레이터는 카메라가 멀어서 애니메이션 퀄리티 요구가 낮습니다. 현실적인 선택.
3. **Generic 리그 리타깃** — 골격 구조가 비슷한 CC0 사족보행 모델 간에는
   Unity의 Generic Avatar로 리타깃이 가능하지만 본 이름 매핑을 손봐야 합니다.

| 소스 | 링크 | 라이선스 |
|---|---|---|
| OpenGameArt — 2D Raptor Running FBX | https://opengameart.org/content/2d-raptor-running-fbx-animation | CC-BY-SA 4.0 ⚠️ |
| Sketchfab — Animated T-Rex (run/roar/bite/idle/tail attack) | https://sketchfab.com/3d-models/animated-tyrannosaurus-rex-dinosaur-running-loop-38007d947ae74dea83988cb0b08ee053 | 개별 확인 |

---

## 3. 사운드 — 포효 / 타격 / 발소리

### ⭐ CC0-Public-Domain-Sounds (GitHub)

```
https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds
```

| 항목 | 내용 |
|---|---|
| 라이선스 | **CC0 1.0 Universal** |
| 관련 폴더 | `80-CC0-creature-SFX`, `80-CC0-creature-sfx-2`, `beast_or_animal` — 크리처/포효<br>`75-cc0-breaking-falling-hit-sfx` — 타격/충돌<br>`80-CC0-RPG-SFX` — 전투 효과음 |

`git clone` 한 번으로 포효 + 타격 + 사망 사운드가 전부 확보됩니다.
**이 프로젝트 사운드는 여기서 시작하세요.**

### 보조 소스

| 소스 | 링크 | 라이선스 | 비고 |
|---|---|---|---|
| OpenGameArt — CC0 Deep Monster Roar | https://opengameart.org/content/cc0-deep-monster-roar | CC0 | 대형 공룡 포효에 바로 사용 |
| OpenGameArt — CC0 Sounds Library | https://opengameart.org/content/cc0-sounds-library | CC0 | 대용량 종합 팩 |
| OpenGameArt — CC0 Sound Effects | https://opengameart.org/content/cc0-sound-effects | CC0 | |
| CC0 sound library (Gist) | https://gist.github.com/PtrMan/b3ff012785ad9e93f7db1a0f031fc2b2 | 큐레이션 | 동물 사운드 링크 모음 |
| freesound.org (CC0 필터) | https://freesound.org/search/?f=license:%22Creative+Commons+0%22 | CC0 | 계정 필요, 라이선스 필터 걸 것 |

### 공룡 포효 만드는 실전 팁

실제 공룡 울음소리는 아무도 모릅니다. 쥬라기 공원 방식이 표준입니다 — **동물 소리 합성**:

- 저음 베이스: 코끼리 / 사자 / 악어 울음 → 피치 다운 (-6 ~ -12 세미톤)
- 중음 레이어: 호랑이 그르렁 → 리버브
- 고음 어택: 새 / 돼지 비명 → 짧게 클립

Audacity(무료)로 3레이어 믹스하면 15분 안에 그럴듯한 T-Rex 포효가 나옵니다.

---

## 4. 다운로드 후 워크플로

```bash
# 사운드 (CC0)
git clone --depth 1 https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds
```

1. **모델**: Quaternius FBX → `Assets/Art/Models/` 에 드롭
2. **임포트 설정**: Inspector → Rig → Animation Type = **Generic**, Root node 지정
3. **애니메이션 추출**: FBX 하위 클립을 `Assets/Art/Animations/` 로 복사(Ctrl+D)
4. **Animator Controller**: `Speed`(float) + `Attack`(trigger) + `Dead`(bool) 파라미터로 구성
   — [`CreatureBrain`](../Assets/Scripts/Units/CreatureBrain.cs)과
   [`MeleeAttack`](../Assets/Scripts/Units/MeleeAttack.cs)이 기대하는 이름입니다
5. **프리팹**: `Assets/Prefabs/Creatures/Creature_TRex.prefab` 의 플레이스홀더 캡슐 비주얼을
   임포트한 모델로 교체. 루트의 `CreatureUnit` / `MeleeAttack` 배선은 그대로 두세요
6. **사운드**: `Assets/Audio/SFX/` 에 배치 → `MeleeAttack.attackAudio` 에 AudioSource 연결
7. **출처 기록**: 받은 애셋마다 [`ATTRIBUTIONS.md`](../ATTRIBUTIONS.md) 에 한 줄 추가

## 5. 라이선스 체크리스트

애셋을 리포지토리에 커밋하기 전에:

- [ ] 라이선스 원문을 **직접** 확인했는가 (검색 결과 요약이 아니라 배포 페이지에서)
- [ ] CC0 또는 CC-BY인가 (CC-BY-SA / NC / ND는 거부)
- [ ] 상업적 사용이 허용되는가 (Play Store 유료화·광고 시 필수)
- [ ] `ATTRIBUTIONS.md` 에 출처를 기록했는가
- [ ] Git LFS가 처리하는 확장자인가 (`.gitattributes` 참고)
