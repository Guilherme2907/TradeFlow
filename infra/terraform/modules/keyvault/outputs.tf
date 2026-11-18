output "key_vault_id" {
  value       = azurerm_key_vault.this.id
  description = "Resource ID of the Azure Key Vault."
}

output "key_vault_uri" {
  value       = azurerm_key_vault.this.vault_uri
  description = "HTTPS URI of the Azure Key Vault."
}

output "key_vault_name" {
  value       = azurerm_key_vault.this.name
  description = "Name of the Azure Key Vault."
}
