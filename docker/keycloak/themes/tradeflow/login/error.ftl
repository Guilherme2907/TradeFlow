<!DOCTYPE html>
<html lang="pt-BR">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <title>Authentication Error — TradeFlow</title>
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
    <section class="tf-auth-card">
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
        <p class="tf-brand-subtitle">Identity Session Notice</p>
      </header>

      <div class="tf-alert tf-alert-error" role="alert">
        <#if message?has_content>
          ${kcSanitize(message.summary)?no_esc}
        <#else>
          An unexpected authentication error occurred.
        </#if>
      </div>

      <#if !skipLink?? && client?? && client.baseUrl?has_content>
        <a href="${client.baseUrl}" class="tf-btn-primary" style="display:flex;align-items:center;justify-content:center;text-decoration:none;">
          Return to TradeFlow Terminal
        </a>
      </#if>
    </section>
  </main>
</body>
</html>
