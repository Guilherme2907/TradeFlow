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

output "workload_identity_client_id" {
  value       = module.aks.workload_identity_client_id
  description = "Managed Identity Client ID bound to TradeFlow AKS ServiceAccount."
}
