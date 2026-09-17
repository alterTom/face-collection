# YuNet face detection model

Source: https://github.com/opencv/opencv_zoo/tree/main/models/face_detection_yunet
File: face_detection_yunet_2023mar.onnx (232589 bytes), downloaded 2026-09-08 from official OpenCV Zoo.
SHA-256: 8f2383e4dd3cfbb4553ea8718107fc0423210dc964f9f4280604804ed2552fa4
License: MIT; see LICENSE in this directory.

Bundled with build and publish outputs. No runtime network download. Detection uses existing OpenCvSharp5.Windows CPU runtime, confidence threshold 0.85, image resized with preserved aspect ratio and black padding to 320×320. Stability is box position/size stability, not liveness or identity verification.

## Face Mesh eyelid landmarks (1.1.1)

- Source: https://github.com/yakhyo/mediapipe-face-mesh-onnx
- Release asset: https://github.com/yakhyo/mediapipe-face-mesh-onnx/releases/download/weights/face_mesh_Nx3x192x192.onnx
- Downloaded 2026-09-16. SHA-256: `3ca77cf59c18e4da0eccb46695bf604683fa564253e3385892981a5c274fb10f`.
- Apache-2.0; see `FACE-MESH-LICENSE`. Copyright 2026 Yakhyokhuja Valikhujaev. Architecture/weights originate from Google MediaPipe; upstream recovered Face Mesh weights via PINTO0309's Apache-2.0 conversion.
- ROI follows upstream `models/onnx_model.py`: center of detector box, square side 1.5×longest box side, roll aligned using eye centers, one affine warp to 192×192 with black border. RGB float32/255, NCHW. Runs in the existing OpenCvSharp CPU runtime; no browser model or runtime download.
- `landmarks`: 1×468×3 in crop pixels; `score`: raw face-presence logit, must be ≥log(9) (90% probability). Eye points must be finite, inside the crop and original image, and eye width ≥4 crop pixels.
- Eye aspect ratio (EAR) = two upper/lower eyelid distances averaged / eye-corner width. Eye indices: [33,160,158,133,153,144] and [362,385,387,263,373,380]. Both EAR≥0.16 means open; both≤0.13 means closed; otherwise transition. Version 1.1.2 corrects the former 0.10 closure threshold: a local user recording showed clear closure at approximately 0.12. These are application thresholds, not a calibrated accuracy claim.
- Replaces OCEC because it strongly misclassified a user-supplied narrow/downward-looking open-eye frame. This frame passed the actual YuNet→FaceMesh→Open pipeline locally; it is not stored in this repository. Blank-image CPU inference and synthetic geometry/state regressions are verified.
- From 1.1.3, measurements also reach the per-round blink tracker. For otherwise transitional frames, closure can be recognized when both eyes fall at least 30% below their recent open baseline and remain at or below 0.16. Each baseline uses the 75th percentile of up to 40 confident open samples in the last two seconds, requires at least three samples spanning 100ms, and resets with face continuity loss. Fixed confident Open retains priority. Completed blinks survive subsequent eyelid transitions, while the final capture still requires confident Open and stable face geometry.
- Live blink sequences, closed-eye false accepts, glasses, lighting, motion, distance and target CPU performance still require real-device validation. Action verification is not anti-replay liveness or identity verification.

## Mouth and head-turn measurements (2026-09-17)

The same Face Mesh inference now supplies independent action measurements selected by `verificationAction`:

- Mouth ratio: distance between inner-lip points 13/14 divided by mouth-corner distance 61/291. Closed threshold ≤0.12, open threshold ≥0.30. Mouth width must be at least 4 crop pixels.
- Turn ratio: nose point 1 offset from the midpoint of eye-corner points 33/263, projected onto the left-to-right eye axis and divided by eye separation. This is a normalized geometric indicator, not a yaw angle. Neutral is absolute ratio ≤0.10; wearer-left is ≥0.22 and wearer-right is ≤−0.22 on unmirrored camera frames.
- Relevant landmarks must be finite and within the crop and source image. Turn verification does not depend on valid mouth/eyelid measurements; mouth verification does not require a blink. Presence confidence and face continuity checks apply to all actions.
- Initial neutral pose, requested action, and returned neutral pose each require 200ms before stability timing starts. These thresholds require live camera calibration; synthetic tests verify state transitions and geometry invariance, not real-world accuracy.

See [delivery and validation record](../../docs/action-verification-validation.md) for scope and remaining device checks.
