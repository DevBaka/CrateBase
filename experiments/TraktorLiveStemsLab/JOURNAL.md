# Investigation journal

## 2026-10-27 00:36–00:45 UTC — setup and baseline

- Explicit eight-hour work request recorded; installed Traktor is not running.
- Copied only the executable into the private lab and verified the original and copy hashes match.
- Existing Mixxx ONNX backend uses disabled graph optimizations, sequential DirectML, shared sessions and an asynchronous source/cache wrapper. Its comments record unresolved DirectML initialization/numerical issues; GPU success must be measured independently.
- Native S4 MK3 not currently enumerated; an F1 (VID_17CC/PID_1120) is present. No hardware commands sent.
- Created a private Python 3.11 environment with pefile, capstone, Frida, psutil, NumPy and soundfile. No global PATH or application settings changed.
- Located Visual Studio 18 Community / MSVC 14.51.36231 for possible native instrumentation builds.

## 00:43–00:54 UTC — static anchors, GPU benchmark, first sandbox launch

- Recovered selected MSVC RTTI and audio vtables, including MediaAudioStream, WAVAudioStream, StemMixdownStream and TrackPlayer. Read callback appears at virtual slot 17; arguments/format still require runtime validation.
- Ghidra 12.2 and Temurin JDK 25 installed privately with published SHA-256 verification. Initial OSGi initialization failed under the default settings directory; a private `application.settingsdir` fixed it. Full analysis is running with a 20 GB heap and eight analysis workers.
- Built a separate CUDA PyTorch 2.5.1 environment. On a real HIGHTKK track, native HTDemucs six-second inference measured ~0.143 s FP32 / ~0.113 s AMP FP16 after warm-up, roughly 558–627 MB peak allocated GPU memory. These exclude decoding/cache/integration. Ten-second direct model input fails because of the model's context constraints; production streaming must split it into supported windows.
- First lab launch successfully kept collection/settings in the isolated directory and showed the original Traktor UI. No S4 audio hardware is connected; its saved ASIO configuration cannot initialize.
- The copied application automatically registered its URI handler under HKCU/Software/Classes/traktor, which was outside the initial redirection scope. Stopped the lab process, restored the exact original command from Traktor's logged previous value, and added URI registry redirection before further launches. Original collection/settings hashes remained unchanged.
- Native Resources64 contains iZMLSSTDSP.dll and onnxruntime.dll. This exposes an additional investigation path: native separation-provider instrumentation, distinct from incremental live deck integration.

## 01:02–01:24 UTC — native source bridge and incremental GPU proof

- Loaded the synthetic five-stream Stem file into the real native Deck A, then Deck B. Native four-stem controls appeared and playback advanced with master volume zero. Keyboard loading worked reliably; rapid synthetic drag gestures did not.
- Confirmed MediaAudioStream virtual slot 17 reads 1,024 planar float samples per stereo channel. Native factory RVA 0x667d30 receives a wide filesystem path and a stem selector (1–4); source position is at object offset 0xe0. Corrected an initial UTF-8/wide-string interpretation using native factory tests.
- Ghidra full analysis continues separately. A second no-analysis project exported a bounded selection of audio functions quickly, enough to verify the source read/seek contract without waiting for full analysis.
- Visual Studio has compiler binaries but lacks C/C++ headers and Windows SDK. Installed Zig privately and built a small C DLL instead. No system installation or PATH changes.
- Implemented an immutable per-block mmap cache, incremental CUDA worker with overlapping context windows, and a per-object native vtable bridge. JavaScript only registers source objects; there are no JavaScript callbacks or GPU calls on the audio read path.
- Real HardTekk test: 90 seconds separated in 7.13 seconds (first CUDA window 3.81 s including warm-up; later windows ~0.12–0.13 s). All 3,876 cache blocks became ready, finite outputs, peak allocated GPU memory ~612 MB. Reconstruction SNR of summed stems versus original ~21.58 dB; this is an objective diagnostic, not a claim of perfect stem quality.
- Native mock-process test passes for all four stereo planes, seek positions, unready-cache fallback, vtable RTTI preservation, and counters.
- Built a valid five-stream silent AAC carrier with shared payload and a Stem manifest: 90 seconds occupies ~26 KB. It is explicitly a carrier, never a finished stem export. FFmpeg decodes every advertised stream.
- Inside the sandbox-owned Traktor process, created native audio readers for this carrier and registered the GPU cache. Twenty native reads across all four stems and five positions returned 320 sampled floats exactly matching CUDA cache output. First version retained native decoding; v3 avoids the redundant decode while preserving native position/seek state. These probes do not yet prove mixer, deck controls, ASIO output, or hardware behavior.
- A Windows firewall permission dialog for private Ghidra/OpenJDK blocks computer-use deck tests. It remains unchanged; requested the user close it with Cancel when available. Native audio API probes and non-UI development continue independently.
- The previous bounded lab session ended and its final guard verified original collection/settings hashes unchanged. New private session PID 8028 started at 01:21 UTC. Global traktor URI handler still points to the original installed executable.

## Later investigation through 05:48 UTC

- Full Ghidra analysis completed; bounded exports recovered source factories, cache filling and final cache reads. Reports and decompiled output remain ignored/private.
- Created authenticated local named-pipe GPU service with a resident HTDemucs model, four bounded track jobs, configurable FP16/FP32 and GPU budget. No TCP listener or Windows service installation.
- Native/FFmpeg decoder comparison measured a 64-sample AAC offset and a zero-sample MP3 offset; alignment is verified before publishing generated audio. Added 44.1/48/96 kHz sampling and native reader lifecycle tests.
- User closed the original installed Traktor and authorized uninterrupted tests in the isolated copy. User extended the remaining work window to 08:48:21 UTC. S4 MK3 is absent.
- Diagnosed filesystem-browser views being mistaken for imported collection contents. The private two-track collection is loaded correctly. Fixed forward-slash path normalization in sandbox redirection. Optional NT file tracing was investigative only; native NT path mutation is disabled by default.

## 05:48–06:28 UTC — automatic native deck playback and control panel

- Corrected factory argument substitution from an invalid Frida UInt64 argument to a NativePointer. Real native Stem Deck A then loaded all four source readers automatically and requested GPU generation.
- Found ~27 s of original audio prefetched before GPU publication. Source-only replacement cannot remove that delay. Recovered final CachedAudioStream fetch RVA 0x67f850 and source-attachment RVA 0x6871b0; implemented a C-only downstream replacement preserving native cache position/locking/EOS handling.
- Mock test with deliberately stale native ring data now verifies immediate generated-sample replacement, seeks, reverse reads and bypass. Native bridge v4 also prioritizes unready playback positions and crossfades between explicit original/generated samples rather than stale native output.
- Actual Traktor PID 9900 ran two real native stem decks. Downstream counters confirm four independent generated planes entering playback, including the second track whose original prefetch was already filled. Four distinct native stem waveforms appear as generated data arrives.
- Real automatic load: 147.727 s AAC completed in 6.592 s (1.297 s decode, 5.292 s generation). 375.066 s MP3 completed in 14.945 s (1.567 s decode, 13.376 s generation). Peak allocated GPU memory ~611.4 MB; model was resident. Stem peaks can exceed 1.0; this is not a claim of artifact-free stems.
- Native stem mute buttons were exercised. Removed copied filter/effect/EQ settings from the comparison path in the lab only. Local Voicemeeter B1 recordings show a bass-only result correlating at 0.867 with generated bass, versus 0.585 with the stem sum. Earlier filtered/quiet recordings were inconclusive and are not treated as clean output proofs.
- Added a German Tk control panel, repository launch command, GPU setting persistence and enable-switch delivery to the native bridge on the control path. Added user-selected private audio import with cue/metadata preservation, classic Stem carriers and duplicate prevention. Preparation test passes without source/NML mutation.
- Closed owned PID 9900 for v4 integration tests. Launcher guard again confirmed original collection and settings hashes unchanged. User reminded us that Traktor needs one to two minutes to reach fullscreen; account for this during each launch.

