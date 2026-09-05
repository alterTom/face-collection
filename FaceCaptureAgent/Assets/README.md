# FaceCaptureAgent 图标

用户选定方案 A「人脸取景」：蓝色四角取景框与人像剪影。

- `face-capture.png`：通过内置 image_gen 工具生成的透明 PNG 源图。
- `face-capture.ico`：32 位透明图标，包含 16、20、24、32、48、64、128、256 像素。
- 修改 PNG 后，在仓库根目录执行 `./scripts/convert-icon.ps1` 重新转换格式。
- 同一 ICO 用于 EXE 图标、嵌入的托盘/日志窗口资源和 Inno Setup 安装包图标。

## 生成记录

使用内置 image_gen（非 CLI），以用户已选择的方案 A 预览图作为参考。

首次生成提示词：

> Create the FINAL standalone production icon from ONLY concept A (upper-left blue person inside four rounded scanning corner brackets) of this reference board. The board is a design reference. Output ONE square transparent PNG icon, no board. Preserve concept A silhouette: four separate thick rounded L-shaped blue viewfinder corners surrounding a simple filled blue round head and shoulder bust. Make the head and shoulders a cohesive simple silhouette with no facial detail. Flat single solid vivid blue #0878FF, fully opaque blue interior, antialiasing only on edges. Genuine transparent alpha background including all negative space. Remove ALL glow, shadows, gradients, background colors, cards, lettering, labels and taskbars from the reference. No text. No lighting or dimensional effects. Center symmetrical geometry with generous gaps between head/bust and four corners; bold strokes designed for legibility down to 16px. Icon fills approximately 88 percent of square canvas with equal transparent margins on every side. Crisp clean production asset.

透明背景修正提示词：

> Background extraction edit. KEEP the blue four scan corner brackets and blue person silhouette exactly as-is. REMOVE the entire baked-in white/gray checkerboard background. Output actual RGBA PNG with alpha=0 on all background pixels and all negative space. A checkerboard drawn as pixels is NOT transparency. Do not draw checkerboard, white, gray or any other replacement background. Genuinely transparent cutout PNG required. Preserve blue icon, centered square composition, no shadows/glow/text.
