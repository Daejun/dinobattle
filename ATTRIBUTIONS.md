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
| `sfx_bite/roar/death_{small,large}.wav` | lavender.pet (원본 녹음) | CC0 1.0 | [CC0-Public-Domain-Sounds](https://github.com/lavenderdotpet/CC0-Public-Domain-Sounds) | 물기·포효·사망 효과음 |

실제 동물 녹음을 피치다운해서 만듭니다 — 자세한 대응표와 이유는 아래
[Creature audio](#creature-audio) 를 보세요. `Dino Battle > 5. Generate Creature Audio` 가
`Assets/Editor/AudioSources/` 의 원본에서 6개 음성을 다시 굽습니다.

`ProceduralAudioBuilder.cs` 의 합성 버전은 **더 이상 쓰이지 않습니다.** 스펙트럼은 맞았지만
목소리로 들리지 않았습니다. 코드는 `Dino Battle > Advanced` 아래에 참고용으로 남아 있습니다.

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
| `sfx_bite_large`  | `angerdog/angerdog2.ogg`      | 0.40x | 0.55s, centroid 491 Hz |
| `sfx_bite_small`  | `angerdog/angerdog2.ogg`      | 0.80x | 0.42s, centroid 1111 Hz |

The two bites share a source deliberately: built from different takes they came out with the small
one darker than the large, because the loudest slice of one happened to be duller. Sharing a source
makes the size difference a property of the pitch alone. Every pair is verified small-brighter:
roar 2.11x, bite 2.26x, death 1.71x.

**The bites are cut differently from the rest.** The other four take the loudest stretch of the
recording, which is right for a sound an animal holds. For a bite it was wrong: the loudest part of
a dog take is the middle of a sustained snarl, so the finished clip peaked 91% of the way through
and took 355ms to get there — the envelope of a growl, on the sound the game plays most often.

They are now cut at the sharpest *rise* in the source rather than the highest level — each of the six
dog takes contains real barks that come up from silence in 7-10ms.

The first attempt at the envelope then overcorrected into the opposite fault. Riding the clip down to
-45dB left the heavy bite with 90ms of audible content and the light one with 39ms, and 39ms of
anything is a click; it sounded like two sticks tapping rather than a jaw. A bark already decays on
its own, so there is now no forced decay at all — just a 2ms attack and a 25ms fade at the end, with
the window length deciding how much of the bark is kept.

Measured across the three versions, audible content (envelope above -20dB of peak):

| | heavy bite | light bite |
|---|---|---|
| loudest-slice (growl envelope) | 90ms, peak 91% through | 39ms, ended at 53% of peak |
| onset cut + forced -45dB decay | 90ms, peak 10% through | 39ms, peak 15% through |
| onset cut, natural decay | **186ms, peak 7% through** | **100ms, peak 5% through** |

The attack that survives is longer than the 2ms envelope — 40ms heavy, 17ms light — because the
recording's own rise is stretched by the pitch drop. That is worth keeping: a bigger jaw does close
more slowly.

## Boss models

**Wyrm Titan** is Quaternius's `SK_Dragon`, from the
[Animated Monsters pack](https://quaternius.com/packs/lowpolyanimatedmonsters.html) — CC0 1.0
Universal, same author as the dinosaurs and the vegetation. In `Assets/Art/Models/Dragon.fbx`,
2180 vertices over 27 bones, with `Dragon_Attack`, `Dragon_Attack2`, `Dragon_Death`,
`Dragon_Flying` and `Dragon_Hit` clips.

This entry was missing until now. The model went in with the boss-mode commit and nothing recorded
where it came from — which is exactly the failure this file exists to prevent, since CC0 or not, an
asset whose provenance nobody wrote down is an asset nobody can re-verify later. Identified after
the fact by matching its `DragonArmature|Dragon_*` clip naming against the Quaternius catalogue.

**Megarachne** is Quaternius's `SK_Spider`, from the
[Easy Enemy pack](https://quaternius.com/packs/easyenemy.html) — CC0 1.0 Universal. Cached at
`.assets-cache/easyenemy/` with the pack's own `License.txt`, and copied into the project by
`Dino Battle > 4b`. 5318 vertices over 39 bones, with Idle, Walk, Attack, Death and Jump clips.

Chosen after failing to find a scorpion, which is what was actually wanted. There is no animated
scorpion that is both licence-acceptable and downloadable without an account: Quaternius has none
across 82 packs and 4078 models, OpenGameArt's only one is CC-BY-SA, Sketchfab has zero under CC0
and gates its CC-BY ones behind a login, and every itch.io and Asset Store option is paid. A spider
was the nearest thing in spirit — a giant arthropod, eight legs, a silhouette nothing else in the
roster has — and being Quaternius it matches the dinosaurs exactly in style.

The name is a real extinct genus, once described as the largest spider that ever lived before being
reclassified as a sea scorpion. Natural names carry no trademark, unlike the two below.

**Indominus Rex** and **Distortus Rex** are not downloaded models. Both are the Quaternius T-Rex above, reshaped by
`CreatureSkinBuilder` with different `BodyShape` entries — no third-party asset involved, so nothing
to attribute. The NAMES, however, are Universal trademarks; see `Docs/legal.md`. They are present
only for a build that goes on the owner's own phone, and must be renamed before any public release.

## Arena vegetation

Models: **Quaternius** — [Ultimate Stylized Nature Pack](https://quaternius.com/packs/ultimatestylizednature.html),
CC0 1.0 Universal (public domain). Same author as the dinosaur pack, which is why the scenery and the
creatures look like they belong in the same game.

Fetched from the mirror [walterpalladino/godot-quaternius-ultimate-stylized-nature](https://github.com/walterpalladino/godot-quaternius-ultimate-stylized-nature)
(repackaging MIT © 2025 Walter H. Palladino; the models themselves remain Quaternius CC0).

17 FBX files, 588 KB total: `PalmTree_1..5`, `Plant_1..2`, `Bush`, `Bush_Large`, `Bush_Small`,
`Grass_Large`, `Grass_Small`, `Rock_1..5`. In `Assets/Art/Models/Nature/`.

**Textures were deliberately not downloaded** — the pack's bark maps are over 20 MB each, and the
creatures are flat-shaded, so photographic bark beside them would look like two games spliced
together. `BattleSceneBuilder.PaintModel` assigns flat colours per material slot instead, splitting
foliage from wood by material name. Colours are quantised to a 12-step palette so several hundred
scenery objects share 21 materials and still static-batch.

## UI font

**Nanum Gothic** (`NanumGothic-Regular.ttf`) — designed by Sandoll Communications for Naver,
released under the [SIL Open Font License 1.1](https://scripts.sil.org/OFL). Fetched from
[google/fonts](https://github.com/google/fonts/tree/main/ofl/nanumgothic). In `Assets/Fonts/`.

The OFL permits bundling in an application without attribution in the UI; this entry is the record,
and the only real obligation is that the font is not sold on its own and keeps its name.

Bundled rather than relying on the system font because every string in this game is Korean and
Unity's builtin face is Arial, which has no Hangul at all. On Windows the editor hides that by
borrowing glyphs from Malgun Gothic; Android makes no such promise — the fallback chain varies by
manufacturer, and when it comes up empty every label draws as blank boxes. 2 MB buys the guarantee.

Verified against the font's own cmap: all 11,172 Hangul syllables plus ASCII, and every character
that appears in the HUD.
