import { describe, expect, it } from 'vitest';
import { ApiProblemError, parseApiProblemError } from './problemDetails';

describe('parseApiProblemError', () => {
  it('extracts custom TradeFlow error code, detail, and Retry-After header from RFC 7807 ProblemDetails', async () => {
    const response = new Response(
      JSON.stringify({
        type: 'https://tradeflow.com/errors/rate-limit-exceeded',
        title: 'Service Unavailable',
        status: 503,
        detail: 'Keycloak circuit breaker is open.',
        code: 'KEYCLOAK_UNAVAILABLE'
      }),
      {
        status: 503,
        headers: {
          'Content-Type': 'application/problem+json',
          'Retry-After': '15'
        }
      }
    );

    const parsed = await parseApiProblemError(response);

    expect(parsed).toBeInstanceOf(ApiProblemError);
    expect(parsed.status).toBe(503);
    expect(parsed.code).toBe('KEYCLOAK_UNAVAILABLE');
    expect(parsed.detail).toBe('Keycloak circuit breaker is open.');
    expect(parsed.retryAfterSeconds).toBe(15);
  });

  it('aggregates FluentValidation field errors when errors dictionary is present', async () => {
    const response = new Response(
      JSON.stringify({
        title: 'One or more validation errors occurred.',
        status: 400,
        errors: {
          Currency: ['Currency must be a 3-letter ISO code.'],
          Quantity: ['Quantity must be greater than zero.']
        }
      }),
      {
        status: 400,
        headers: { 'Content-Type': 'application/problem+json' }
      }
    );

    const parsed = await parseApiProblemError(response);

    expect(parsed.status).toBe(400);
    expect(parsed.detail).toContain('Currency must be a 3-letter ISO code.');
    expect(parsed.detail).toContain('Quantity must be greater than zero.');
  });
});
