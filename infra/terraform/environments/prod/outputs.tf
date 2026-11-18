output "resource_group_name" {
  value       = azurerm_resource_group.this.name
  description = "Production resource group name."
}

output "aks_cluster_name" {
  value       = module.aks.cluster_name
  description = "Production AKS cluster name."
}

output "acr_login_server" {
  value       = module.aks.acr_login_server
  description = "Production Azure Container Registry login server."
}

output "key_vault_uri" {
  value       = module.keyvault.key_vault_uri
  description = "Production Azure Key Vault URI."
}

output "workload_identity_client_id" {
  value       = module.aks.workload_identity_client_id
  description = "Managed Identity Client ID bound to Production TradeFlow AKS ServiceAccount."
}
