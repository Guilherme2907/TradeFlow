<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Sign In — TradeFlow Institutional Terminal</title>
  <link rel="preconnect" href="https://fonts.googleapis.com" />
  <link rel="preconnect" href="https://fonts.gstatic.com" crossorigin />
  <link href="https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600;700&family=JetBrains+Mono:wght@400;600&display=swap" rel="stylesheet" />
  <#if properties.styles?has_content>
    <#list properties.styles?split(' ') as style>
      <link href="${url.resourcesPath}/${style}" rel="stylesheet" />
    </#list>
  </#if>
</head>
<body>
  <main class="tf-auth-shell">
    <section class="tf-auth-card" aria-label="TradeFlow Institutional Authentication">
      <header class="tf-brand-header">
        <div class="tf-brand-logo">
          <div class="tf-brand-icon" aria-hidden="true">
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" stroke="#022c22" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">
              <polyline points="22 7 13.5 15.5 8.5 10.5 2 17"></polyline>
              <polyline points="16 7 22 7 22 13"></polyline>
            </svg>
          </div>
          <span class="tf-brand-name">TradeFlow</span>
        </div>
        <p class="tf-brand-subtitle">Institutional B2B Trading &amp; Liquidity Terminal</p>
        <div class="tf-security-pill">
          <span class="tf-security-dot" aria-hidden="true"></span>
          <span>OIDC · PKCE PROTECTED SSO</span>
        </div>
      </header>

      <#if message?has_content && (message.type != 'warning' || !isAppInitiatedAction??)>
        <div class="tf-alert tf-alert-${message.type}" role="alert">
          ${kcSanitize(message.summary)?no_esc}
        </div>
      </#if>

      <form id="kc-form-login" action="${url.loginAction}" method="post">
        <#if !usernameHidden??>
          <div class="tf-field">
            <label for="username" class="tf-label">
              <#if !realm.loginWithEmailAllowed>${msg("username")}<#elseif !realm.registrationEmailAsUsername>${msg("usernameOrEmail")}<#else>${msg("email")}</#if>
            </label>
            <input
              tabindex="1"
              id="username"
              class="tf-input"
              name="username"
              value="${(login.username!'')}"
              type="text"
              autofocus
              autocomplete="username"
              placeholder="trader.alpha"
              aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"
            />
            <#if messagesPerField.existsError('username','password')>
              <span id="input-error" class="tf-field-error" aria-live="polite">
                ${kcSanitize(messagesPerField.getFirstError('username','password'))?no_esc}
              </span>
            </#if>
          </div>
        </#if>

        <div class="tf-field">
          <label for="password" class="tf-label">${msg("password")}</label>
          <input
            tabindex="2"
            id="password"
            class="tf-input"
            name="password"
            type="password"
            autocomplete="current-password"
            placeholder="••••••••"
            aria-invalid="<#if messagesPerField.existsError('username','password')>true</#if>"
          />
        </div>

        <#if realm.rememberMe && !usernameHidden?? || realm.resetPasswordAllowed>
          <div class="tf-row-between">
            <#if realm.rememberMe && !usernameHidden??>
              <label class="tf-checkbox-label">
                <input tabindex="3" id="rememberMe" name="rememberMe" type="checkbox" <#if login.rememberMe??>checked</#if> />
                <span>${msg("rememberMe")}</span>
              </label>
            </#if>
            <#if realm.resetPasswordAllowed>
              <a tabindex="5" href="${url.loginResetCredentialsUrl}" class="tf-link">${msg("doForgotPassword")}</a>
            </#if>
          </div>
        </#if>

        <input type="hidden" id="id-hidden-input" name="credentialId" <#if auth.selectedCredential?has_content>value="${auth.selectedCredential}"</#if> />
        <button tabindex="4" name="login" id="kc-login" type="submit" class="tf-btn-primary">
          Access Trading Desk
        </button>
      </form>

      <div class="tf-sandbox-box">
        <p class="tf-sandbox-title">Quick Fill — Sandbox Personas</p>
        <div class="tf-sandbox-accounts">
          <button type="button" class="tf-sandbox-btn" onclick="tfFillCredentials('trader.alpha', 'trader123')">
            <span class="tf-sandbox-user">trader.alpha</span>
            <span class="tf-sandbox-role">Trader · Alpha Corp</span>
          </button>
          <button type="button" class="tf-sandbox-btn" onclick="tfFillCredentials('trader.beta', 'trader123')">
            <span class="tf-sandbox-user">trader.beta</span>
            <span class="tf-sandbox-role">Trader · Beta Ind</span>
          </button>
          <button type="button" class="tf-sandbox-btn" onclick="tfFillCredentials('risk.manager', 'risk123')">
            <span class="tf-sandbox-user">risk.manager</span>
            <span class="tf-sandbox-role">RiskManager · Cross-Tenant</span>
          </button>
          <button type="button" class="tf-sandbox-btn" onclick="tfFillCredentials('admin.user', 'admin123')">
            <span class="tf-sandbox-user">admin.user</span>
            <span class="tf-sandbox-role">Admin · Full Access</span>
          </button>
        </div>
      </div>
    </section>

    <footer class="tf-footer">
      Secured by Keycloak OAuth2 / OpenID Connect · TradeFlow Platform
    </footer>
  </main>

  <script>
    function tfFillCredentials(user, pass) {
      var u = document.getElementById('username');
      var p = document.getElementById('password');
      if (u) u.value = user;
      if (p) p.value = pass;
    }
  </script>
</body>
</html>
