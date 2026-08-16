import { PublicClientApplication, type AuthenticationResult } from '@azure/msal-browser'

/**
 * Microsoft sign-in, via the redirect flow rather than a popup: no CSP frame-src exception
 * is needed, and it behaves the same whether or not the browser blocks popups. MSAL is
 * bundled (this file), not loaded from a CDN, so it needs no script-src relaxation either —
 * only the token endpoint itself (connect-src https://login.microsoftonline.com) does.
 *
 * The `common` authority accepts both work/school and personal Microsoft accounts, matching
 * the "any Microsoft account" trust model used here — unknown accounts land in the same
 * admin-approval queue as everyone else, exactly like Google sign-in.
 */
const CLIENT_ID = import.meta.env.VITE_MICROSOFT_CLIENT_ID as string | undefined

let instance: PublicClientApplication | null = null
let initialized: Promise<PublicClientApplication> | null = null

export const microsoftSignInConfigured = Boolean(CLIENT_ID)

function create(): PublicClientApplication {
  return new PublicClientApplication({
    auth: {
      clientId: CLIENT_ID!,
      authority: 'https://login.microsoftonline.com/common',
      // Returning to /login keeps the redirect-handling code in exactly one place.
      redirectUri: `${window.location.origin}/login`,
      postLogoutRedirectUri: `${window.location.origin}/login`,
    },
    cache: {
      // localStorage, not the default sessionStorage: the full-page navigation to Microsoft
      // and back is not guaranteed to stay in the same tab/session on every browser.
      cacheLocation: 'localStorage',
    },
  })
}

/** Lazily creates and initialises the singleton MSAL client. Safe to call repeatedly. */
async function getMsal(): Promise<PublicClientApplication> {
  if (!CLIENT_ID) throw new Error('Microsoft sign-in is not configured (VITE_MICROSOFT_CLIENT_ID is unset).')
  instance ??= create()
  initialized ??= instance.initialize().then(() => instance!)
  return initialized
}

/** Starts the redirect to Microsoft. The browser navigates away; nothing returns from this call. */
export async function startMicrosoftSignIn(): Promise<void> {
  const msal = await getMsal()
  await msal.loginRedirect({ scopes: ['openid', 'profile', 'email'] })
}

/**
 * Call once on the sign-in page's mount. Resolves the pending redirect (if any) and returns
 * the ID token to exchange with the backend, or null if the page was not reached via a
 * Microsoft redirect.
 */
export async function completeMicrosoftSignIn(): Promise<string | null> {
  if (!CLIENT_ID) return null
  const msal = await getMsal()
  const result: AuthenticationResult | null = await msal.handleRedirectPromise()
  return result?.idToken ?? null
}

/**
 * True only when the current URL carries the parameters Microsoft's redirect adds
 * (`code=`, `error=`, `client_info=`, …). Used to decide, before MSAL has even initialised,
 * whether the sign-in form should be held back for a moment — an ordinary visit to /login
 * must never be delayed by this check.
 */
export function looksLikeMicrosoftRedirectResponse(): boolean {
  const carrier = window.location.hash || window.location.search
  return /(^|[#?&])(code|error|client_info)=/.test(carrier)
}
