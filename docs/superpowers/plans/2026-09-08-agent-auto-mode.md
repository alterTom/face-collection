# Agent auto capture implementation plan

> Execute with superpowers:subagent-driven-development; review changes and run integration checks before delivery.

**Goal:** Move stable-face auto capture from Vue into the local Agent.
**Architecture:** Session-owned detection loop using OpenCV YuNet and a monotonic stability tracker. Existing camera read gate serializes preview and detection. Cancellation is awaited before mode changes, rearm, close and disposal. Events carry a roundId; clients discard obsolete rounds.
**Tech stack:** .NET 10, existing OpenCvSharp5.Windows, bundled YuNet ONNX, JS SDK, Vue 3.
**Spec:** AGENTS.md at commit 837bae6, section “已记录、下次实施：由前端传参，Agent 自动抓拍”, approved by user on 2026-09-08.

## Global constraints
- Default manual; stableDurationMs integer 500–10000, default 1500; session only.
- Single stable face for configured duration, reset on missing/multiple faces, movement, gaps over 750ms. Compare against start-of-window anchor.
- No photo persistence or Base64 logging. Model and license ship offline with installer.
- Preserve existing manual SDK/protocol. Auto capture works without browser preview.

## Task 1: Agent detection and protocol
- [x] Write and run failing tests for stability, invalid mode/duration, single capture/rearm, cancellation and errors.
- [x] Add AutoCapture/FaceStabilityTracker.cs and YuNetFaceDetector.cs with injectable IFaceDetector; bundle model/license with output and publish.
- [x] Extend CaptureSession with camera.setCaptureMode and capture.rearm. Open/set/rearm responses include captureMode, stableDurationMs, roundId (unique string per round). New event envelopes: {type:'auto.status'|'auto.capture'|'auto.error',event:true,data:{roundId,...}}; status is no-face/multiple-faces/stabilizing/complete; capture includes existing photo fields; error includes code,message,retryable,cameraClosed.
- [x] Strictly validate configuration before mutation. Await previous loop cancellation; control commands validate then cancel before waiting for the command gate; the loop holds that gate for state consistency. Detection failure leaves manual available; camera failure closes camera and releases lease.
- [x] Run .NET tests, including real model on synthetic blank JPEG.

## Task 2: SDK and Vue migration
- [x] Add failing SDK tests for options, subscriptions, round fencing and obsolete socket events.
- [x] SDK open forwards captureMode/stableDurationMs; setCaptureMode(options), rearm(), on(eventType,callback) returning unsubscribe. Events cannot resolve commands; bind active round on successful responses before resolving promises, invalidate on requested mode/rearm/close/disconnect. Ignore stale socket events.
- [x] Replace Vue detector with SDK subscriptions; keep current UI selection/status/retake, show safe errors, remove MediaPipe code/assets/dependency and prepare scripts. Update tests and fixture server to new protocol. Preserve old manual flows.
- [x] Run SDK tests and Vue tests/build.

## Task 3: review and distribution
- [x] Review cross-layer race handling and compatibility; fix findings with regression tests.
- [x] Update AGENTS.md and README with usable API and model provenance, remaining hardware validation.
- [x] Build installer, verify published model/native runtime; browser desktop/mobile smoke tests when tools available. Real hardware validation requires available camera and subject; report actual coverage only.

## Execution decisions
- Use requested existing agent-auto-mode branch, clean at start; no branch switch or merge required.
- User already approved recorded architecture and asked implementation; no repeat approval gate.
- Keep implementation uncommitted for user review unless subsequently requested.

## Verification outcome (2026-09-09)
- Agent Release tests: 84 passed, including real YuNet model and real WebSocket event order.
- SDK: 11 passed. Vue: 14 passed. Vite production build passed.
- Review findings fixed with red/green regression tests: detector cancellation inversion, close exceptions, preview failure after completion, blocked error send during disposal, camera closure after detector failure/manual mode. Scoped re-review: no remaining important findings.
- Camera failure notifications use a separate session-lifetime token and a 2-second send timeout so preview and auto cancellation cannot suppress each other's notification.
- Installer 1.1.0 built successfully (93,485,070 bytes). Published ONNX SHA-256 matches source; compiler included Models, license and runtime in the self-contained executable package.
- Published EXE live smoke on isolated 127.0.0.1:17656: camera indices 0 and 1 each produced exactly one auto photo, then manual photo at 1280x720; no detection errors. Only metadata/status emitted; no photos persisted. Test process stopped; pre-existing Agent retained.
- Browser UI at 127.0.0.1:5173 with synthetic fixture on 17655: manual capture, switch auto, rearm, download click, close, disconnect passed; no app console errors. Desktop 1265x713 and mobile 390x844 inspected, no horizontal overflow. Screenshots outside repository.
- Remaining acceptance: target-computer performance, lighting/multi-person field tests and installer upgrade/uninstall on an isolated machine. Existing installed program was not modified.
- User authorized committing and pushing the completed migration to origin/agent-auto-mode on 2026-09-09.