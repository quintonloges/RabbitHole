import { test, expect } from './auth-fixture';
import { publicationDate, readableDate } from '../../apps/web/lib/api';
import { datedTitle } from '../../apps/web/lib/publication';

test('date-only publications and editions preserve the source calendar date', () => {
  const expected = new Date('2001-02-03T00:00:00Z').toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric', timeZone: 'UTC' });
  expect(publicationDate('2001-02-03T00:00:00Z')).toBe(expected);
  expect(readableDate('2001-02-03')).toBe(expected);
  expect(publicationDate(null)).toBe('Date unknown');
});

test('older titles show their publication year without inventing or duplicating it', () => {
  const now = Date.parse('2026-10-31T00:00:00Z');
  expect(datedTitle('How filters work', '2001-02-03T00:00:00Z', now)).toBe('How filters work (2001)');
  expect(datedTitle('A recent story', '2026-10-30T00:00:00Z', now)).toBe('A recent story');
  expect(datedTitle('A future story', '2027-01-01T00:00:00Z', now)).toBe('A future story');
  expect(datedTitle('Unknown date', null, now)).toBe('Unknown date');
  expect(datedTitle('Invalid date', 'invalid', now)).toBe('Invalid date');
  expect(datedTitle('An essay (2001)', '2001-02-03', now)).toBe('An essay (2001)');
  expect(datedTitle('An essay [2001]', '2001-02-03', now)).toBe('An essay [2001]');
  expect(datedTitle('A study of 1850', '2001-02-03', now)).toBe('A study of 1850 (2001)');
  expect(datedTitle('Earlier this year', '2026-01-01T00:00:00Z', now)).toBe('Earlier this year (2026)');
});
