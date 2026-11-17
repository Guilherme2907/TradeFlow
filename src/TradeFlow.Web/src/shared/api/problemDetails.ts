export interface ProblemDetails {
  type?: string;
  title?: string;
  status?: number;
  detail?: string;
  code?: string;
  errors?: Record<string, string[]>;
}

/**
 * Typed frontend representation of RFC 7807 ProblemDetails returned by TradeFlow.Api.
 * Isolates HTTP error parsing from UI components.
 */
export class ApiProblemError extends Error {
  public readonly status: number;
  public readonly code: string;
  public readonly detail: string;
  public readonly retryAfterSeconds?: number;

  constructor(
    status: number,
    code: string,
    detail: string,
    retryAfterSeconds?: number
  ) {
    super(detail);
    this.name = 'ApiProblemError';
    this.status = status;
    this.code = code;
    this.detail = detail;
    this.retryAfterSeconds = retryAfterSeconds;
  }
}

export function extractValidationSummary(problem: ProblemDetails): string | undefined {
  if (!problem.errors) {
    return undefined;
  }

  const messages = Object.values(problem.errors).flat().filter(Boolean);
  return messages.length > 0 ? messages.join(' ') : undefined;
}

export async function parseApiProblemError(response: Response): Promise<ApiProblemError> {
  const retryHeader = response.headers.get('Retry-After');
  const retryAfterSeconds = retryHeader ? Number.parseInt(retryHeader, 10) : undefined;

  try {
    const contentType = response.headers.get('content-type') ?? '';
    if (contentType.includes('json')) {
      const problem = (await response.json()) as ProblemDetails;
      const validationSummary = extractValidationSummary(problem);
      const detail =
        validationSummary ||
        problem.detail ||
        problem.title ||
        `HTTP ${response.status} error returned by TradeFlow API.`;
      const code = problem.code || `HTTP_${response.status}`;

      return new ApiProblemError(
        problem.status ?? response.status,
        code,
        detail,
        Number.isNaN(retryAfterSeconds) ? undefined : retryAfterSeconds
      );
    }
  } catch {
    // Fall back to status text when response body is not valid JSON
  }

  return new ApiProblemError(
    response.status,
    `HTTP_${response.status}`,
    response.statusText || `Request failed with status ${response.status}`,
    Number.isNaN(retryAfterSeconds) ? undefined : retryAfterSeconds
  );
}
