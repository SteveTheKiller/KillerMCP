import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';

const source = process.argv[2] ?? new URL('../Features/Browser/browser-companion.html', import.meta.url);
const page = await readFile(source, 'utf8');
assert.match(page, /may send it to its AI provider/);
const elements = new Map();
const context = { drawImage() {} };
const element = selector => {
  if (!elements.has(selector)) {
    elements.set(selector, {
      addEventListener() {},
      getContext: () => context,
      toDataURL: () => 'data:image/jpeg;base64,AA==',
    });
  }
  return elements.get(selector);
};
let stopped = 0;
let failCameraUpload = false;
let recording;
class Recorder {
  constructor() { recording = this; }
  start(chunkInterval) {
    assert.equal(chunkInterval, 250);
    this.state = 'recording';
  }
  stop() {
    this.state = 'inactive';
    this.onstop();
  }
}
runInNewContext(page.match(/<script>([\s\S]*?)<\/script>/)[1], {
  URL,
  location: { href: 'http://127.0.0.1/?token=test' },
  document: { querySelector: element, createElement: () => element('canvas') },
  navigator: { mediaDevices: { getUserMedia: async () => ({ getTracks: () => [{ stop() { stopped++; } }] }) } },
  screen: {},
  innerWidth: 800,
  innerHeight: 600,
  devicePixelRatio: 1,
  window: { MediaRecorder: Recorder },
  MediaRecorder: Recorder,
  addEventListener() {},
  fetch: async (_, request) => ({ ok: !(failCameraUpload && JSON.parse(request.body).type === 'camera') }),
});
const settled = () => new Promise(resolve => setImmediate(resolve));
element('#enableCamera').onclick();
await settled();
assert.equal(element('#enableCamera').disabled, true);
Object.assign(element('#preview'), { videoWidth: 1920, videoHeight: 1080 });
failCameraUpload = true;
element('#capturePhoto').onclick();
await settled();
assert.match(element('#cameraStatus').textContent, /previous capture/);
element('#startRecording').onclick();
await settled();
recording.ondataavailable({ data: { size: 1_500_000 } });
await settled();
assert.equal(recording.state, 'inactive');
assert.match(element('#cameraStatus').textContent, /size limit/);
element('#disableCamera').onclick();
assert.equal(stopped, 1);
assert.equal(element('#enableCamera').disabled, false);
console.log('PASS capture disclosure, photo transfer errors, recording limit and camera shutdown.');
