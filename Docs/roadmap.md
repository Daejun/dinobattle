# 로드맵

## 현재 상태 — M0 스캐폴드 ✅

- [x] Unity 6.5 (`6000.5.x`) 프로젝트 구조
- [x] 데이터 모델 (`CreatureDefinition`, `CreatureRoster`, `BattleLoadout`)
- [x] 전투 AI (`CreatureBrain`, `CreatureLocomotion`, `MeleeAttack`, `Health`)
- [x] 매치 생명주기 (`BattleManager`: 배치 → 전투 → 결과)
- [x] 배치 컨트롤러 + 예산 시스템
- [x] 관전 카메라 (궤도/줌/팬, 배속)
- [x] HUD (로스터, 팀 전환, 언두, 시작, 배속, 결과)
- [x] 씬/샘플 콘텐츠 코드 생성기
- [x] Android 헤드리스 빌드 파이프라인
- [x] 문서 + 애셋 소스 조사

**다음 액션:**

1. [`setup.md`](setup.md) 대로 Unity 6.5 설치
2. 에디터 메뉴 `Dino Battle > 1. Generate Sample Content` → `2. Build Battle Scene` → Play
3. (권장) [`setup.md` 8번](setup.md#8-unity-mcp-연동-선택-권장) — CoplayDev Unity MCP 연동.
   Claude Code가 실제 컴파일 에러를 읽고 메뉴를 직접 실행할 수 있게 되어
   "컴파일 검증 불가" 제약이 해소됩니다. 공식 Unity MCP는 유료라 제외했습니다

---

## M1 — "실제로 공룡처럼 보인다" ✅

- [x] [Quaternius Animated Dinosaur Pack](https://quaternius.com/packs/animateddinosaurs.html) (CC0) 임포트
- [x] Generic 리그, 루트 모션 off, 바운즈 기반 자동 스케일
- [x] `Speed` 블렌드 트리 + `Attack`/`Die` 트리거 Animator Controller 종별 생성
- [x] 6종 프리팹 비주얼 교체 — 로스터를 팩이 제공하는 종에 맞춰 재정의
- [x] 사운드 — 외부 팩 대신 코드 합성 (물기/포효/사망 × 대·소)
- [x] 아레나 배경 — 언덕·바위·지면 변화·거리 안개
- [x] 자동 프레이밍 관전 카메라

CC0 합성 오디오라 `ATTRIBUTIONS.md` 항목은 모델 팩만 필요합니다.

## M1-b — 이전 M1 계획 (참고용)

플레이스홀더 캡슐을 실물로 교체.

- [ ] [Quaternius Animated Dinosaur Pack](https://quaternius.com/packs/animateddinosaurs.html) (CC0) 임포트
- [ ] Generic 리그 임포트 설정, 루트 모션 off
- [ ] `Speed`(float) / `Attack`(trigger) / `Die`(trigger) 파라미터로 Animator Controller 구성
- [ ] 6종 프리팹의 비주얼 교체 (루트 컴포넌트 배선은 유지)
- [ ] [CC0-Public-Domain-Sounds](https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds) 에서
      포효 / 물기 / 타격 / 사망 사운드 배치
- [ ] `ATTRIBUTIONS.md` 갱신

상세: [`assets.md`](assets.md)

## M2 — "전투가 재밌다"

- [ ] 사망 시 래그돌 전환 (현재는 `freezeRotation` 해제만)
- [ ] 타격 VFX — 피, 먼지, 히트 플래시
- [ ] 카메라 연출: 킬 캠, 마지막 공룡 자동 포커스
- [ ] 물기 시 targeting을 부위별로 (머리/꼬리 히트박스)
- [ ] 배틀 로그 — "T-Rex가 Velociraptor를 물어 죽였다"
- [ ] 밸런스 1차 패스: 배속 x4로 각 매치업 10회 반복 후 수치 조정

## M3 — "계속 플레이할 이유"

- [ ] 캠페인 / 챌린지 모드 — 정해진 적 구성을 정해진 예산으로 격파
- [ ] 승리 시 공룡 언락
- [ ] 저장 데이터 (`PlayerPrefs` → JSON)
- [ ] 로스터 확장: Giganotosaurus, Carnotaurus, Pteranodon(공중 유닛)
- [ ] 변종 시스템 — Bio / Zombie / Armored 접두어로 스탯 모디파이어

## M2 — "전투가 재밌다" (부분 완료)

- [x] 사망 애니메이션 (래그돌 대신 — 레퍼런스도 래그돌을 쓰지 않습니다)
- [x] Reynolds steering 기반 전투 이동 (Arrive / Pursue / Separation)
- [x] 체급 상호작용 — 작은 개체가 큰 개체에 기어올라 물어뜯기
- [x] 전투 사운드 (합성)
- [x] 자동 프레이밍 카메라
- [ ] 타격 VFX — 피, 먼지, 히트 플래시
- [ ] 배틀 로그 — "T-Rex가 Velociraptor를 물어 죽였다"
- [ ] 밸런스 패스: 배속 x4로 각 매치업 10회 반복 후 조정

## M4 — "출시 가능"

- [ ] **LTS로 이관** — 6.5는 Supported Update라서 6.6이 나오면 지원이 끊깁니다.
      출시 전에 그 시점의 최신 LTS로 올리고 `Tools/check-project.sh` 의
      `EXPECTED_UNITY_MINOR` 와 `Docs/setup.md` 를 함께 갱신
- [ ] **Link Time Optimization (ThinLTO) 활성화** — Player > Android > Publishing Settings.
      시작 시간·프레임 평균 5% 개선. 6.5 신규 옵션이라 현재는 수동 토글
- [ ] On-Tile Post Processing 검토 (6.5 신규, 모바일 후처리 최적화)
- [ ] URP 도입 + 모바일 품질 프로파일
- [ ] TextMeshPro 로 HUD 이관
- [ ] Input System 으로 입력 이관
- [ ] 실기 성능 프로파일링 (목표: 중급기에서 공룡 20마리 60fps)
- [ ] 아이콘, 스플래시, 스토어 스크린샷
- [ ] 키스토어 생성 + AAB 서명 빌드
- [ ] 개인정보처리방침 (광고/분석 넣을 경우 필수)

## 건틀릿 모드 (계단 아레나) — 설계 완료, 미구현

계단식 판형 아레나를 층별로 올라가며 싸우는 신규 모드. 설계와 단계별 계획은
**[`gauntlet-mode.md`](gauntlet-mode.md)** 에 있습니다.

**착수 전에 반드시 그 문서 1절과 2절을 읽으세요.** 현재 로코모션은 수평 추진만 하고
(`ApplySteering` 의 y 성분이 0) 접지가 끊기면 조향이 멈춰서, **말 그대로의 계단은 오르지
못합니다.** 1단계가 경사로 프로토타입 게이트인 이유이고, 여기서 실패하면 모드 설계가
바뀝니다.

## 알려진 UX 문제 (고칠 것)

- [ ] **승리 화면 글씨가 공룡을 가림.** 소유자 보고 (2026-07-26). 결과 패널이 화면 중앙을
      덮는데, 같은 순간 카메라는 승자를 클로즈업하고 이긴 팀은 춤을 춥니다 — 보여주려고 만든
      연출을 UI가 정면에서 가립니다. 4살 플레이테스트에서 "이겼는데 그냥 까만 네모만 떠"
      라고 했던 것과 같은 자리의 문제이고, 그때는 연출을 추가했지 패널을 비켜주지는
      않았습니다. `BattleSceneBuilder` 의 `resultPanel` 앵커 문제라 씬 빌더에서 고칩니다.
      후보: 패널을 화면 상단/하단 띠로 옮기기, 배경 반투명도 낮추기, 춤추는 동안 지연 표시.

## 나중에 검토할 것

| 항목 | 트리거 |
|---|---|
| NavMesh (`com.unity.ai.navigation`) | 장애물 있는 아레나를 만들 때 |
| 공룡 조작 모드 (1인칭/3인칭) | 관전 모드가 완성된 뒤 |
| 멀티플레이 로스터 대결 | 싱글이 재미있어진 뒤에만 |
| 근접 스캔용 공간 그리드 | `budgetPerTeam` > 3000 또는 `maxPerTeam` > 40 — [`performance.md` P1](performance.md) |
| ECS / Job System | 공룡 50마리 이상이 필요해질 때 (**그리드를 먼저** 검토 — `performance.md`) |
| Addressables | 앱 용량이 150MB를 넘을 때 |

측정해두고 의도적으로 미룬 성능 항목은 트리거와 함께 **[`Docs/performance.md`](performance.md)** 에
있습니다. 성능 작업을 시작하기 전에 그 문서의 트리거부터 확인하세요.
