import { describe, it, expect, beforeEach, vi, afterEach } from 'vitest';
import {
  request,
  ApiClientError,
  setStoredToken,
  API_BASE_URL,
} from '../services/api';

describe('API Client & Request Layer', () => {
  beforeEach(() => {
    sessionStorage.clear();
    vi.restoreAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('includes Content-Type and Accept headers by default', async () => {
    const mockResponse = { data: 'ok' };
    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify(mockResponse), {
        status: 200,
        headers: { 'Content-Type': 'application/json' },
      })
    );

    const result = await request('/test-endpoint', { requiresAuth: false });

    expect(result).toEqual(mockResponse);
    expect(fetchSpy).toHaveBeenCalledTimes(1);

    const [calledUrl, calledOptions] = fetchSpy.mock.calls[0];
    expect(calledUrl).toBe(`${API_BASE_URL}/test-endpoint`);

    const headers = calledOptions?.headers as Headers;
    expect(headers.get('Content-Type')).toBe('application/json');
    expect(headers.get('Accept')).toBe('application/json');
  });

  it('attaches Bearer token when token is stored and requiresAuth is true', async () => {
    setStoredToken('test-jwt-token-123');

    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ status: 'success' }), { status: 200 })
    );

    await request('/api/secure-data');

    expect(fetchSpy).toHaveBeenCalledTimes(1);
    const [, calledOptions] = fetchSpy.mock.calls[0];
    const headers = calledOptions?.headers as Headers;
    expect(headers.get('Authorization')).toBe('Bearer test-jwt-token-123');
  });

  it('does not attach Authorization header when requiresAuth is false', async () => {
    setStoredToken('test-jwt-token-123');

    const fetchSpy = vi.spyOn(global, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ status: 'success' }), { status: 200 })
    );

    await request('/api/public-data', { requiresAuth: false });

    const [, calledOptions] = fetchSpy.mock.calls[0];
    const headers = calledOptions?.headers as Headers;
    expect(headers.get('Authorization')).toBeNull();
  });

  it('handles HTTP 401 Unauthorized with proper ApiClientError flags', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ message: 'Invalid token.' }), {
        status: 401,
        statusText: 'Unauthorized',
      })
    );

    try {
      await request('/api/profile');
      expect.fail('Should have thrown ApiClientError');
    } catch (err: unknown) {
      const apiErr = err as ApiClientError;
      expect(apiErr.status).toBe(401);
      expect(apiErr.isUnauthorized).toBe(true);
      expect(apiErr.isForbidden).toBe(false);
    }
  });

  it('handles HTTP 403 Forbidden with proper ApiClientError flags', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ message: 'Forbidden resource.' }), {
        status: 403,
        statusText: 'Forbidden',
      })
    );

    try {
      await request('/api/admin/metrics');
      expect.fail('Should have thrown ApiClientError');
    } catch (err: unknown) {
      const apiErr = err as ApiClientError;
      expect(apiErr.status).toBe(403);
      expect(apiErr.isForbidden).toBe(true);
      expect(apiErr.isUnauthorized).toBe(false);
    }
  });

  it('handles HTTP 429 Too Many Requests with rate limit messaging', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ detail: 'Rate limit exceeded. Try again in 60s.' }), {
        status: 429,
        statusText: 'Too Many Requests',
      })
    );

    try {
      await request('/api/AI/classify-ticket');
      expect.fail('Should have thrown ApiClientError');
    } catch (err: unknown) {
      const apiErr = err as ApiClientError;
      expect(apiErr.status).toBe(429);
      expect(apiErr.isRateLimited).toBe(true);
      expect(apiErr.message).toContain('Rate limit exceeded');
    }
  });

  it('handles HTTP 500 Internal Server Error cleanly', async () => {
    vi.spyOn(global, 'fetch').mockResolvedValueOnce(
      new Response(JSON.stringify({ title: 'Internal Error', detail: 'Database error' }), {
        status: 500,
        statusText: 'Internal Server Error',
      })
    );

    try {
      await request('/api/tickets');
      expect.fail('Should have thrown ApiClientError');
    } catch (err: unknown) {
      const apiErr = err as ApiClientError;
      expect(apiErr.status).toBe(500);
      expect(apiErr.isServerError).toBe(true);
    }
  });

  it('handles network disconnection transport failures', async () => {
    vi.spyOn(global, 'fetch').mockRejectedValueOnce(new Error('Failed to fetch'));

    try {
      await request('/api/tickets');
      expect.fail('Should have thrown ApiClientError');
    } catch (err: unknown) {
      const apiErr = err as ApiClientError;
      expect(apiErr.status).toBe(0);
      expect(apiErr.message).toContain('Unable to connect to the ResolveIQ API server');
    }
  });
});
