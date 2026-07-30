# Sound Effects — Running List

Audio gets its own pass later. This tracks every moment that needs sound, what
temporary clip (already in the repo) is wired today, and what we actually want.
Temp wiring is done by `Tools > RingSport > Setup Juice Polish`
([JuicePolishSetup.cs](Assets/Scripts/Editor/JuicePolishSetup.cs)) — it only fills
empty fields, so replacing a temp clip in the Inspector sticks.

## Wired this pass (temporary stand-ins)

| Moment | Temp clip (in repo) | What we actually want | Plays from |
|---|---|---|---|
| Run death impact | `UI/ui-impact.wav` | Meaty thud layered with a dog yelp/whimper | `GameManager.DeathImpactSequence` |
| Landing thump | `Dog/dog-footsteps3.wav` | Soft paw-on-dirt thump, 3–4 round-robin variants | `PlayerController.OnLanded` |
| Countdown ticks (3-2-1) | `Reward/reward-pop.wav` (pitch climbs 1.0→1.12) | Short dry UI tick, low pitch | `UIManager.CountdownRoutine` |
| "GO!" | `Reward/reward-bell.wav` (pitch 1.15) | Bright start horn / sharp whistle blast | `UIManager.CountdownRoutine` |
| Flee-attack catch | `Decoy bite/bite-tackle.wav` | Keep; layer `decoy-scream1.wav` on top | `MiniLevelFleeAttack.DoCatch` |
| Stop-attack whistle | Malbers `Whistle Stop.wav` | Keep — it's a real dog whistle | `MiniLevelStopAttack.DoSuccessfulStop` |
| Face-attack QTE tap | `Decoy bite/bite-impact1.wav` | Keep; round-robin `bite-impact1-5` per encounter | `MiniLevelFaceAttack.ResolveCorrectTap` |
| Face-attack catch | `Decoy bite/bite-tackle.wav` | Keep; layer `decoy-scream2.wav` | `MiniLevelFaceAttack.DoCatch` |
| Food-refusal collect | `Reward/reward-collect.wav` | Distinct chime so it doesn't read as a coin | `MiniLevelFoodRefusal.OnCollectibleCollected` |
| Death sting (existing) | `Meme/meme-dundundun.wav` (now delayed to panel) | Keep | `GameManager.ShowGameOverDelayed` |

## Needed — no clip in the repo fits yet

| Moment | What we want | Where it will play |
|---|---|---|
| Death whimper layer | Dog yelp/whimper, 2 variants | `GameManager.DeathImpactSequence` |
| Finish line | Crowd cheer + party horn under the confetti (currently only the random meme sting) | `LevelManager.FinishMomentRoutine` |
| Love-note pickup | Unique romantic chime (currently shares the mega-coin squeaker) | `LoveNoteCollectible.Collect` (Tier 2) |
| Near-miss whoosh | Airy whoosh, pitch by clearance | `Obstacle.OnPlayerCollision` success branch (Tier 2) |
| Face-attack bullet time | Riser into the freeze + heartbeat/tick loop while the window drains | `MiniLevelFaceAttack.EnterReveal` (Tier 2) |
| Secret-note finale | Paper unfold + fanfare + confetti pops | `SecretNotePanel.Open` (Tier 2) |
| Palisade | Wall-hit thud, per-tap thock (rising), timer tick under 1.5s | `PalisadeMinigame` (Tier 2) |
| Simon Says | Per-pose tone ladder, correct chime, wrong buzz | `MiniLevelPositionsSimonSays` (Tier 3) |
| Steak splat / whoosh | Splat on hit, whoosh per near-missed steak | `MiniLevelFoodRefusal` (Tier 3) |
| UI button click | Global soft click on every button press | future `JuicyButton` (Tier 3) |
| Sprint exhausted | Dog panting when stamina empties | `PlayerStaminaSystem` (Tier 3) |

## Ideas from clips already in the repo (unused, earmarked)

- `Reward/reward-coin.wav`, `reward-coin2.wav`, `reward-coin3.wav` — unused; could
  round-robin as the small-coin pickup instead of `reward-collect` (the pitch
  ladder in `LevelManager.PlayCollectSound` works with any short clip).
- `Decoy bite/bite-impact2-5`, `bite-munch`, `bite-fall`, `decoy-scream1/2`,
  `decoy-running` — earmarked for catch layering and ragdoll ground contact.
- `Dog/dog-footsteps1-18` — footstep round-robin pass (only `dog-running3.mp3`
  loop is wired today); also landing-thump variants.
- `Dog/dog-bark`, `dog-growl`, `dog-snarl` — vault effort, chase intro barks.
- `UI/ui-impact2.wav`, `UI/ui-gameover.wav` — alternates for impact/game-over.
- Malbers `Whistle.wav`, `Whistle Quick.wav` — alternates for the stop whistle.

## Already wired before this pass (unchanged)

Jump `Dog/dog-jump.wav` · footstep loop `Dog/dog-running3.mp3` · coins
`Reward/reward-collect.wav` · mega coin + love note `Reward/reward-squeaker2.wav` ·
life `Reward/reward-bell.wav` · level complete: 7 random `Meme/*` clips ·
location music/ambient via `LocationConfig`.
