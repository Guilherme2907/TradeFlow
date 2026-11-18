output "server_id" {
  value       = azurerm_postgresql_flexible_server.this.id
  description = "Resource ID of the PostgreSQL Flexible Server."
}

output "fqdn" {
  value       = azurerm_postgresql_flexible_server.this.fqdn
  description = "Private FQDN of the PostgreSQL Flexible Server."
}

output "database_name" {
  value       = azurerm_postgresql_flexible_server_database.tradeflow.name
  description = "Name of the provisioned TradeFlow database."
}

output "administrator_login" {
  value       = var.administrator_login
  description = "PostgreSQL administrator username."
}

output "administrator_password" {
  value       = random_password.postgres_admin.result
  description = "Generated PostgreSQL administrator password (stored in Key Vault)."
  sensitive   = true
}
