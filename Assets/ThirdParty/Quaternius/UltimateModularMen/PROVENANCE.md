# Quaternius Ultimate Modular Men: athlete presentation source

Author: Quaternius. License: CC0 1.0 Universal / Public Domain Dedication.
The original author-supplied notice is preserved in `CC0-License.txt`.

Official pack page: https://quaternius.com/packs/ultimatemodularcharacters.html

Downloaded from that page's public Google Drive folder, without authentication,
on 2026-10-16. Only the selected character with its embedded animations and license
were acquired; no executables or external animation services are used.

- `Casual_2.fbx`: Individual Characters / FBX / Casual_2.fbx, including its
  original matching skeleton and embedded animations.
  https://drive.google.com/file/d/15q2F99fLycew7wQunZhfg0n_wChZfyCM/view
  SHA-256: `9B4C6520DE636575D4BA612E3DB0716306858B24871DA8C0CE039D914523E694`
- Original license file:
  https://drive.google.com/file/d/1TTvylHa1CsiJuHFWWiv6PFGhLM-aAH5z/view

The game uses the actual skinned character and six selected idle/walk/run animation
clips, with an in-project clothing palette, fixed 1.75 m presentation scale,
head look, and a non-stretching right-arm IK overlay. These are visual assets,
not measured badminton biomechanics. The animation never controls the physical
racket, shuttle velocity, player pose, opponent movement, or scoring.

Rig compatibility: the pack's separate Humanoid Rig variant renames/reparents
bones and cannot be paired with the original generic animation file merely by
loading both assets. The active builder deliberately uses geometry and embedded
clips from `Casual_2.fbx`, verifies every transform binding, and tests actual
lower-leg rotation plus skinned-mesh deformation, rather than only counting clips.
