import test from 'node:test';
import assert from 'node:assert/strict';
import { DEFAULT_DATABASE_PORTS, normalizeConnectionPort, changeConnectionProvider } from './database-providers.ts';

test('empty SQL Server ports normalize to 1433, including legacy edits', () => {
  for (const port of [null, undefined, '']) {
    const form = { provider: 'sqlserver', port, host: '192.168.1.138', database: 'master', instanceName: 'REPORTS' };
    const normalized = normalizeConnectionPort(form);
    assert.equal(normalized.port, 1433);
    assert.equal(normalized.instanceName, 'REPORTS');
    assert.equal(normalized.database, 'master');
    assert.equal(form.port, port);
  }
});
test('custom port remains 1500 when editing and changing provider', () => {
  const form = { provider: 'sqlserver', port: 1500, host: 'db' };
  assert.equal(normalizeConnectionPort(form).port, 1500);
  assert.equal(changeConnectionProvider(form, 'postgresql').port, 1500);
  assert.equal(changeConnectionProvider(form, 'postgresql').host, 'db');
});
test('provider change updates only an uncustomized port', () => {
  const postgres = changeConnectionProvider({ provider: 'sqlserver', port: 1433 }, 'postgresql');
  assert.equal(postgres.port, 5432);
  assert.equal(changeConnectionProvider(postgres, 'oracle').port, 1521);
  assert.deepEqual(DEFAULT_DATABASE_PORTS, { sqlserver: 1433, postgresql: 5432, oracle: 1521 });
});
test('explicit discovery keeps null, but explicit TCP port takes priority', () => {
  assert.equal(normalizeConnectionPort({ provider: 'sqlserver', port: null, useNamedInstanceDiscovery: true }).port, null);
  assert.equal(normalizeConnectionPort({ provider: 'sqlserver', port: 1500, useNamedInstanceDiscovery: true }).port, 1500);
});
test('invalid explicit port is not silently replaced with a default', () => {
  assert.equal(normalizeConnectionPort({ provider: 'sqlserver', port: 0 }).port, 0);
});
