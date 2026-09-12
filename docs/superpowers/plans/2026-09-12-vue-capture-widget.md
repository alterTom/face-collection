# Vue 3 capture component implementation plan

**Goal:** Deliver an independently installable Vue 3 modal component in `face-capture-widget/`, keeping `vue3-demo`, the SDK and Agent unchanged.

**Approved design:** `<FaceCaptureDialog v-model="visible" @result="handleResult" />` opens a modal, connects automatically, previews and captures one stable face. Successful capture closes the modal and returns JPEG metadata, Base64 and Blob. Cancel returns `cancelled`. Failed connections retry within a single 60,000 ms deadline; expiry closes the modal with `connection_failed / AGENT_CONNECTION_TIMEOUT`. Connection success cancels that deadline. Retake resets detection or retries a recoverable device error. No automatic upload, logging of photos, identity verification or countdown during capture.

**Architecture:** Plain JS session state machine using the existing SDK; Vue SFC handles presentation, dialog accessibility and lifecycle. Vite library build bundles the SDK and externalizes Vue. Native dialog supplies focus containment; scoped CSS avoids host-page changes. Connection and operation generations invalidate late responses. Each session emits at most one terminal result.

**Constraints:** New component and documentation files only. Vue 3.5+, Node 22.12+. Local ws/wss endpoint only. Agent remains required; HTTPS deployment requires separate browser/local-service compatibility validation. No npm publication in this task.

## Tasks

- [x] Write `face-capture-widget/tests/session.test.mjs` covering deadline/retry, success, cancellation, stale completions, recoverable errors and retake. Observed missing-module failure before implementation.
- [x] Implement `src/session.js` with injected clock/client for deterministic lifecycle tests, and `src/client.js` using the existing SDK.
- [x] Implement `src/FaceCaptureDialog.vue`, `src/index.js` and `src/index.d.ts`; result event closes the UI before delivering the result. Include responsive circular preview, status, retry and exit controls.
- [x] Add package/build configuration, standalone example and README. Build ESM library with external Vue and bundled SDK; pack only dist and README.
- [x] Run session and real WebSocket integration tests, component lifecycle tests, build and isolated tarball consumption checks. Existing SDK and .NET tests passed. Browser checked desktop/mobile modal, full 60-second countdown, cancel, retake and auto capture; error recovery covered in DOM/WebSocket integration tests.
- [x] Review diff, package contents and actual verification results. Installation artifact is `face-capture-widget/face-capture-vue-0.1.0.tgz`; scope and hardware/browser limitations recorded in `face-capture-widget/VALIDATION.md`.
