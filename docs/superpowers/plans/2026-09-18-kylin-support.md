# Windows / Kylin implementation plan

> Execute with superpowers:subagent-driven-development. User approved shared core and separate platform hosts.

**Goal:** Preserve Windows x64 behavior and produce a Linux ARM64 host for Kylin Desktop V10 SP1 2403 (glibc 2.31, 4KB pages), plus Linux x64 build support.

**Architecture:** Shared net10.0 core owns camera/protocol/session/configuration/detection/logging and HTTP host mapping. Existing Windows project retains WinForms and identity. Linux project owns desktop integration and platform native dependencies. Existing SDK and business routing unchanged. All hosts remain loopback-only; photos and activity logs remain in memory.

- [x] Extract shared core and common host/test-page build resources; preserve models and Windows packaging. Baseline and regression .NET tests.
- [x] Test then implement platform reporting and V4L2 enumeration/backend selection. Keep numeric device IDs; Linux enumerate actual nodes and validate capture ability. Test invalid identifiers and platform behavior.
- [x] Add Linux host with GTK desktop integration, conditional native runtime packages and portable core tests; compile Linux ARM64 host and Windows host.
- [x] Add publish and user installation scripts, autostart and removal preserving config; document native dependencies and real-machine acceptance steps.
- [x] Review changes, run Windows .NET/SDK/frontend checks, inspect Windows publish assets, record limitations honestly.
- [ ] Build compatible Kylin native library, publish and validate both Linux architectures on target machines, including desktop and camera acceptance.

**2026-09-19 validation:** Windows Release tests 154 passed, shared core tests 140 passed, SDK tests 15 passed, Vue production build succeeded. Windows self-contained EXE served embedded test-page assets and returned win-x64 system information from outside the source directory; model/license hashes matched. Inno Setup produced the Windows 1.1.3 installer. Installation/upgrade and real-camera acceptance were not repeated. See `docs/windows-package-2026-09-19.md`.

**Linux blocker:** Official ARM64 OpenCV native package requires glibc 2.38, newer than target 2.31. ARM64 publishing requires a compatible custom native library and checks ELF architecture/GLIBC requirements. Native build recipe and ABI checker are included, but the recipe has not been executed on Kylin. Mocked installer tests on Git Bash passed with the real-symlink case skipped; they do not validate systemd or desktop behavior.

**Acceptance:** Existing Windows tests pass; both Linux targets compile/publish with models and embedded /test/; no Windows desktop dependency in Linux output. Kylin native loading, real camera, desktop tray, browser and install lifecycle must be tested on the actual machine before claiming supported deployment.

**Ruling:** Implement in existing feature branch agent-auto-mode; retain source model/assets paths for existing scripts. User explicitly authorized documentation updates, commit and GitHub push on 2026-09-19. Do not install into the user's existing application environment as part of this delivery.
