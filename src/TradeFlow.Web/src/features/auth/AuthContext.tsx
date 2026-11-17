import {
  createContext,
  useCallback,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode
} from 'react';
import type { User } from 'oidc-client-ts';
import { registerAccessTokenProvider } from '../../shared/api/httpClient';
import { AuthLandingScreen } from './AuthLandingScreen';
import { userManager } from './oidcConfig';

const IGNORED_KEYCLOAK_ROLES = new Set([
  'default-roles-tradeflow',
  'offline_access',
  'uma_authorization'
]);

export interface TradeFlowUserSession {
  username: string;
  email?: string;
  fullName: string;
  customerId?: string;
  roles: string[];
  accessToken: string;
  isPrivilegedRole: boolean;
  canWriteOrders: boolean;
  canCancelOrders: boolean;
}

interface AuthContextValue {
  session: TradeFlowUserSession;
  logout: () => Promise<void>;
}

const AuthContext = createContext<AuthContextValue | null>(null);

// Module-level guard prevents React 19 StrictMode double-mounting from consuming the single-use PKCE auth code twice.
let inflightCallbackPromise: Promise<User> | null = null;
let inflightSilentCallbackPromise: Promise<void> | null = null;

function parseJwtPayload(token: string): Record<string, unknown> {
  try {
    const parts = token.split('.');
    if (parts.length < 2) {
      return {};
    }

    const base64 = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const jsonPayload = decodeURIComponent(
      atob(base64)
        .split('')
        .map((c) => `%${`00${c.charCodeAt(0).toString(16)}`.slice(-2)}`)
        .join('')
    );

    return JSON.parse(jsonPayload) as Record<string, unknown>;
  } catch {
    return {};
  }
}

function buildUserSession(user: User): TradeFlowUserSession {
  const profile = user.profile as Record<string, unknown>;
  const accessClaims = parseJwtPayload(user.access_token);

  const rawRoles: string[] = [];
  const collectRoles = (candidate: unknown) => {
    if (Array.isArray(candidate)) {
      for (const item of candidate) {
        if (typeof item === 'string' && !IGNORED_KEYCLOAK_ROLES.has(item)) {
          rawRoles.push(item);
        }
      }
    }
  };

  collectRoles(profile.roles);
  collectRoles(accessClaims.roles);
  const realmAccess = accessClaims.realm_access as { roles?: unknown } | undefined;
  collectRoles(realmAccess?.roles);

  const roles = Array.from(new Set(rawRoles));
  const customerId =
    (typeof profile.customer_id === 'string' ? profile.customer_id : undefined) ??
    (typeof accessClaims.customer_id === 'string' ? accessClaims.customer_id : undefined);

  const username =
    (typeof profile.preferred_username === 'string' ? profile.preferred_username : undefined) ??
    'trader';

  const fullName =
    (typeof profile.name === 'string' ? profile.name : undefined) ?? username;

  const isPrivilegedRole =
    roles.includes('Admin') ||
    roles.includes('RiskManager') ||
    roles.includes('ComplianceAuditor');

  const canWriteOrders = roles.includes('Trader') || roles.includes('Admin');
  const canCancelOrders =
    roles.includes('Trader') ||
    roles.includes('RiskManager') ||
    roles.includes('Admin');

  return {
    username,
    email: typeof profile.email === 'string' ? profile.email : undefined,
    fullName,
    customerId,
    roles,
    accessToken: user.access_token,
    isPrivilegedRole,
    canWriteOrders,
    canCancelOrders
  };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<User | null>(null);
  const [status, setStatus] = useState<'loading' | 'authenticated' | 'unauthenticated'>('loading');
  const [statusMessage, setStatusMessage] = useState('Initializing encrypted SSO session...');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [isSignedOutView, setIsSignedOutView] = useState(false);

  useEffect(() => {
    registerAccessTokenProvider(async () => {
      const current = await userManager.getUser();
      if (!current || current.expired) {
        return null;
      }
      return current.access_token;
    });

    const onUserLoaded = (loadedUser: User) => {
      setUser(loadedUser);
      setStatus('authenticated');
      setErrorMessage(null);
    };

    const onUserUnloaded = () => {
      setUser(null);
      setStatus('unauthenticated');
    };

    userManager.events.addUserLoaded(onUserLoaded);
    userManager.events.addUserUnloaded(onUserUnloaded);

    return () => {
      userManager.events.removeUserLoaded(onUserLoaded);
      userManager.events.removeUserUnloaded(onUserUnloaded);
    };
  }, []);

  useEffect(() => {
    let active = true;

    async function bootstrapAuth() {
      const { pathname, search } = window.location;
      const searchParams = new URLSearchParams(search);

      // 1. Handle hidden iframe silent renew callback
      if (pathname === '/auth/silent-callback') {
        if (!inflightSilentCallbackPromise) {
          inflightSilentCallbackPromise = userManager.signinSilentCallback();
        }
        await inflightSilentCallbackPromise;
        return;
      }

      // 2. Handle PKCE redirect callback returning from Keycloak
      if (pathname === '/auth/callback') {
        setStatusMessage('Validating PKCE cryptographic handshake...');
        try {
          if (!inflightCallbackPromise) {
            inflightCallbackPromise = userManager.signinRedirectCallback();
          }
          const authenticatedUser = await inflightCallbackPromise;
          window.history.replaceState({}, document.title, '/');
          if (active) {
            setUser(authenticatedUser);
            setStatus('authenticated');
            setErrorMessage(null);
          }
        } catch (err) {
          window.history.replaceState({}, document.title, '/');
          if (active) {
            setStatus('unauthenticated');
            setErrorMessage(
              err instanceof Error
                ? `Failed to complete SSO sign-in: ${err.message}`
                : 'Failed to complete SSO sign-in.'
            );
          }
        }
        return;
      }

      // 3. Respect explicit post-logout landing without forcing immediate redirect back
      if (searchParams.get('logged_out') === '1') {
        if (active) {
          setIsSignedOutView(true);
          setStatus('unauthenticated');
        }
        return;
      }

      // 4. Check existing in-memory session
      const existing = await userManager.getUser();
      if (existing && !existing.expired) {
        if (active) {
          setUser(existing);
          setStatus('authenticated');
        }
        return;
      }

      // 5. Try silent SSO recovery (e.g., after F5 refresh clears RAM token)
      setStatusMessage('Restoring TradeFlow SSO session...');
      try {
        const silentUser = await userManager.signinSilent();
        if (silentUser && active) {
          setUser(silentUser);
          setStatus('authenticated');
          return;
        }
      } catch {
        // Silent iframe renew blocked or no active session yet — fall through to top-level SSO redirect
      }

      // 6. Automatically redirect to Keycloak white-label SSO gate
      setStatusMessage('Connecting to TradeFlow Identity Provider...');
      try {
        await userManager.signinRedirect();
      } catch (err) {
        if (active) {
          setStatus('unauthenticated');
          setErrorMessage(
            err instanceof Error
              ? `Identity Provider unreachable (${err.message}). Verify Keycloak is running on port 8080.`
              : 'Identity Provider unreachable.'
          );
        }
      }
    }

    void bootstrapAuth();

    return () => {
      active = false;
    };
  }, []);

  const handleManualLogin = useCallback(async () => {
    setErrorMessage(null);
    setIsSignedOutView(false);
    setStatus('loading');
    setStatusMessage('Redirecting to TradeFlow Identity Provider...');
    try {
      await userManager.signinRedirect();
    } catch (err) {
      setStatus('unauthenticated');
      setErrorMessage(
        err instanceof Error
          ? `Failed to reach Keycloak (${err.message}).`
          : 'Failed to reach Keycloak.'
      );
    }
  }, []);

  const logout = useCallback(async () => {
    setStatus('loading');
    setStatusMessage('Closing SSO session...');
    try {
      await userManager.signoutRedirect();
    } catch {
      await userManager.removeUser();
      setUser(null);
      setIsSignedOutView(true);
      setStatus('unauthenticated');
    }
  }, []);

  const contextValue = useMemo<AuthContextValue | null>(() => {
    if (!user || status !== 'authenticated') {
      return null;
    }

    return {
      session: buildUserSession(user),
      logout
    };
  }, [user, status, logout]);

  if (status === 'loading') {
    return (
      <AuthLandingScreen
        isLoading
        statusMessage={statusMessage}
        onLoginClick={handleManualLogin}
      />
    );
  }

  if (!contextValue) {
    return (
      <AuthLandingScreen
        statusMessage={statusMessage}
        isSignedOut={isSignedOutView}
        errorMessage={errorMessage}
        onLoginClick={handleManualLogin}
      />
    );
  }

  return <AuthContext.Provider value={contextValue}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error('useAuth must be used inside an authenticated AuthProvider tree.');
  }
  return ctx;
}
