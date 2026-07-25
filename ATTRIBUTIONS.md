# 출처 표기 (Attributions)

이 프로젝트에 사용된 서드파티 애셋 목록입니다.
**애셋을 추가할 때마다 여기에 한 줄 기록하세요.** CC0라서 표기가 법적으로 불필요해도
나중에 라이선스를 재확인할 때 이 파일이 유일한 근거가 됩니다.

애셋 후보 조사 결과는 [`Docs/assets.md`](Docs/assets.md) 를 보세요.

---

## 3D 모델 / 애니메이션

| 애셋 | 제작자 | 라이선스 | 출처 | 사용처 |
|---|---|---|---|---|
| Dinosaur Animated Pack (Dec 2018) | [@Quaternius](https://www.patreon.com/quaternius) | **CC0 1.0** | https://quaternius.itch.io/animated-lowpoly-dinosaurs | Trex, Triceratops, Velociraptor, Stegosaurus, Parasaurolophus 모델 + `Idle/Walk/Run/Attack/Death/Jump` 애니메이션 |

CC0라 표기 의무는 없지만 출처 추적을 위해 기록합니다. 원본 `License.txt` 는
`.assets-cache/quaternius-dinosaurs/` 에 함께 보관돼 있습니다 (gitignore).

같은 팩의 Apatosaurus FBX는 임포트돼 있으나 로스터에는 없습니다 — 대형 유닛 후보로 보관 중입니다.
이 파일은 자체 Death 클립 대신 `Stegosaurus_Death` 를 담고 있는 팩 자체의 오류가 있으며,
임포터가 종 접두어를 무시하고 동작 접미어로 매칭하므로 문제없이 동작합니다.

## 사운드 / 음악

| 애셋 | 제작자 | 라이선스 | 출처 | 사용처 |
|---|---|---|---|---|
| `sfx_bite/roar/death_{small,large}.wav` | 이 프로젝트 (절차적 생성) | 해당 없음 (자체 생성물) | [`ProceduralAudioBuilder.cs`](Assets/Editor/ProceduralAudioBuilder.cs) | 물기·포효·사망 효과음 |

**서드파티 오디오를 쓰지 않았습니다.** 모든 효과음은 `Dino Battle > 5. Generate Creature Audio`
가 합성해 WAV로 씁니다. 녹음 음원으로 교체할 때는 [`Docs/assets.md`](Docs/assets.md) 의
CC0 소스를 쓰고 이 표에 한 줄 추가하세요.

## 텍스처 / VFX

| 애셋 | 제작자 | 라이선스 | 출처 | 사용처 |
|---|---|---|---|---|
| _(없음 — 머티리얼은 전부 단색, 코드 생성)_ | | | | |

---

## 기록 예시

```
| Animated Dinosaur Pack | Quaternius | CC0 1.0 | https://quaternius.com/packs/animateddinosaurs.html | 공룡 6종 모델 + 애니메이션 |
| CC0 Deep Monster Roar | rubberduck | CC0 1.0 | https://opengameart.org/content/cc0-deep-monster-roar | T-Rex 포효 SFX |
```

## Creature audio

Source: [CC0-Public-Domain-Sounds](https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds)
by lavender.pet — CC0 1.0 Universal (public domain, no attribution required; recorded here anyway).

Real animal recordings, pitched down. That is how films do it, and it is the only approach of the
three tried that sounded like an animal: synthesis got the spectrum right and the character wrong,
and the pack's ready-made "creature" SFX are designed sci-fi monsters rather than throats.

Sources live in `Assets/Editor/AudioSources/` — an Editor folder, so Unity keeps them out of the
build. `Dino Battle > 5. Generate Creature Audio` rebuilds the six voices from them.

| In-game slot | Source | Pitch | Result |
|---|---|---|---|
| `sfx_roar_large`  | `beast_or_animal/Growl 1.wav` | 0.38x | 2.24s, centroid 89 Hz |
| `sfx_roar_small`  | `beast_or_animal/Growl 2.wav` | 0.68x | 0.81s, centroid 166 Hz |
| `sfx_death_large` | `beast_or_animal/Voice 3.wav` | 0.42x | 1.67s, centroid 183 Hz |
| `sfx_death_small` | `beast_or_animal/Voice 1.wav` | 0.72x | 0.62s, centroid 284 Hz |
| `sfx_bite_large`  | `angerdog/angerdog2.ogg`      | 0.40x | 0.40s, centroid 420 Hz |
| `sfx_bite_small`  | `angerdog/angerdog2.ogg`      | 0.95x | 0.13s, centroid 640 Hz |

The two bites share a source deliberately: built from different takes they came out with the small
one darker than the large, because the loudest slice of one happened to be duller. Sharing a source
makes the size difference a property of the pitch alone. Every pair is now verified small-brighter:
roar 1.86x, bite 1.52x, death 1.56x.
