import test from 'node:test';
import assert from 'node:assert/strict';
import { selectableNames, formatDatabaseSize, filterDatabases } from './database-selection.ts';
const databases = [
  { name: 'Sales', status: 'ONLINE', isAccessible: true },
  { name: 'SalesArchive', status: 'OFFLINE', isAccessible: true },
  { name: 'Finance', status: 'ONLINE', isAccessible: false },
  { name: 'Warehouse', status: 'RESTORING', isAccessible: true },
];
test('select all excludes offline/restoring and inaccessible databases', () => {
  assert.deepEqual(selectableNames(databases), ['Sales']);
});
test('search filters case insensitively without changing selection data', () => {
  assert.equal(filterDatabases(databases, ' SALES ').length, 2);
  assert.equal(databases.length, 4);
});
test('sizes distinguish unavailable metadata from zero and use units', () => {
  assert.equal(formatDatabaseSize(null), '—');
  assert.equal(formatDatabaseSize(0), '0 KB');
  assert.equal(formatDatabaseSize(0.5), '512 KB');
  assert.equal(formatDatabaseSize(1024), '1 GB');
  assert.equal(formatDatabaseSize(1048576), '1 TB');
});
