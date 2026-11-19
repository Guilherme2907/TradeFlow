output "resource_group_name" {
  value       = azurerm_resource_group.this.name
  description = "Dev resource group name."
}

output "aks_cluster_name" {
  value       = module.aks.cluster_name
  description = "Dev AKS cluster name."
}

output "acr_login_server" {
  value       = module.aks.acr_login_server
  description = "Dev Azure Container Registry login server."
}

output "key_vault_uri" {
  value       = module.keyvault.key_vault_uri
  description = "Dev Azure Key Vault URI."
}

output "key_vault_name" {
  value       = module.keyvault.key_vault_name
  description = "Dev Azure Key Vault name."
}

output "tenant_id" {
  value       = data.azurerm_client_config.current.tenant_id
  description = "Azure Entra ID tenant ID."
}

output "workload_identity_client_id" {
  value       = module.aks.workload_identity_client_id
  description = "Managed Identity Client ID bound to TradeFlow AKS ServiceAccount."
}

output "application_insights_name" {
  value       = module.aks.application_insights_name
  description = "Dev Azure Application Insights resource name."
}

output "application_insights_connection_string" {
  value       = module.aks.application_insights_connection_string
  sensitive   = true
  description = "Dev Azure Application Insights connection string."
}
