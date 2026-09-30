import test from 'node:test';
import assert from 'node:assert/strict';
import { validateJob } from './job-validation.mjs';

test('requires job name and at least one database', () => {
  assert.deepEqual(validateJob({ name: '', databases: [], backupDirectory: 'D:\\Backup', retentionDays: 2, connectionId: 'id' }), {
    name: 'Tên job là bắt buộc.',
    databases: 'Chọn ít nhất một database.'
  });
});

test('accepts a named job with selected databases', () => {
  assert.deepEqual(validateJob({ name: 'EMI CMS', databases: ['o24cms'], backupDirectory: 'D:\\Backup', retentionDays: 2, connectionId: 'id' }), {});
});

test('requires path, positive integer retention and connection', () => {
  const errors = validateJob({ name: 'Job', databases: ['db'], backupDirectory: ' ', retentionDays: 0 });
  assert.ok(errors.backupDirectory);
  assert.ok(errors.retentionDays);
  assert.ok(errors.connectionId);
  assert.ok(validateJob({ retentionDays: 1.5 }).retentionDays);
});
