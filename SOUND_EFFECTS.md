# Sound Effects — Running List

Audio gets its own pass later. This tracks every moment that needs sound, what
temporary clip (already in the repo) is wired today, and what we actually want.
Temp wiring is done by `Tools > RingSport > Setup Juice Polish`
([JuicePolishSetup.cs](Assets/Scripts/Editor/JuicePolishSetup.cs)) — it only fills
empty fields, so replacing a temp clip in the Inspector sticks.

## Real clips (audio pass — no longer stand-ins)

| Moment | Clip | Behaviour | Plays from |
|---|---|---|---|
| Near-miss whoosh | `near-miss woosh.wav` | Pitch/volume by clearance — a shave is higher and louder than a comfortable clear. Own AudioSource, so the coin pitch ladder is untouched | `Obstacle.OnPlayerCollision` → `LevelManager.PlayNearMissWhoosh` |
| Lane-change whoosh | `lane-change woosh.mp3` | Own source that restarts per swipe (a weave never stacks), panned and pitched by direction | `PlayerController.NotifyLaneChange` |
| Sprint start | `sprint start sound.wav` | One-shot on the sprint that actually begins (a held input can't retrigger it) | `PlayerController.TryStartSprint` |
| Sprint wind layer | `wind speed layer.wav` | Looping, fades in with the sprint; volume + pitch ride the world scroll speed | `PlayerController.HandleSprintAudio` |
| Sprint exhausted pant | `sprint exhausted pant.wav` | Looping, starts when stamina empties and stops the moment sprint is usable again (it *is* the lockout's voice) | `PlayerController.HandleSprintAudio` |
| Confetti pops | `confetti pops.flac` | Three pitched pops staggered ~70ms apart, layered *under* the existing moment — the finish-line sting stays | `LevelManager.PlayConfettiPops`, called from `FinishMomentRoutine` and `SecretNotePanel.Open` |
| Face-attack freeze riser | `face-attack freeze.wav` | Hits on the freeze and sustains under the limb QTE (~4.3s clip vs a ~2.3–2.9s window) | `MiniLevelFaceAttack.EnterReveal` |
| Steak splat | `steak splat.aiff` | Wet hit alongside the dust burst + camera shake | `MiniLevelFoodRefusal.OnSteakHit` |
| Simon Says correct | `simon says hurray.mp3` | Replaced `reward-coin` | `MiniLevelPositionsSimonSays` round pass |
| Simon Says wrong | `simons says buzzer.wav` | Replaced `meme-bruh` | `MiniLevelPositionsSimonSays.HandleIncorrectInput` |

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
| Flee/face catch scream layer | `Decoy bite/decoy-scream1.wav` / `decoy-scream2.wav` | Keep | `MiniLevelFleeAttack.DoCatch` / `MiniLevelFaceAttack.DoCatch` |
| Face-attack window tick | `Reward/reward-pop.wav` (accelerating, pitched 0.85→1.25) | Dry clock tick or heartbeat | `MiniLevelFaceAttack.UpdateWindow` |
| Palisade wall hit | `UI/ui-impact2.wav` | Wood thud + dog scrabble | `PalisadeMinigame.StartMinigame` |
| Palisade tap thock | `Dog/dog-footsteps5.wav` (pitch rises with progress) | Short woody thock | `PalisadeMinigame.OnTapFeedback` |
| Palisade timer tick | `Reward/reward-pop.wav` (pitch 0.9) | Urgent clock tick | `PalisadeMinigame.Update` (under 1.5s) |
| Palisade success | `Dog/dog-bark.wav` | Keep | `PalisadeMinigame.AnimateAndResume` |
| NEW HIGH SCORE reveal | `Meme/meme-taduh.wav` | Bright fanfare sting | `UIManager.RevealNewHighScore` |
| Secret-note reveal | `Meme/meme-taduh.wav` | Paper unfold + warm fanfare (this is the emotional payoff — worth a real clip) | `SecretNotePanel.Open` |
| Love-note pickup | `Reward/reward-squeaker1.wav` (no longer shares the mega coin's squeaker2) | Unique romantic chime | `LoveNoteCollectible.Collect` |
| Button click (all UI) | `Reward/reward-pop.wav` (±3% pitch jitter, low volume) | Soft dry UI tick | `JuicyButton.OnPointerDown` |
| Simon Says pose tones | `Reward/reward-pop.wav` (Down 0.8 / Sit 1.0 / Stand 1.2) | Three clean melodic tones | `MiniLevelPositionsSimonSays.PlayPoseTone` |

## Needed — no clip in the repo fits yet

| Moment | What we want | Where it will play |
|---|---|---|
| Death whimper layer | Dog yelp/whimper, 2 variants | `GameManager.DeathImpactSequence` |
| Finish line | Crowd cheer + party horn (the confetti pops and the random meme sting are in; the crowd isn't) | `LevelManager.FinishMomentRoutine` |
| Simon Says pose tones | Three clean melodic tones (still `reward-pop` on a pitch ladder) | `MiniLevelPositionsSimonSays.PlayPoseTone` |
| Steak near-miss whoosh | Whoosh per steak that just misses (the splat on a hit is in) | `MiniLevelFoodRefusal` |
| UI button click | Soft dry click (still `reward-pop`) | `JuicyButton.OnPointerDown` |

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
