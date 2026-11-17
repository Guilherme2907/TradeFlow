import { parseApiProblemError } from './problemDetails';

export type AccessTokenProvider = () => Promise<string | null>;

let getAccessToken: AccessTokenProvider = async () => null;

/**
 * Registers the in-memory OIDC access token accessor without coupling HTTP calls to storage or React context.
 */
export function registerAccessTokenProvider(provider: AccessTokenProvider): void {
  getAccessToken = provider;
}

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL as string | undefined) ?? 'http://localhost:5022';

export async function apiFetch<TResponse>(
  path: string,
  options: RequestInit = {}
): Promise<TResponse> {
  const token = await getAccessToken();
  const headers = new Headers(options.headers);

  if (!headers.has('Content-Type') && options.body) {
    headers.set('Content-Type', 'application/json');
  }

  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...options,
    headers
  });

  if (!response.ok) {
    throw await parseApiProblemError(response);
  }

  if (response.status === 204) {
    return undefined as TResponse;
  }

  const text = await response.text();
  if (!text) {
    return undefined as TResponse;
  }

  return JSON.parse(text) as TResponse;
}
