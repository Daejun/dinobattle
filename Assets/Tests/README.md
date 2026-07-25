# Unit tests

Unity의 Test Framework는 asmdef를 자동 생성해야 정상 동작합니다.
손으로 asmdef를 만들면 UnityEngine.TestRunner 참조가 빠져 컴파일이 깨지므로,
여기서는 폴더만 준비해 두었습니다.

## 만드는 방법

1. Window > General > Test Runner
2. EditMode 탭 > "Create EditMode Test Assembly Folder" 클릭 → 이 폴더를 지정
3. Unity가 `DinoBattle.Tests.asmdef` 를 올바른 참조와 함께 생성합니다

## 테스트할 가치가 있는 것

순수 로직이라 Unity 런타임 없이 검증 가능한 부분:

- `BattleLoadout` — 예산 계산(`SpentBy`/`RemainingFor`/`CanAfford`),
  자리 겹침 판정(`IsSpotFree`), `RemoveLast` 의 팀 필터링
- `Health` — 방어 감산 후 최소 1 데미지 보장, `Died` 가 정확히 한 번만 발생
- `CreatureDefinition` — `DamagePerSecond` / `PowerScore` 계산
- `TeamExtensions.Opponent()`

`UnitRegistry` 는 static 상태를 들고 있으므로 테스트마다 `Clear()` 를 `[SetUp]` 에 넣으세요.
