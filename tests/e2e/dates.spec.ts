import { test, expect } from '@playwright/test';
import { publicationDate, readableDate } from '../../apps/web/lib/api';

test('date-only publications and editions preserve the source calendar date', () => {
  const expected = new Date('2001-02-03T00:00:00Z').toLocaleDateString(undefined, { month: 'long', day: 'numeric', year: 'numeric', timeZone: 'UTC' });
  expect(publicationDate('2001-02-03T00:00:00Z')).toBe(expected);
  expect(readableDate('2001-02-03')).toBe(expected);
  expect(publicationDate(null)).toBe('Date unknown');
});
