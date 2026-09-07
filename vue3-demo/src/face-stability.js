// Compare against the start of the window, so slow cumulative drift also resets it.
export function createStabilityTracker({ stableMs = 1500, maxGapMs = 750 } = {}) {
  let anchor, since, last, fired = false;
  function reset() { anchor = null; since = last = undefined; fired = false; }
  function update(faces, now) {
    if (fired) return { ready: false, status: '拍照完成' };
    const box = faces.length === 1 ? faces[0] : null;
    if (!box || ![box.x, box.y, box.width, box.height].every(Number.isFinite)
      || box.width <= 0 || box.height <= 0) {
      reset();
      return { ready: false, status: faces.length > 1 ? '请保持画面中只有一人' : '请面向摄像头' };
    }
    const moved = !anchor || Math.abs(box.x - anchor.x) > anchor.width * 0.08
      || Math.abs(box.y - anchor.y) > anchor.height * 0.08
      || Math.abs(box.width - anchor.width) > anchor.width * 0.1
      || Math.abs(box.height - anchor.height) > anchor.height * 0.1;
    if (moved || last === undefined || now <= last || now - last > maxGapMs) {
      anchor = { ...box };
      since = now;
    }
    last = now;
    const ready = now - since >= stableMs;
    if (ready) fired = true;
    return { ready, status: ready ? '正在抓拍…' : '请保持稳定' };
  }
  return { update, reset };
}
