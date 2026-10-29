import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it } from 'vitest';

import { Button } from './button';
import { NotFoundPage } from '@/pages/not-found-page';

/**
 * A white screen instead of "page not found".
 *
 * Radix's Slot takes exactly one child and replaces it with its own props. The Button rendered a
 * spinner expression and then its child, so with `asChild` the Slot was handed two things -- the
 * `false` of an unused `isLoading &&` counts -- and threw during render. There is no error
 * boundary above the routes, so React unmounted the whole tree: the console went white for any
 * address it does not know, and the page that broke it was its own 404 page.
 *
 * Rehearsing the demo found it, by opening /login, which is not a route (/auth/login is).
 */
describe('a Button rendered as something else', () => {
  it('renders its child instead of a button', () => {
    render(
      <Button asChild>
        <a href="/dashboard">Back to the dashboard</a>
      </Button>,
    );

    const link = screen.getByRole('link', { name: 'Back to the dashboard' });
    expect(link.tagName).toBe('A');
    expect(screen.queryByRole('button')).toBeNull();
  });

  it('does not crash the page that uses it', () => {
    render(
      <MemoryRouter>
        <NotFoundPage />
      </MemoryRouter>,
    );

    expect(screen.getByRole('link')).toBeTruthy();
  });
});
