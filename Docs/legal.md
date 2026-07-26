# 저작권 / 법적 주의사항

## 마블 캐릭터는 사용할 수 없습니다

레퍼런스 영상 제목에 스파이더맨, 베놈, 캡틴아메리카가 들어가 있지만, 그 영상 자체가
Disney/Marvel의 저작권과 상표를 침해한 상태입니다. YouTube에서 문제없이 유지되는 것은
단속이 안 됐을 뿐이며, **앱스토어는 다릅니다.**

Google Play에 마블 캐릭터가 포함된 앱을 올리면:

- 상표권 신고로 즉시 내려갑니다
- 개발자 계정 정지 가능 (누적 위반 시 영구)
- Disney의 IP 단속은 특히 적극적입니다

## 대신 할 것

**오리지널 공룡 + 창작 변종.** 이미 그 방향으로 스캐폴드되어 있습니다:

- 실제 공룡 종(T-Rex, Triceratops, Ankylosaurus…)은 **자연물이라 저작권이 없습니다**
- "Bio T-Rex" 같은 변종은 직접 만든 창작물이면 문제없습니다
- 하지만 **쥬라기 공원의 "Indominus Rex", "I-Rex" 같은 이름은 Universal의 상표**입니다 — 피하세요

안전한 변종 네이밍 예시:

| 안전 ✅ | 위험 ❌ |
|---|---|
| Bio T-Rex, Alpha Rex, Cyber Rex | Indominus Rex, Indoraptor |
| Toxic Raptor, Armored Rex | Blue (쥬라기 월드 랩터 이름) |
| Ancient King, Apex Predator | Godzilla, Kaiju (Toho 상표) |
| Megarachne (실존 멸종 속명) | Distortus Rex (쥬라기 월드 리버스) |

### 현재 리포지토리에 들어 있는 상표명 (배포 전 반드시 변경)

소유자가 **본인 폰에만 설치하는 빌드**를 위해 요청해서 들어와 있습니다. 상표의 문제는
"상거래에서의 사용"이므로 개인 기기 한정 빌드에는 해당하지 않지만, 공개 배포 시점에는
그 근거가 사라집니다.

| 현재 이름 | 출처 | 대체 후보 |
|---|---|---|
| `Indominus Rex` | 쥬라기 월드 (Universal) | Alpha Hybrid, Pale Tyrant |
| `Distortus Rex` | 쥬라기 월드 리버스 (Universal) | Malformed Rex, Failed Tyrant |

둘 다 `Assets/Editor/CreatureBlueprints.cs` 의 `BossBlueprints` 에 있고 이름 문자열 하나씩만
바꾸면 됩니다. **모델 자체는 문제없습니다** — Quaternius T-Rex를 코드로 변형한 것이라
서드파티 애셋이 아닙니다. 상표는 이름에만 붙습니다.

반면 `Megarachne` 는 실존 멸종 속명(한때 사상 최대 거미로 기재됨)이라 자연물 이름이며
그대로 배포 가능합니다.

## 애셋 라이선스

[`assets.md`](assets.md) 의 체크리스트를 따르세요. 요약:

- **CC0** — 자유. 출처 표기도 불필요. 최우선 선택.
- **CC-BY** — 자유. 단 출처 표기 필수 → `ATTRIBUTIONS.md` 에 기록
- **CC-BY-SA** — ❌ 게임에 쓰지 마세요. 파생물까지 같은 라이선스로 공개해야 합니다
- **CC-NC** — ❌ 비상업 전용. 광고·유료화 불가
- **Unity Asset Store 무료 애셋** — Asset Store EULA 적용. 대체로 사용 가능하나
  재배포 금지 조항이 있으므로 소스 리포지토리 공개 시 주의

## 사운드 특별 주의

영화/게임에서 리핑한 사운드(쥬라기 공원 T-Rex 포효 등)는 **절대 사용 불가**입니다.
Content ID 및 오디오 지문 인식으로 탐지됩니다.
[`assets.md`](assets.md) 의 "공룡 포효 만드는 실전 팁"대로 CC0 동물 소리를 합성하세요.

## Play Store 등록 시 추가 필요 사항

- [ ] 개인정보처리방침 URL (광고 SDK / 분석 도구를 넣으면 필수)
- [ ] 데이터 보안 섹션 작성
- [ ] 콘텐츠 등급 설문 (공룡 전투는 폭력성 항목에 해당 — 만화적 폭력으로 신고)
- [ ] 타겟 연령 설정 (아동 대상이면 Families 정책 추가 적용 — 광고 규제가 엄격해집니다)
