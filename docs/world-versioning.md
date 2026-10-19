# World versioning

World starts its own release sequence at **0.1.0**, based on Expanded **0.4.1**.
`VERSION` records the World release. `SetupConfig.WORLD_VERSION` supplies the
title/menu headers, window title, startup log and exported report headings.
Windows file and product versions identify the same World release; the file
version adds a fourth `.0` component.

`SetupConfig.GAME_VERSION` stays **0.4.1** as the inherited save/mod compatibility
version. Save envelopes, mod requirements and user data folders keep that value.
The assembly name and assembly version **0.4.1.0** remain stable because stored
type names can reference that identity. This prevents a cosmetic release bump
from hiding existing saves/settings or changing their compatibility checks.
World format remains **5**; no save migration is performed by this branding update.

`tools/release_version.rb` updates World release values and checks the separate
compatibility values. File/product versions are compiled from World constants.
The release workflow packages `RogueSurvivorWorld-vX.Y.Z-windows.zip` and uses
`world-vX.Y.Z` tags so World 0.1.0 does not collide with the inherited Expanded
`v0.1.0` tag. Inherited release history and attribution retain their original
version numbers.

## October 19, 2026 verification

The Windows Release build completed with no compiler warnings or errors. Native
unit tests and **554/554 gameplay scenarios** passed, including the genuine
pre-community format-5 fixture, version compatibility and large-save/load gates.
The actual main menu was rendered offscreen through WinForms/GDI+ and visually
checked; the window title also identifies World 0.1.0. This is automated native
verification, separate from human play. Linux/Docker and the Ruby release helper
were not executed locally.

Earlier generated scavenging failures and the October 18 large-save timing
overrun remain historical evidence; a passing run does not establish a fix to
those intermittent failures.
