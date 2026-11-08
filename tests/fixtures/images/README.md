# Splash regression images

These unmodified central-window captures were recorded from the running game on
2026-11-08 around 21:45:35–21:46:00 (Asia/Taipei), using the promoted diagnostic
tool in `tools/BobberDiagnostic.cs`. The user cast manually and retrieved the fish
after the splash. The source sequence has 48 frames; these eight consecutive
frames minimize the failing image-to-bite scenario:

- 37–42: normal visible float motion, used to establish the track and calibration.
- 43: visible splash. The original detector rejected the matching feather group
  because its fill ratio was approximately 0.25, below 0.35.
- 44: recovery frame, also originally rejected (fill ratio approximately 0.34).

The test feeds the original pixels through the production tracker, detector and
watcher. It uses a deterministic 500 ms interval, approximately the diagnostic
sampling rate; the image fixtures do not contain exact capture timestamps. It
asserts no pre-splash strike, continued tracking during the splash, and a strike
on the splash sequence. The two CSV fixtures in the parent folder are separate
older no-bite logs, not observations generated from these images.
