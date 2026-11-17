import { expect, test } from '@playwright/test';

/**
 * ADR-0018 Critical Full-Stack E2E Journey:
 * Requires local stack running:
 * 1) docker compose up -d (postgres, redis, keycloak with custom tradeflow theme, rabbitmq, kafka, jaeger)
 * 2) dotnet run --project src/TradeFlow.Api (port 5022)
 * 3) npm run dev in src/TradeFlow.Web (port 5173)
 */
test('transparent Keycloak PKCE SSO login, order lifecycle execution, and F5 session persistence', async ({
  page
}) => {
  // 1. Navigate unauthenticated to React SPA
  await page.goto('/');

  // 2. Verify transparent redirect to custom white-label Keycloak login page
  await page.waitForURL(/localhost:8080\/realms\/tradeflow/);
  await expect(page.getByText('OIDC · PKCE PROTECTED SSO')).toBeVisible();

  await page.getByLabel(/username/i).fill('trader.alpha');
  await page.getByLabel(/password/i).fill('trader123');
  await page.getByRole('button', { name: /access trading desk/i }).click();

  // 3. Verify seamless redirect back to authenticated TradeFlow Dashboard
  await page.waitForURL('http://localhost:5173/');
  await expect(page.getByText(/Alice Trader \(trader\.alpha\)/i)).toBeVisible();
  await expect(page.getByRole('heading', { name: /purchase order book/i })).toBeVisible();

  // 4. Create Draft Order
  await page.getByRole('button', { name: /create draft order/i }).click();
  await expect(page.getByRole('heading', { name: /add sku line to draft/i })).toBeVisible();

  // 5. Add line item from seeded inventory and Submit Order
  await page.getByRole('button', { name: /add line snapshot/i }).click();
  const submitBtn = page.getByRole('button', { name: /^submit order$/i });
  await expect(submitBtn).toBeEnabled();
  await submitBtn.click();

  // 6. Assert Submitted badge & F5 reload session recovery via Silent Renew / PKCE SSO cookie
  await expect(page.getByLabel('Selected Order Details').getByText('Submitted')).toBeVisible();
  await page.reload();
  await expect(page.getByText(/Alice Trader \(trader\.alpha\)/i)).toBeVisible();
});
