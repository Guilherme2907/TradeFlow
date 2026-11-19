output "cluster_id" {
  value       = azurerm_kubernetes_cluster.this.id
  description = "Resource ID of the AKS cluster."
}

output "cluster_name" {
  value       = azurerm_kubernetes_cluster.this.name
  description = "Name of the AKS cluster."
}

output "oidc_issuer_url" {
  value       = azurerm_kubernetes_cluster.this.oidc_issuer_url
  description = "OIDC Issuer URL used for Workload Identity federation."
}

output "acr_login_server" {
  value       = azurerm_container_registry.this.login_server
  description = "Login server URL of the Azure Container Registry."
}

output "workload_identity_client_id" {
  value       = azurerm_user_assigned_identity.tradeflow_api.client_id
  description = "Client ID of the TradeFlow API Workload Identity for Kubernetes ServiceAccount annotation."
}

output "application_insights_name" {
  value       = azurerm_application_insights.this.name
  description = "Name of the Azure Application Insights resource."
}

output "application_insights_connection_string" {
  value       = azurerm_application_insights.this.connection_string
  sensitive   = true
  description = "Connection string for Azure Application Insights OpenTelemetry exporter."
}
