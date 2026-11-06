# Repository guidance

This repository contains the replay frontend, not a replacement for Overcooked-AI's rules.
Keep upstream as a pinned external dependency. Never simulate game rules inside Unity.
Preserve T+1 snapshots for T joint actions and keep sparse score separate from shaped reward.
The optional event-info patch only forwards data already produced by the upstream MDP.
Do not add licensed Store source art, source VRM/GLB files, environments or Unity caches to Git.
Preserve third-party attribution in compiled releases. See THIRD_PARTY_NOTICES.md and LICENSES/.
