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

export async function apiStreamPost(
  path: string,
  body: unknown,
  onDataLine: (jsonPayload: string) => void,
  signal?: AbortSignal
): Promise<void> {
  const token = await getAccessToken();
  const headers = new Headers({
    'Content-Type': 'application/json',
    Accept: 'text/event-stream'
  });

  if (token) {
    headers.set('Authorization', `Bearer ${token}`);
  }

  const response = await fetch(`${apiBaseUrl}${path}`, {
    method: 'POST',
    headers,
    body: JSON.stringify(body),
    signal
  });

  if (!response.ok) {
    throw await parseApiProblemError(response);
  }

  if (!response.body) {
    return;
  }

  const reader = response.body.getReader();
  const decoder = new TextDecoder();
  let buffer = '';

  while (true) {
    const { value, done } = await reader.read();
    if (done) {
      break;
    }

    buffer += decoder.decode(value, { stream: true });
    const events = buffer.split('\n\n');
    buffer = events.pop() ?? '';

    for (const evt of events) {
      for (const line of evt.split('\n')) {
        if (line.startsWith('data:')) {
          const payload = line.slice('data:'.length).trim();
          if (payload) {
            onDataLine(payload);
          }
        }
      }
    }
  }
}
