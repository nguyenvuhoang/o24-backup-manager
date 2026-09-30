import test from 'node:test';
import assert from 'node:assert/strict';
import { LatestRequest } from './latest-request.ts';

test('switching connection prevents a late test success from overwriting the new error', async () => {
  const requests = new LatestRequest();
  const old = requests.start();
  let finish;
  let notice = '';
  const pending = new Promise(resolve => { finish = resolve; }).then(() => { if (!old.signal.aborted) notice = 'A success'; });
  requests.cancel(); // selection switches to B
  notice = 'B connection failed';
  finish(); await pending;
  assert.equal(notice, 'B connection failed');
});

test('a newer test and unmount both cancel pending work', () => {
  const requests = new LatestRequest(); const a = requests.start(); const b = requests.start();
  assert.equal(a.signal.aborted, true); assert.equal(b.signal.aborted, false);
  requests.cancel(); assert.equal(b.signal.aborted, true);
});
