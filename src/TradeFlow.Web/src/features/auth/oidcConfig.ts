import {
  InMemoryWebStorage,
  UserManager,
  WebStorageStateStore,
  type UserManagerSettings
} from 'oidc-client-ts';

const authority =
  (import.meta.env.VITE_KEYCLOAK_AUTHORITY as string | undefined) ??
  'http://localhost:8080/realms/tradeflow';
const clientId =
  (import.meta.env.VITE_KEYCLOAK_CLIENT_ID as string | undefined) ??
  'tradeflow-web';

const origin = typeof window !== 'undefined' ? window.location.origin : 'http://localhost:5173';

/**
 * ADR-0018:
 * - Authorization Code Flow + PKCE (S256)
 * - Access/ID/Refresh tokens live STRICTLY in RAM (InMemoryWebStorage) to eliminate localStorage XSS persistence.
 * - Ephemeral PKCE state/code_verifier lives transiently in default sessionStorage during top-level Keycloak redirect
 *   and is deleted immediately upon /auth/callback completion.
 */
export const oidcSettings: UserManagerSettings = {
  authority,
  client_id: clientId,
  redirect_uri: `${origin}/auth/callback`,
  post_logout_redirect_uri: `${origin}/?logged_out=1`,
  silent_redirect_uri: `${origin}/auth/silent-callback`,
  response_type: 'code',
  scope: 'openid profile email',
  automaticSilentRenew: true,
  userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() })
};

export const userManager = new UserManager(oidcSettings);
