import test from 'node:test';
import assert from 'node:assert/strict';
import { createStabilityTracker } from '../src/face-stability.js';

const face = { x: 0.3, y: 0.2, width: 0.3, height: 0.4 };
function feed(tracker, start, end, value = [face]) {
  let result;
  for (let time = start; time <= end; time += 200) result = tracker.update(value, time);
  return result;
}
test('one face must remain stable for 1.5 seconds and triggers once until reset', () => {
  const tracker = createStabilityTracker();
  assert.equal(feed(tracker, 0, 1400).ready, false);
  assert.equal(tracker.update([face], 1600).ready, true);
  assert.equal(tracker.update([face], 1800).ready, false);
  tracker.reset();
  assert.equal(feed(tracker, 2000, 3600).ready, true);
});
test('missing faces, multiple faces and frame gaps reset stability', () => {
  for (const interruption of [[], [face, face]]) {
    const tracker = createStabilityTracker();
    feed(tracker, 0, 1400);
    tracker.update(interruption, 1500);
    assert.equal(tracker.update([face], 1600).ready, false);
  }
  const tracker = createStabilityTracker();
  feed(tracker, 0, 1400);
  assert.equal(tracker.update([face], 2600).ready, false);
});
test('movement, size changes and slow drift restart the stable window', () => {
  for (const changed of [{ ...face, x: 0.4 }, { ...face, width: 0.4 }]) {
    const tracker = createStabilityTracker();
    feed(tracker, 0, 1400);
    assert.equal(tracker.update([changed], 1600).ready, false);
  }
  const tracker = createStabilityTracker();
  for (let i = 0; i < 20; i++) {
    assert.equal(tracker.update([{ ...face, x: face.x + i * 0.008 }], i * 200).ready, false);
  }
});
